namespace blob_demo.Models;

/// <summary>
/// Strongly-typed binding for the "BlobStorage" section of appsettings.json.
/// NFR-2: keeps the connection string out of source code.
/// </summary>
public class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true"; //by default connectionstring 
    public string ContainerName { get; set; } = "demo-uploads"; // default folder name

    /// <summary>FR-2.2: hard upload ceiling (default 50 MB).</summary>
    public long MaxFileSizeBytes { get; set; } = 52_428_800;

    /// <summary>FR-3.2: SAS token lifetime.</summary>
    public int SasExpiryMinutes { get; set; } = 15; // 15mins timeframe to make stored data available publicly after the given time framed expire it will not be accessible anymore
}
