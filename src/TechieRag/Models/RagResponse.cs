namespace TechieRag.Models;

/// <summary>Response from an auto-RAG operation (search + generate).</summary>
public class RagResponse
{
    /// <summary>Gets or sets the generated answer text.</summary>
    public required string Answer { get; set; }

    /// <summary>Gets or sets the search results used to generate the answer.</summary>
    public required IReadOnlyList<SearchResult> Sources { get; set; }

    /// <summary>Gets or sets the token usage for the LLM operation.</summary>
    public required TokenUsage Usage { get; set; }

    /// <summary>Gets or sets the original query.</summary>
    public required string Query { get; set; }

    /// <summary>Gets or sets the model name that generated the answer.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the library's own count of matching documents and their sources, set by
    /// <c>AskWithEvidenceAsync</c> and null otherwise (REQ-RAG-172, MyDiary TR-RAG-001).
    /// </summary>
    /// <remarks>Show <see cref="MatchReport.DocumentCount"/> as the count and <see cref="MatchReport.Documents"/> as the
    /// sources: the library counted them, the model did not.</remarks>
    public MatchReport? Matches { get; set; }

    /// <summary>
    /// Gets or sets whether the answer was withheld because nothing matched (REQ-RAG-172). When true, no model was
    /// called, <see cref="Answer"/> is empty and <see cref="Sources"/> is empty; the host says "no matching entries"
    /// in its own words and language.
    /// </summary>
    public bool Abstained { get; set; }
}
