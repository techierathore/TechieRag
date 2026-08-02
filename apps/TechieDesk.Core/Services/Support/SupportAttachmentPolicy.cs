using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Support;

/// <summary>
/// Decides which files may be attached to a support issue or a comment, and how large they may be
/// (REQ-UI-047 / BRD-141).
/// </summary>
/// <remarks>
/// <para>
/// This is an <b>allowlist</b>, not a denylist. <see cref="TechieDesk.Services.Data.UploadTypePolicy"/>
/// — the document library's rule — can afford to name the formats it rejects because everything it accepts ends up
/// in a text extractor. An attachment is different: it is a file this app writes to disk under the
/// data directory and later hands to a support engineer, so anything not explicitly named is
/// refused rather than tolerated. The set is exactly what the requirement asked for: PNG, JPG and
/// PDF, plus LOG because a diagnostics file is the second thing every bug report needs.
/// </para>
/// <para>
/// Both limits are enforced again by <see cref="SupportAttachmentStore"/> while the bytes stream in.
/// A client-declared length is a claim, not a measurement, and the browser file picker's own
/// <c>accept</c> filter is a convenience the user can defeat with a drag or a paste.
/// </para>
/// </remarks>
public static class SupportAttachmentPolicy
{
    /// <summary>Largest attachment accepted, in bytes (10 MB, as shown on the screen).</summary>
    public const long MaxFileSizeBytes = 10L * 1024 * 1024;

    /// <summary>Most attachments carried on a single issue or comment.</summary>
    public const int MaxAttachmentCount = 5;

    /// <summary>Longest file name kept on disk, before the extension is re-appended.</summary>
    public const int MaxFileNameLength = 120;

    /// <summary>Name used when a supplied file name sanitises away to nothing.</summary>
    public const string FallbackFileName = "attachment";

    /// <summary>The file-picker accept filter matching <see cref="AllowedExtensions"/>.</summary>
    public const string AcceptTypes = ".png,.jpg,.jpeg,.pdf,.log";

    /// <summary>Resource key for the human-readable summary of the limits, shown beside the drop zone.</summary>
    /// <remarks>
    /// REQ-UI-055 / BRD-91. This was the literal <c>"PNG, JPG, PDF, LOG · 10 MB each"</c>, and it was
    /// passed as an ARGUMENT into two already-localized sentences on the Support screen — so a Hindi
    /// install rendered a Devanagari hint with an English tail glued onto it, which is worse than an
    /// English hint because it reads as a rendering fault. The extension tokens inside the value stay
    /// in Latin script in every language: they are file-type names, not words.
    /// </remarks>
    public const string LimitsSummaryKey = "SupportAttachmentLimitsSummary";

    /// <summary>The extensions an attachment may carry.</summary>
    public static IReadOnlySet<string> AllowedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".pdf", ".log"
        };

    /// <summary>
    /// Builds the user-facing rejection message for an attachment, or null when it is acceptable.
    /// </summary>
    /// <param name="fileName">The file name as offered by the picker, drop or paste.</param>
    /// <param name="sizeBytes">The file size in bytes.</param>
    /// <param name="localize">Resolves the resource key of whichever rule the file breaks.</param>
    /// <returns>The rejection reason, or null when the file may be attached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="localize"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// REQ-UI-055 / BRD-91. Three of the four refusals NAME the file and one of them names two
    /// sizes, so this takes a <see cref="LocalizeText"/> rather than returning a bare key: a key on
    /// its own cannot carry an argument, and the screen that toasts this message has no way to know
    /// which arguments a given refusal wanted. There is deliberately no parameterless overload.
    /// </remarks>
    public static string? GetRejectionReason(string? fileName, long sizeBytes, LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return localize("SupportAttachmentRejectedNoName");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            return localize("SupportAttachmentRejectedType", fileName, localize(LimitsSummaryKey));
        }

        if (sizeBytes <= 0)
        {
            return localize("SupportAttachmentRejectedEmpty", fileName);
        }

        return sizeBytes > MaxFileSizeBytes
            ? localize("SupportAttachmentRejectedTooLarge", fileName, FormatSize(sizeBytes), FormatSize(MaxFileSizeBytes))
            : null;
    }

    /// <summary>
    /// Reduces an offered file name to one that is safe to use as a leaf file name.
    /// </summary>
    /// <param name="fileName">The offered file name, possibly containing path separators.</param>
    /// <returns>A single path segment with no separators and no invalid characters.</returns>
    /// <remarks>
    /// Path separators are stripped for BOTH conventions, not just the host's. On macOS a backslash
    /// is an ordinary file-name character, so <c>Path.GetFileName</c> leaves <c>..\..\evil</c> intact
    /// — harmless there, but the same staged name later read on Windows would escape the directory.
    /// A name that reduces to nothing, to <c>.</c> or to <c>..</c> becomes
    /// <see cref="FallbackFileName"/> rather than being trusted.
    /// </remarks>
    public static string SafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FallbackFileName;
        }

        var lastSeparator = fileName.LastIndexOfAny(['/', '\\']);
        var leaf = lastSeparator >= 0 ? fileName[(lastSeparator + 1)..] : fileName;

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(leaf
            .Select(character => invalid.Contains(character) || character is '/' or '\\' ? '-' : character)
            .ToArray())
            .Trim();

        if (cleaned.Length == 0 || cleaned == "." || cleaned == "..")
        {
            return FallbackFileName;
        }

        var extension = Path.GetExtension(cleaned);
        var stem = Path.GetFileNameWithoutExtension(cleaned);
        if (stem.Length == 0)
        {
            stem = FallbackFileName;
        }

        if (stem.Length > MaxFileNameLength)
        {
            stem = stem[..MaxFileNameLength];
        }

        return stem + extension;
    }

    /// <summary>Formats a byte count the way the attachment chips show it.</summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>A short human-readable size such as <c>248 KB</c>.</returns>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.#} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
