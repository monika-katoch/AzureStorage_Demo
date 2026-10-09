namespace blob_demo.Services;

/// <summary>
/// Defends the upload path (edge case: malicious file uploads).
/// Combines an extension whitelist with magic-number (file signature) sniffing
/// so a "malware.png.exe" or a spoofed content-type is rejected before it ever
/// reaches blob storage.
/// </summary>
public static class FileValidator
{
    // Allowed extensions -> the byte signatures we accept for that extension.
    // An empty signature list means "extension is allowed, signature not checked"
    // (used for plain-text formats that have no reliable magic number).
    private static readonly Dictionary<string, byte[][]> AllowList = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"]  = new[] { new byte[] { 0x25, 0x50, 0x44, 0x46 } },                 // %PDF
        [".png"]  = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } },                 // .PNG
        [".jpg"]  = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".gif"]  = new[] { new byte[] { 0x47, 0x49, 0x46, 0x38 } },                 // GIF8
        [".mp4"]  = new[] { new byte[] { 0x66, 0x74, 0x79, 0x70 } },                 // 'ftyp' at offset 4
        [".zip"]  = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },                 // PK..
        [".txt"]  = Array.Empty<byte[]>(),
        [".csv"]  = Array.Empty<byte[]>(),
        [".json"] = Array.Empty<byte[]>(),
    };

    public static IEnumerable<string> AllowedExtensions => AllowList.Keys;

    /// <summary>
    /// Returns true when the extension is whitelisted AND the leading bytes match
    /// a known signature for that extension. On mismatch, <paramref name="error"/>
    /// explains why.
    /// </summary>
    public static bool IsAllowed(string fileName, Stream content, out string error)
    {
        error = string.Empty;
        var ext = Path.GetExtension(fileName);

        if (string.IsNullOrWhiteSpace(ext) || !AllowList.TryGetValue(ext, out var signatures))
        {
            error = $"File type '{ext}' is not permitted. Allowed: {string.Join(", ", AllowList.Keys)}.";
            return false;
        }

        // Text formats: extension check is sufficient.
        if (signatures.Length == 0)
            return true;

        if (!content.CanSeek)
        {
            error = "Cannot validate file signature on a non-seekable stream.";
            return false;
        }

        var header = new byte[16];
        long original = content.Position;
        content.Position = 0;
        int read = content.Read(header, 0, header.Length);
        content.Position = original;

        foreach (var sig in signatures)
        {
            // mp4's 'ftyp' marker sits at offset 4; everything else at offset 0.
            int offset = ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ? 4 : 0;
            if (read >= offset + sig.Length && MatchesAt(header, sig, offset))
                return true;
        }

        error = $"File content does not match its '{ext}' extension (possible disguised file).";
        return false;
    }

    private static bool MatchesAt(byte[] header, byte[] signature, int offset)
    {
        for (int i = 0; i < signature.Length; i++)
        {
            if (header[offset + i] != signature[i])
                return false;
        }
        return true;
    }
}
