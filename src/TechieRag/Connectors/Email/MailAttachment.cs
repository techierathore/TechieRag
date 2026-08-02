namespace TechieRag.Connectors.Email;

/// <summary>
/// A file carried by a message (REQ-RAG-049 / BRD-135).
/// </summary>
/// <param name="FileName">The attachment's file name, decoded. Never a path — path separators are stripped by the parser.</param>
/// <param name="MediaType">The declared media type, lowercased, e.g. <c>application/pdf</c>.</param>
/// <param name="Content">The decoded bytes, ready to hand to a document processor without touching disk.</param>
public sealed record MailAttachment(string FileName, string MediaType, byte[] Content);
