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
}
