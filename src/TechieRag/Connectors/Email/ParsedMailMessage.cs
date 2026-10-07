namespace TechieRag.Connectors.Email;

/// <summary>
/// A message decoded down to what ingestion needs (REQ-RAG-049 / BRD-135).
/// </summary>
/// <param name="Headers">All headers, unfolded, keyed case-insensitively. Values are raw; the named properties below are decoded.</param>
/// <param name="Subject">The decoded subject, or empty.</param>
/// <param name="From">The decoded From header, or empty.</param>
/// <param name="To">The decoded To header, or empty.</param>
/// <param name="Date">The parsed Date header, when it was present and valid.</param>
/// <param name="MessageId">The Message-ID, when present.</param>
/// <param name="Body">The message's readable text, converted from HTML when there was no plain-text alternative.</param>
/// <param name="Attachments">Files carried by the message, decoded.</param>
public sealed record ParsedMailMessage(
    IReadOnlyDictionary<string, string> Headers,
    string Subject,
    string From,
    string To,
    DateTimeOffset? Date,
    string? MessageId,
    string Body,
    IReadOnlyList<MailAttachment> Attachments)
{
    /// <summary>
    /// Gets the parts the parser did not decode, each with a code and the part's name
    /// (REQ-RAG-119 / BRD-180). Empty when the whole message was read.
    /// </summary>
    /// <remarks>
    /// An addition, not a change: existing callers that never read it are unaffected. Content nested
    /// past <see cref="MimeParser.MaxNestingDepth"/> or beyond <see cref="MimeParser.MaxAttachments"/>
    /// used to vanish without notice; it is now listed here.
    /// </remarks>
    public IReadOnlyList<MailParseNote> Notes { get; init; } = [];
}
