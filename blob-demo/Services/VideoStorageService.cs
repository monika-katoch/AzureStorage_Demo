using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using blob_demo.Models;
using Microsoft.Extensions.Options;

namespace blob_demo.Services;

public record VideoUploadResult(bool Success, string Message, string? BlobName = null);

/// <summary>A seekable stream over a blob, plus what the player needs to serve it.</summary>
public record VideoStream(Stream Content, string ContentType, long Length);

public interface IVideoStorageService
{
    Task EnsureContainerAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VideoViewModel>> ListAsync(CancellationToken ct = default);
    Task<VideoUploadResult> UploadAsync(IFormFile file, CancellationToken ct = default);

    /// <summary>
    /// Opens a SEEKABLE read stream over the blob. The key to range streaming:
    /// the stream reports Length and supports Seek, so ASP.NET Core can serve any
    /// byte range without ever holding the whole file in memory.
    /// </summary>
    Task<VideoStream?> OpenReadAsync(string blobName, CancellationToken ct = default);
}

public class VideoStorageService : IVideoStorageService
{
    // Video-only whitelist with leading signatures (mp4 'ftyp' sits at offset 4).
    private static readonly Dictionary<string, (int offset, byte[] sig)> Allow = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"]  = (4, new byte[] { 0x66, 0x74, 0x79, 0x70 }),        // ....ftyp
        [".m4v"]  = (4, new byte[] { 0x66, 0x74, 0x79, 0x70 }),
        [".webm"] = (0, new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),        // EBML
    };

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".webm"] = "video/webm",
    };

    private readonly BlobContainerClient _container;
    private readonly VideoStorageOptions _options;
    private readonly ILogger<VideoStorageService> _logger;

    public VideoStorageService(
        BlobServiceClient serviceClient,
        IOptions<VideoStorageOptions> options,
        ILogger<VideoStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _container = serviceClient.GetBlobContainerClient(_options.ContainerName);
    }

    public async Task EnsureContainerAsync(CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _logger.LogInformation("Video container '{Container}' is ready.", _options.ContainerName);
    }

    public async Task<IReadOnlyList<VideoViewModel>> ListAsync(CancellationToken ct = default)
    {
        var list = new List<VideoViewModel>();
        await foreach (BlobItem item in _container.GetBlobsAsync(cancellationToken: ct))
        {
            list.Add(new VideoViewModel
            {
                Name = item.Name,
                SizeBytes = item.Properties.ContentLength ?? 0,
                ContentType = item.Properties.ContentType ?? "video/mp4",
                LastModified = item.Properties.LastModified,
            });
        }
        return list.OrderByDescending(v => v.LastModified).ToList();
    }

    public async Task<VideoUploadResult> UploadAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return new VideoUploadResult(false, "No file was provided.");

        if (file.Length > _options.MaxFileSizeBytes)
            return new VideoUploadResult(false, $"File exceeds the {_options.MaxFileSizeDisplay()} limit.");

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !Allow.TryGetValue(ext, out var rule))
            return new VideoUploadResult(false, $"Only {string.Join(", ", Allow.Keys)} videos are allowed.");

        await using var stream = file.OpenReadStream();

        // Signature sniff so a renamed file can't masquerade as a video.
        if (stream.CanSeek)
        {
            var head = new byte[16];
            int read = stream.Read(head, 0, head.Length);
            stream.Position = 0;
            bool ok = read >= rule.offset + rule.sig.Length &&
                      head.Skip(rule.offset).Take(rule.sig.Length).SequenceEqual(rule.sig);
            if (!ok)
                return new VideoUploadResult(false, "File content is not a valid video (signature mismatch).");
        }

        var uniqueName = $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";
        var blob = _container.GetBlobClient(uniqueName);

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = ContentTypes[ext] },
            // Large-file behavior: upload in 8 MB blocks, 4 in parallel, so a 2 GB
            // file streams up in chunks instead of buffering entirely in memory.
            TransferOptions = new StorageTransferOptions
            {
                InitialTransferSize = 8 * 1024 * 1024,
                MaximumTransferSize = 8 * 1024 * 1024,
                MaximumConcurrency = 4
            }
        };

        try
        {
            await blob.UploadAsync(stream, options, ct);
            _logger.LogInformation("Uploaded video '{Blob}' ({Bytes} bytes).", uniqueName, file.Length);
            return new VideoUploadResult(true, $"Uploaded '{Path.GetFileName(file.FileName)}'.", uniqueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video upload failed for '{File}'.", file.FileName);
            return new VideoUploadResult(false, "Upload failed. Is Azurite running?");
        }
    }

    public async Task<VideoStream?> OpenReadAsync(string blobName, CancellationToken ct = default)
    {
        var safeName = Path.GetFileName(blobName);
        var blob = _container.GetBlobClient(safeName);

        if (!await blob.ExistsAsync(ct))
            return null;

        BlobProperties props = await blob.GetPropertiesAsync(cancellationToken: ct);

        // OpenReadAsync returns a CanSeek stream that fetches blocks on demand as the
        // player seeks — no full download. This is what makes range streaming work.
        Stream stream = await blob.OpenReadAsync(
            new BlobOpenReadOptions(allowModifications: false) { BufferSize = 1 * 1024 * 1024 }, ct);

        var contentType = props.ContentType ?? "application/octet-stream";
        return new VideoStream(stream, contentType, props.ContentLength);
    }
}

internal static class VideoOptionsExtensions
{
    public static string MaxFileSizeDisplay(this VideoStorageOptions o) =>
        $"{o.MaxFileSizeBytes / (1024L * 1024 * 1024):0.##} GB";
}
