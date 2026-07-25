namespace TechieDesk.Services.Data;

/// <summary>
/// Decides which uploaded file types the document library accepts and which it rejects up front.
/// </summary>
/// <remarks>
/// REQ-RAG-011 / BRD-41: every type backed by a TechieRag processor is accepted — including the
/// XLSX, PPTX and CSV formats delivered by REQ-RAG-033 — while genuinely unsupported binary
/// formats get a clear, friendly per-file rejection instead of a crash or a silent failure.
/// </remarks>
public static class UploadTypePolicy
{
    /// <summary>
    /// The file-picker accept filter listing the commonly uploaded supported extensions.
    /// </summary>
    public const string AcceptTypes =
        ".pdf,.docx,.xlsx,.pptx,.txt,.md,.markdown,.html,.htm,.json,.toml,.csv,.tsv," +
        ".xml,.yaml,.yml,.cs,.js,.ts,.py,.java,.go,.rs,.cpp,.c,.sql";

    /// <summary>
    /// Binary formats with no text-extraction processor behind them.
    /// </summary>
    /// <remarks>
    /// The legacy Office formats (.xls, .ppt, .doc) stay here deliberately: REQ-RAG-033 added
    /// OpenXml processors for the modern .xlsx/.pptx/.docx containers only.
    /// </remarks>
    private static readonly HashSet<string> UnsupportedBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tiff", ".tif", ".webp", ".psd", ".raw",
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a",
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v",
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".cab",
        ".exe", ".dll", ".so", ".dylib", ".bin", ".msi", ".app",
        ".xls", ".ppt", ".doc",
        ".db", ".sqlite", ".mdb", ".accdb",
        ".class", ".jar", ".war", ".ear", ".onnx"
    };

    /// <summary>
    /// Determines whether a file name carries an extension the library can ingest.
    /// </summary>
    /// <param name="fileName">The uploaded file name.</param>
    /// <returns>True when the file should proceed to ingestion.</returns>
    public static bool IsSupported(string fileName)
    {
        return GetRejectionReason(fileName) is null;
    }

    /// <summary>
    /// Builds the user-facing rejection message for an unsupported upload.
    /// </summary>
    /// <param name="fileName">The uploaded file name.</param>
    /// <returns>The rejection reason, or null when the file is supported.</returns>
    public static string? GetRejectionReason(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Unsupported file — the upload has no file name.";
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            return $"Unsupported file — '{fileName}' has no extension, so its format can't be determined.";
        }

        if (UnsupportedBinaryExtensions.Contains(extension))
        {
            return $"Unsupported type — no processor for {extension}.";
        }

        return null;
    }
}
