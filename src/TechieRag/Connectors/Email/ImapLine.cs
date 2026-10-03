namespace TechieRag.Connectors.Email;

/// <summary>One untagged IMAP response line and the literals it carried.</summary>
/// <param name="Text">The line text, literal markers included.</param>
/// <param name="Literals">The literal payloads, in order.</param>
internal sealed record ImapLine(string Text, IReadOnlyList<byte[]> Literals);
