namespace TechieDesk.Services.Data;

/// <summary>
/// Why one uploaded file was refused, as a resource KEY plus the values that fill it
/// (REQ-UI-055 / BRD-91).
/// </summary>
/// <param name="MessageKey">A key in <c>AppStrings.resx</c>.</param>
/// <param name="Arguments">Format arguments for that key; empty when it carries no placeholder.</param>
/// <remarks>
/// The key/arguments split is what lets the surface render the refusal with
/// <c>Localizer[rejection.MessageKey, rejection.Arguments]</c>. The arguments are the file name and
/// the extension — values, not words — so they are the same in every culture.
/// </remarks>
public sealed record UploadRejection(string MessageKey, object[] Arguments);

/// <summary>
/// Decides which uploaded file types the document library accepts and which it rejects up front.
/// </summary>
/// <remarks>
/// <para>
/// REQ-RAG-011 / BRD-41: every type backed by a TechieRag processor is accepted — including the
/// XLSX, PPTX and CSV formats delivered by REQ-RAG-033 — while genuinely unsupported binary
/// formats get a clear, friendly per-file rejection instead of a crash or a silent failure.
/// </para>
/// <para>
/// <b>REQ-UI-055 / BRD-91.</b> The refusal is returned as a resource KEY, never as English, for the
/// reason <c>DataStorageInspector</c> records: a static policy class sits outside the razor tree and
/// therefore outside both localization counters, so an English sentence built here reaches a Hindi
/// window with nothing to catch it. The TYPE vocabulary — <see cref="AcceptTypes"/> and the
/// unsupported-extension set — is deliberately NOT keyed: those are file-system tokens matched
/// against a file name and handed to the OS file picker, and a culture that moved them would make
/// the picker offer filters no file on disk matches.
/// </para>
/// </remarks>
public static class UploadTypePolicy
{
    /// <summary>Resource key for an upload that arrived with no file name at all.</summary>
    public const string NoFileNameKey = "UploadRejectedNoFileName";

    /// <summary>Resource key for a file whose name carries no extension. Takes the file name.</summary>
    public const string NoExtensionKey = "UploadRejectedNoExtension";

    /// <summary>Resource key for an extension with no processor behind it. Takes the extension.</summary>
    public const string UnsupportedTypeKey = "UploadRejectedUnsupportedType";

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
        return GetRejection(fileName) is null;
    }

    /// <summary>
    /// Builds the rejection for an unsupported upload, as a key the surface resolves.
    /// </summary>
    /// <param name="fileName">The uploaded file name.</param>
    /// <returns>The rejection, or null when the file is supported.</returns>
    public static UploadRejection? GetRejection(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new UploadRejection(NoFileNameKey, []);
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            return new UploadRejection(NoExtensionKey, [fileName]);
        }

        if (UnsupportedBinaryExtensions.Contains(extension))
        {
            return new UploadRejection(UnsupportedTypeKey, [extension]);
        }

        return null;
    }
}
