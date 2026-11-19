using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Builds the matches report and the count sentence <c>AskWithEvidenceAsync</c> hands the model
/// (REQ-RAG-172, MyDiary TR-RAG-001). Model-facing text is invariant English (project rule, REQ-RAG-050).
/// </summary>
internal static class EvidencePrompt
{
    /// <summary>How many document names the count sentence lists.</summary>
    private const int NamesListed = 50;

    /// <summary>Groups matching results by document into a report, the best document first.</summary>
    /// <param name="query">The query.</param>
    /// <param name="minScore">The score a chunk needed.</param>
    /// <param name="matches">The matching results.</param>
    /// <param name="complete">Whether every match was read.</param>
    /// <returns>The report.</returns>
    public static MatchReport Report(string query, float minScore, IEnumerable<SearchResult> matches, bool complete) => new()
    {
        Query = query,
        MinScore = minScore,
        IsComplete = complete,
        Documents = matches
            .GroupBy(result => result.Chunk.DocumentId, StringComparer.Ordinal)
            .Select(group =>
            {
                var chunks = group.OrderByDescending(result => result.Score).ToList();
                return new MatchedDocument
                {
                    DocumentId = group.Key,
                    DocumentName = chunks
                        .Select(result => result.Chunk.Metadata.TryGetValue("DocumentName", out var name) ? VectorStores.MetadataMatch.TextOf(name) : null)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? group.Key,
                    BestScore = chunks[0].Score,
                    Chunks = chunks
                };
            })
            .OrderByDescending(document => document.BestScore)
            .ThenBy(document => document.DocumentId, StringComparer.Ordinal)
            .ToList()
    };

    /// <summary>Adds the library's count and the matching names to a system prompt.</summary>
    /// <param name="basePrompt">The system prompt in use.</param>
    /// <param name="report">The matches.</param>
    /// <returns>The system prompt the model gets.</returns>
    public static string Build(string basePrompt, MatchReport report)
    {
        var count = report.DocumentCount;
        var names = string.Join("; ", report.Documents.Take(NamesListed).Select(document => document.DocumentName));
        var more = count > NamesListed ? $"; and {count - NamesListed} more" : string.Empty;
        var bound = report.IsComplete ? "exactly" : "at least";
        return $"{basePrompt}\n\nThe library searched every entry and found {bound} {count} matching "
            + $"{(count == 1 ? "entry" : "entries")}: {names}{more}. If the user asks how many, the answer is {count}: "
            + $"begin your reply with the number {count}, then say briefly which entries they are. Do not count the context "
            + "yourself. Answer only from the matching entries.";
    }
}
