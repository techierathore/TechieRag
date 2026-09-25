namespace TechieRag.Agentic;

/// <summary>Configuration for the knowledge-base tools (REQ-RAG-015 / BRD-83).</summary>
public sealed class RetrievalToolOptions
{
    /// <summary>Gets or sets the passages returned when the model gives no <c>top_k</c>. Default 5.</summary>
    public int TopK { get; set; } = 5;

    /// <summary>Gets or sets the most passages one search may return. Default 20.</summary>
    public int MaxTopK { get; set; } = 20;

    /// <summary>Gets or sets the searches allowed per turn before <c>limit_reached</c>. Default 4.</summary>
    public int MaxSearchesPerTurn { get; set; } = 4;

    /// <summary>Gets or sets the best score at or above which a search is <c>strong</c>. Default 0.55 (bge-m3 cosine).</summary>
    public float WeakScoreThreshold { get; set; } = 0.55f;

    /// <summary>Gets or sets the best score below which a search is <c>none</c>. Default 0.35.</summary>
    public float NoneScoreThreshold { get; set; } = 0.35f;

    /// <summary>Gets or sets whether searches are reranked. Reranking replaces the cosine scale, so the
    /// thresholds above no longer apply; see <see cref="RerankWeakThreshold"/>.</summary>
    public bool Rerank { get; set; }

    /// <summary>Gets or sets the reranker score at or above which a reranked search is <c>strong</c>;
    /// null classifies any reranked hit as <c>strong</c>.</summary>
    public float? RerankWeakThreshold { get; set; }

    /// <summary>Gets or sets the longest passage text returned to the model, in characters. Default 1500.</summary>
    public int MaxChunkChars { get; set; } = 1500;

    /// <summary>Gets or sets a document every search is restricted to, overriding the model's <c>document_id</c>.</summary>
    public string? DocumentFilter { get; set; }

    /// <summary>Gets or sets whether scores are shown to the model. Default true.</summary>
    public bool IncludeScores { get; set; } = true;

    /// <summary>Gets or sets the search tool's name. Default <c>search_knowledge_base</c>.</summary>
    public string ToolName { get; set; } = KnowledgeBaseTools.SearchToolName;

    /// <summary>Gets or sets the list tool's name. Default <c>list_documents</c>.</summary>
    public string ListToolName { get; set; } = KnowledgeBaseTools.ListToolName;
}
