namespace TechieRag.Connectors;

/// <summary>
/// An item's contents, fetched and reduced to text (REQ-RAG-032 / BRD-113).
/// </summary>
/// <param name="Item">The item this text came from, carried through so a citation can name it.</param>
/// <param name="Text">Readable text. Never null; may be empty, which callers treat as "nothing to ingest".</param>
public sealed record ConnectorDocument(ConnectorItem Item, string Text);
