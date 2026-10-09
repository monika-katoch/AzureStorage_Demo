namespace blob_demo.Models;

/// <summary>Binds the "VideoStorage" section — a separate container for large media.</summary>
public class VideoStorageOptions
{
    public const string SectionName = "VideoStorage";

    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";
    public string ContainerName { get; set; } = "demo-videos";

    /// <summary>Large ceiling (default 2 GB) — videos are big.</summary>
    public long MaxFileSizeBytes { get; set; } = 2_147_483_648;
}

/// <summary>Metadata for a stored video, shown on the Videos page.</summary>
public class VideoViewModel
{
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "video/mp4";
    public DateTimeOffset? LastModified { get; set; }

    public string SizeDisplay
    {
        get
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double s = SizeBytes; int u = 0;
            while (s >= 1024 && u < units.Length - 1) { s /= 1024; u++; }
            return $"{s:0.##} {units[u]}";
        }
    }
}

public class VideoPageViewModel
{
    public string ContainerName { get; set; } = string.Empty;
    public long MaxFileSizeBytes { get; set; }
    public IReadOnlyList<VideoViewModel> Videos { get; set; } = new List<VideoViewModel>();
    public string? NowPlaying { get; set; }

    public string MaxFileSizeDisplay => $"{MaxFileSizeBytes / (1024L * 1024 * 1024):0.##} GB";
}
