namespace TechieRag.Connectors.Email;

/// <summary>
/// The stable codes on <see cref="MailParseNote.Code"/> (REQ-RAG-119 / BRD-180).
/// </summary>
/// <remarks>A host switches on these. The values never change once shipped; a new case gets a new code.</remarks>
public static class MailParseCodes
{
    /// <summary>
    /// The part was nested deeper than <see cref="MimeParser.MaxNestingDepth"/>, so neither it nor anything
    /// inside it was decoded.
    /// </summary>
    public const string NestingTooDeep = "MailParseNestingTooDeep";

    /// <summary>
    /// The message already yielded <see cref="MimeParser.MaxAttachments"/> attachments, so this one was
    /// not decoded.
    /// </summary>
    public const string AttachmentLimitReached = "MailParseAttachmentLimitReached";
}
