namespace TechieRag.Connectors.Email;

/// <summary>
/// One page of matching message headers (REQ-RAG-049 / BRD-135).
/// </summary>
/// <param name="Headers">Headers in the page, oldest first.</param>
/// <param name="HasMore">True when the folder holds further matches past this page.</param>
public sealed record MailSearchPage(IReadOnlyList<MailHeader> Headers, bool HasMore);
