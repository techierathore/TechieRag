namespace TechieRag.Models;

/// <summary>
/// What counts as a match for <see cref="ITechieRag.FindMatchesAsync"/> and <see cref="ITechieRag.AskWithEvidenceAsync"/>:
/// a relevance score at or above <see cref="MinScore"/>, within an optional scope (REQ-RAG-172, MyDiary TR-RAG-001).
/// </summary>
public sealed class MatchOptions
{
    /// <summary>
    /// Gets the lowest relevance score (cosine similarity, 0 to 1) a chunk needs to count as a match. Required: the
    /// right value depends on the embedding model, so the library sets none. For bge-m3, 0.5 to 0.6 keeps clear
    /// matches; check it against your own data.
    /// </summary>
    public required float MinScore { get; init; }

    /// <summary>Gets the documents to look in, or null for all.</summary>
    public IReadOnlyCollection<string>? DocumentFilters { get; init; }

    /// <summary>Gets metadata every match must carry, or null for none; the same rule as <see cref="SearchOptions.MetadataFilter"/>.</summary>
    public IReadOnlyDictionary<string, string>? MetadataFilter { get; init; }

    /// <summary>
    /// Gets the most matching chunks read before the search stops; the report then says
    /// <see cref="MatchReport.IsComplete"/> false. Defaults to 10,000.
    /// </summary>
    public int MaxMatches { get; init; } = 10_000;
}
