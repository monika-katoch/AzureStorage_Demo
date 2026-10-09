using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using blob_demo.Models;
using Microsoft.Extensions.Options;

namespace blob_demo.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;
    private readonly BlobStorageOptions _options;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(
        BlobServiceClient serviceClient,
        IOptions<BlobStorageOptions> options,
        ILogger<BlobStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _container = serviceClient.GetBlobContainerClient(_options.ContainerName);
    }

    public async Task EnsureContainerAsync(CancellationToken ct = default)
    {
        // Edge case (data leaks): always create the container Private.
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _logger.LogInformation("Container '{Container}' is ready.", _options.ContainerName);
    }

    public async Task<IReadOnlyList<BlobFileViewModel>> ListFilesAsync(CancellationToken ct = default)
    {
        var results = new List<BlobFileViewModel>();
        await foreach (BlobItem item in _container.GetBlobsAsync(cancellationToken: ct))
        {
            results.Add(new BlobFileViewModel
            {
                Name = item.Name,
                SizeBytes = item.Properties.ContentLength ?? 0,
                ContentType = item.Properties.ContentType ?? "application/octet-stream",
                LastModified = item.Properties.LastModified,
            });
        }
        return results.OrderByDescending(f => f.LastModified).ToList();
    }

    public async Task<UploadResult> UploadAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return new UploadResult(false, "No file was provided.");

        // FR-2.2: reject oversize uploads before touching blob storage.
        if (file.Length > _options.MaxFileSizeBytes)
        {
            var limitMb = _options.MaxFileSizeBytes / (1024 * 1024);
            return new UploadResult(false, $"File exceeds the {limitMb} MB limit.");
        }

        await using var stream = file.OpenReadStream();

        // Malicious-upload defense: whitelist + magic-number check.
        if (!FileValidator.IsAllowed(file.FileName, stream, out var validationError))
            return new UploadResult(false, validationError);

        // FR-2.3 + duplicate/overwrite edge case: sanitize the name and prefix a
        // GUID so a user-supplied name can neither traverse paths nor clobber an
        // existing blob.
        var safeName = Path.GetFileName(file.FileName);          // strips ../ segments
        var uniqueName = $"{Guid.NewGuid():N}_{safeName}";

        var blob = _container.GetBlobClient(uniqueName);
        var headers = new BlobHttpHeaders
        {
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType
        };

        // NFR-3: the SDK automatically splits blobs larger than the threshold into
        // parallel block uploads; StorageTransferOptions tunes that behavior.
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = headers,
            TransferOptions = new Azure.Storage.StorageTransferOptions
            {
                InitialTransferSize = 8 * 1024 * 1024,
                MaximumTransferSize = 8 * 1024 * 1024,
                MaximumConcurrency = 4
            },
            ProgressHandler = new Progress<long>(x =>
            {

            })
        };

        try
        {
            await blob.UploadAsync(stream, uploadOptions, ct);
            _logger.LogInformation("Uploaded blob '{Blob}' ({Bytes} bytes).", uniqueName, file.Length);
            return new UploadResult(true, $"Uploaded '{safeName}'.", uniqueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload failed for '{File}'.", file.FileName);
            return new UploadResult(false, "Upload failed. Check that Azurite is running.");
        }
    }

    public async Task<BlobDownload?> DownloadAsync(string blobName, CancellationToken ct = default)
    {
        var safeName = Path.GetFileName(blobName); // never trust the routed value
        var blob = _container.GetBlobClient(safeName);

        if (!await blob.ExistsAsync(ct))
            return null;

        BlobDownloadStreamingResult result = await blob.DownloadStreamingAsync(cancellationToken: ct);
        var contentType = result.Details.ContentType ?? "application/octet-stream";
        return new BlobDownload(result.Content, contentType, safeName);
    }

    public Uri? GenerateReadSasUri(string blobName)
    {
        var safeName = Path.GetFileName(blobName);
        var blob = _container.GetBlobClient(safeName);

        // Requires a shared-key credential (present with Azurite's dev connection
        // string). In production behind managed identity, use a user-delegation SAS.
        if (!blob.CanGenerateSasUri)
        {
            _logger.LogWarning("Cannot generate SAS: client lacks a shared-key credential.");
            return null;
        }

        var builder = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = safeName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(_options.SasExpiryMinutes)
        };
        builder.SetPermissions(BlobSasPermissions.Read); // read-only, short-lived

        return blob.GenerateSasUri(builder);
    }
}
