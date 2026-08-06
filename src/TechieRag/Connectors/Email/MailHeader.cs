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
/// <param name="StableId">
/// An identity the TRANSPORT supplies when <paramref name="Folder"/> and <paramref name="UidValidity"/>
/// are not a safe basis for one. Null for IMAP, where they are.
/// </param>
/// <remarks>
/// <para><b>Why <paramref name="StableId"/> exists (TR-RAG-037 / REQ-RAG-049).</b> A connector item's
/// identity is what decides "have I ingested this before", so anything mutable inside it re-ingests
/// the world when it changes. For IMAP, <c>folder/uidvalidity/uid</c> is exactly right — the server
/// owns all three and tells you when they stop meaning what they meant. For a FILE-BACKED mailbox it
/// is not: <c>MboxMailTransport</c> reports its folder as the archive's file name, so renaming
/// <c>inbox.mbox</c> re-ingested every message in it as new. The transport is the only thing that
/// knows whether its own coordinates are stable, so the transport supplies the identity rather than
/// the connector guessing from a shape it cannot see.</para>
/// </remarks>
public sealed record MailHeader(
    string Folder,
    string Uid,
    string UidValidity,
    string Subject,
    string From,
    string To,
    DateTimeOffset? Date = null,
    long? SizeBytes = null,
    string? MessageId = null,
    string? StableId = null);
