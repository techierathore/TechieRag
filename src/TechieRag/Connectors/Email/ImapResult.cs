namespace TechieRag.Connectors.Email;

/// <summary>The response to one tagged IMAP command.</summary>
/// <param name="IsOk">True when the tagged completion was OK.</param>
/// <param name="Completion">The tagged completion line.</param>
/// <param name="Lines">The untagged lines that preceded it.</param>
internal sealed record ImapResult(bool IsOk, string Completion, IReadOnlyList<ImapLine> Lines);
