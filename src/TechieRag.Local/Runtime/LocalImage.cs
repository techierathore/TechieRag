namespace TechieRag.Local.Runtime;

/// <summary>
/// One picture of a chat turn as the runtime receives it: its bytes and media type, in the order the
/// prompt's image markers name them (REQ-RAG-147 / BRD-205).
/// </summary>
/// <param name="Bytes">The encoded picture (PNG, JPEG and so on), never fetched from a network.</param>
/// <param name="MediaType">The media type the app gave, for example <c>image/png</c>.</param>
internal sealed record LocalImage(byte[] Bytes, string MediaType);
