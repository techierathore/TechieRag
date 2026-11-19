namespace TechieRag.Models;

/// <summary>One document with at least one matching chunk (REQ-RAG-172, MyDiary TR-RAG-001).</summary>
public sealed class MatchedDocument
{
    /// <summary>Gets the document's id.</summary>
    public required string DocumentId { get; init; }

    /// <summary>Gets the document's name, from its chunks' <c>DocumentName</c> metadata, or its id when none is stored.</summary>
    public required string DocumentName { get; init; }

    /// <summary>Gets the highest score among its matching chunks.</summary>
    public required float BestScore { get; init; }

    /// <summary>Gets its matching chunks, highest score first.</summary>
    public required IReadOnlyList<SearchResult> Chunks { get; init; }
}
