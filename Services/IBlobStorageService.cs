using blob_demo.Models;

namespace blob_demo.Services;

public record UploadResult(bool Success, string Message, string? BlobName = null);

public record BlobDownload(Stream Content, string ContentType, string FileName);

public interface IBlobStorageService
{
    /// <summary>FR-1.1: create the default container on startup if absent.</summary>
    Task EnsureContainerAsync(CancellationToken ct = default);

    /// <summary>FR-1.2: list files (with metadata) in the active container.</summary>
    Task<IReadOnlyList<BlobFileViewModel>> ListFilesAsync(CancellationToken ct = default);

    /// <summary>FR-2: validate then upload an uploaded form file.</summary>
    Task<UploadResult> UploadAsync(IFormFile file, CancellationToken ct = default);

    /// <summary>FR-3.1: stream a blob back for download.</summary>
    Task<BlobDownload?> DownloadAsync(string blobName, CancellationToken ct = default);

    /// <summary>FR-3.2: generate a time-limited read-only SAS URL.</summary>
    Uri? GenerateReadSasUri(string blobName);
}
