namespace blob_demo.Models;

/// <summary>Metadata for a single blob, shown in the asset browser (FR-1.2).</summary>
public class BlobFileViewModel
{
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public DateTimeOffset? LastModified { get; set; }

    public string SizeDisplay
    {
        get
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double size = SizeBytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return $"{size:0.##} {units[unit]}";
        }
    }
}

/// <summary>Backing model for the Files/Index view.</summary>
public class FileListViewModel
{
    public string ContainerName { get; set; } = string.Empty;
    public long MaxFileSizeBytes { get; set; }
    public IReadOnlyList<BlobFileViewModel> Files { get; set; } = new List<BlobFileViewModel>();

    public string MaxFileSizeDisplay => $"{MaxFileSizeBytes / (1024 * 1024)} MB";
}
