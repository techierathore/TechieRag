namespace TechieRag.Connectors.Email;

/// <summary>
/// One piece of a message the parser did not decode, and why (REQ-RAG-119 / BRD-180, Sevak feedback TR-RAG-035).
/// </summary>
/// <param name="Code">Why it was left out, from <see cref="MailParseCodes"/>. A host switches on this, never on prose.</param>
/// <param name="PartName">The part that was left out: its file name when it had one, otherwise its media type
/// (<c>multipart/mixed</c>, <c>text/plain</c>), or <c>(unlabelled)</c> when it declared neither.</param>
/// <param name="Depth">How deeply the part was nested; the top-level message is depth 0.</param>
public sealed record MailParseNote(string Code, string PartName, int Depth);
