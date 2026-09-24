namespace TechieRag.Agentic;

/// <summary>One search the model made through <c>search_knowledge_base</c> (REQ-RAG-015 / BRD-83).</summary>
/// <param name="Query">The model's query.</param>
/// <param name="TopK">Passages asked for, after clamping.</param>
/// <param name="DocumentId">The document the search was restricted to, or null.</param>
/// <param name="Status"><c>strong</c>, <c>weak</c>, <c>none</c> or <c>limit_reached</c>.</param>
/// <param name="BestScore">The best score, or null when nothing was returned.</param>
/// <param name="Refs">Citation refs of the returned passages, in order.</param>
/// <param name="Duration">How long the search took.</param>
public sealed record RetrievalTrace(
    string Query,
    int TopK,
    string? DocumentId,
    string Status,
    float? BestScore,
    IReadOnlyList<string> Refs,
    TimeSpan Duration);
