namespace TechieRag.Models;

/// <summary>
/// Answer mode for a workspace's RAG operations.
/// </summary>
public enum WorkspaceChatMode
{
    /// <summary>Conversational mode: the LLM may combine context with general knowledge.</summary>
    Chat,
    /// <summary>Query mode: answers must come only from the retrieved workspace context.</summary>
    Query
}

/// <summary>
/// A workspace (collection) that isolates a set of documents together with its own
/// retrieval and generation settings.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets one TechieRag instance serve multiple isolated knowledge bases,
/// each with a per-workspace system prompt, LLM model override, similarity threshold, topK,
/// rerank toggle, and chat-vs-query answer mode. Stored in the TrWorkspace table.</para>
/// <para><b>Code Flow:</b> Managed by WorkspaceManager via IWorkspaceStore. Null settings
/// fall back to the library-wide configuration.</para>
/// </remarks>
public class Workspace
{
    /// <summary>Gets or sets the unique workspace identifier.</summary>
    public string WorkspaceId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Gets or sets the workspace display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the per-workspace system prompt override, or null to use the global prompt.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Gets or sets the per-workspace LLM model override, or null to use the configured model.</summary>
    public string? LlmModel { get; set; }

    /// <summary>Gets or sets the minimum similarity score a chunk must reach to be used as context, or null for no threshold.</summary>
    public float? SimilarityThreshold { get; set; }

    /// <summary>Gets or sets the per-workspace topK override, or null to use the caller's default.</summary>
    public int? TopK { get; set; }

    /// <summary>Gets or sets whether the rerank stage is applied for this workspace's retrieval.</summary>
    /// <remarks>
    /// <para><b>Authoritative:</b> This flag decides reranking for every workspace-scoped
    /// retrieval — it overrides the library-wide <c>TechieRagConfig.Rerank.Enabled</c> setting in
    /// both directions. True forces reranking on even when the global flag is off; false forces it
    /// off even when the global flag is on.</para>
    /// <para><b>Requires a reranker:</b> Setting this to true has no effect unless an
    /// <c>IReranker</c> is configured (TechieRagBuilder.WithReranker or the <c>TechieRag:Rerank</c>
    /// configuration section); the library logs a warning and returns vector-similarity order.</para>
    /// <para><b>History:</b> Before REQ-RAG-047 (TR-RAG-005) this property round-tripped through
    /// the store but nothing read it, so reranking was global configuration only.</para>
    /// </remarks>
    public bool RerankEnabled { get; set; }

    /// <summary>Gets or sets the answer mode (chat vs context-only query).</summary>
    public WorkspaceChatMode ChatMode { get; set; } = WorkspaceChatMode.Chat;

    /// <summary>Gets or sets when the workspace was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets when the workspace was last updated (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
