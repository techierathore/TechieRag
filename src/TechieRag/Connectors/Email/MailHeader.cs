namespace TechieRag.Connectors.Email;

/// <summary>
/// What a mailbox says about a message without sending its body (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// Enough to filter on, and no more. Deciding whether a message is wanted must not require
/// downloading it: a mailbox is the one source where the items nobody asked for outnumber the ones
/// they did by two orders of magnitude, and where downloading them anyway is a privacy problem as
/// well as a performance one.
/// </remarks>
/// <param name="Folder">The folder the message lives in.</param>
/// <param name="Uid">The message's identifier within that folder. Stable and never reused while <paramref name="UidValidity"/> holds.</param>
/// <param name="UidValidity">The folder's UID generation. When a server changes this, every UID in the folder means something new.</param>
/// <param name="Subject">The decoded subject line, or empty.</param>
/// <param name="From">The decoded From header, or empty.</param>
/// <param name="To">The decoded To header, or empty.</param>
/// <param name="Date">When the message was sent or received, when the server reports it.</param>
/// <param name="SizeBytes">The message's size including attachments, when the server reports it.</param>
/// <param name="MessageId">The RFC 5322 Message-ID, when present. The only identifier stable across folders and accounts.</param>
public sealed record MailHeader(
    string Folder,
    string Uid,
    string UidValidity,
    string Subject,
    string From,
    string To,
    DateTimeOffset? Date = null,
    long? SizeBytes = null,
    string? MessageId = null);
