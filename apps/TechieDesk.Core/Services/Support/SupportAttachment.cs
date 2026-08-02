namespace TechieDesk.Services.Support;

/// <summary>
/// One file staged for a support issue or comment (REQ-UI-047).
/// </summary>
/// <param name="FileName">The sanitised leaf file name as written to disk.</param>
/// <param name="SizeBytes">The measured size on disk, not the size the client claimed.</param>
/// <param name="ContentType">The MIME type, inferred from the extension when the client sent none.</param>
/// <param name="FullPath">Absolute path under the data directory's support-attachments folder.</param>
public sealed record SupportAttachment(
    string FileName,
    long SizeBytes,
    string ContentType,
    string FullPath)
{
    /// <summary>Gets the size formatted the way the attachment chip shows it.</summary>
    public string FormattedSize => SupportAttachmentPolicy.FormatSize(SizeBytes);

    /// <summary>Gets the upper-cased extension without its dot, for the chip's type square.</summary>
    public string TypeLabel
    {
        get
        {
            var extension = Path.GetExtension(FileName);
            return extension.Length > 1 ? extension[1..].ToUpperInvariant() : "FILE";
        }
    }
}
