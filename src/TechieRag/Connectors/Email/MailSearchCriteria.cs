namespace TechieRag.Connectors.Email;

/// <summary>
/// Which messages a folder should return (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>These are pushed to the server, not applied afterwards.</b> IMAP can evaluate every one
/// of these itself, and letting it do so is the difference between transferring the headers of a
/// decade of mail and transferring the headers of a week of it. A transport that cannot express a
/// criterion must apply it locally rather than ignore it — a filter that silently does nothing on
/// one transport is worse than one that is slow.</para>
/// <para><b>Scope filters are a privacy control.</b> BRD-135 treats them as acceptance criteria
/// rather than convenience: everything ingested becomes answerable to anyone who can query that
/// workspace, so "which messages" is the whole safety question for this connector.</para>
/// </remarks>
/// <param name="SinceUtc">Only messages on or after this date. Day granularity — IMAP has no finer.</param>
/// <param name="SenderContains">Only messages whose From header contains this text, matched case-insensitively.</param>
/// <param name="SubjectContains">Only messages whose Subject contains this text, matched case-insensitively.</param>
public sealed record MailSearchCriteria(
    DateTimeOffset? SinceUtc = null,
    string? SenderContains = null,
    string? SubjectContains = null);
