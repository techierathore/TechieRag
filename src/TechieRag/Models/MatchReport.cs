namespace TechieRag.Models;

/// <summary>
/// Every document with a chunk at or above a score, counted by the library rather than by a model
/// (REQ-RAG-172, MyDiary TR-RAG-001).
/// </summary>
/// <remarks>
/// <para><b>Exact.</b> <see cref="DocumentCount"/> is the number of documents whose best chunk scores at least
/// <see cref="MinScore"/>: the search reads matches best-first until a chunk scores below it, so nothing at or
/// above the score is left out (unless <see cref="IsComplete"/> is false). It never depends on a language model,
/// so it is the same with a hosted model, the built-in model, or none.</para>
/// <para><b>Empty means none.</b> <see cref="IsEmpty"/> is the abstention signal: no document matched, so there is
/// nothing to answer from.</para>
/// </remarks>
public sealed class MatchReport
{
    /// <summary>Gets the query.</summary>
    public required string Query { get; init; }

    /// <summary>Gets the score a chunk needed to match.</summary>
    public required float MinScore { get; init; }

    /// <summary>Gets the matching documents, the one with the highest best score first.</summary>
    public required IReadOnlyList<MatchedDocument> Documents { get; init; }

    /// <summary>Gets whether every match was read; false when <see cref="MatchOptions.MaxMatches"/> stopped the search.</summary>
    public bool IsComplete { get; init; } = true;

    /// <summary>Gets the number of matching documents.</summary>
    public int DocumentCount => Documents.Count;

    /// <summary>Gets the number of matching chunks across all documents.</summary>
    public int ChunkCount => Documents.Sum(document => document.Chunks.Count);

    /// <summary>Gets whether nothing matched.</summary>
    public bool IsEmpty => Documents.Count == 0;
}
