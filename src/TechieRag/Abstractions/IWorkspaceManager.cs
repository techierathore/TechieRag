using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Describes the workspace manager: isolated document sets with per-workspace retrieval and
/// generation settings (REQ-RAG-116 / BRD-176, Sevak feedback TR-RAG-013).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A host service built on workspaces depends on this interface rather than on
/// the concrete <see cref="Services.WorkspaceManager"/>, so a unit test can hand it a test double and
/// never build a library client, a vector store or an embedding provider.</para>
/// <para><b>Code Flow:</b> <see cref="Services.WorkspaceManager"/> implements it.
/// <c>ITechieRag.GetWorkspaceManager()</c> keeps returning the concrete type (owner decision
/// 2026-10-06), which converts to this interface implicitly. <c>AddTechieRag</c> registers it in the
/// service collection with <c>TryAdd</c>, so a host that registered its own stand-in first keeps it.</para>
/// <para><b>Contract:</b> Every member mirrors the member of the same name on
/// <see cref="Services.WorkspaceManager"/>; the remarks there are the authoritative behaviour.</para>
/// </remarks>
public interface IWorkspaceManager
{
    /// <summary>
    /// Raised when a composed workspace context did not fit <c>PromptConfig.MaxContextChunks</c>
    /// and one or more chunks were evicted.
    /// </summary>
    event EventHandler<ContextTruncatedEventArgs>? ContextTruncated;

    /// <summary>Gets the underlying workspace store for direct access.</summary>
    /// <returns>The configured workspace store.</returns>
    IWorkspaceStore GetStore();

    /// <summary>Creates a new workspace.</summary>
    /// <param name="name">The workspace display name.</param>
    /// <param name="configure">Optional action to set workspace settings before saving.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The created workspace.</returns>
    Task<Workspace> CreateWorkspaceAsync(string name, Action<Workspace>? configure = null, CancellationToken cancellationToken = default);

    /// <summary>Lists all workspaces, most recently updated first.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>All workspaces.</returns>
    Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieves a workspace by identifier.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The workspace, or null when not found.</returns>
    Task<Workspace?> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Updates a workspace's settings.</summary>
    /// <param name="workspace">The workspace with updated values.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous update.</returns>
    Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default);

    /// <summary>Deletes a workspace and its memberships.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete.</returns>
    Task DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Ingests text into a workspace with content-hash deduplication.</summary>
    /// <param name="workspaceId">The target workspace identifier.</param>
    /// <param name="text">The text content to ingest.</param>
    /// <param name="documentName">A friendly name for the document.</param>
    /// <param name="metadata">Optional metadata for the document chunks.</param>
    /// <param name="pinned">Whether the document should be pinned (always in context).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The document identifier.</returns>
    Task<string> IngestTextAsync(string workspaceId, string text, string documentName, Dictionary<string, object>? metadata = null, bool pinned = false, CancellationToken cancellationToken = default);

    /// <summary>Ingests a file into a workspace with content-hash deduplication.</summary>
    /// <param name="workspaceId">The target workspace identifier.</param>
    /// <param name="filePath">Absolute path to the file to ingest.</param>
    /// <param name="pinned">Whether the document should be pinned (always in context).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The document identifier.</returns>
    Task<string> IngestFileAsync(string workspaceId, string filePath, bool pinned = false, CancellationToken cancellationToken = default);

    /// <summary>References an already-ingested document from a workspace without re-embedding it.</summary>
    /// <param name="workspaceId">The target workspace identifier.</param>
    /// <param name="documentId">The existing document identifier.</param>
    /// <param name="contentHash">Optional content hash to record for deduplication.</param>
    /// <param name="pinned">Whether the document should be pinned.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous add.</returns>
    Task AddExistingDocumentAsync(string workspaceId, string documentId, string? contentHash = null, bool pinned = false, CancellationToken cancellationToken = default);

    /// <summary>Pins or unpins a workspace document.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="pinned">True to pin, false to unpin.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous update.</returns>
    Task SetPinnedAsync(string workspaceId, string documentId, bool pinned, CancellationToken cancellationToken = default);

    /// <summary>Performs semantic search scoped to a workspace's documents.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="query">The natural language query.</param>
    /// <param name="topK">Optional topK override.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked results restricted to the workspace's documents.</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string workspaceId, string query, int? topK = null, CancellationToken cancellationToken = default);

    /// <summary>Performs semantic search scoped to a workspace and narrowed by a per-turn retrieval scope.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="query">The natural language query.</param>
    /// <param name="overrides">Per-turn overrides supplying the retrieval scope; null means the whole workspace.</param>
    /// <param name="topK">Optional topK override.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked results restricted to the in-scope documents.</returns>
    Task<IReadOnlyList<SearchResult>> SearchScopedAsync(string workspaceId, string query, WorkspaceTurnOverrides? overrides, int? topK = null, CancellationToken cancellationToken = default);

    /// <summary>Performs a complete workspace-scoped RAG operation.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="question">The user's question.</param>
    /// <param name="options">Optional LLM completion parameters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The answer with its workspace-scoped sources and token usage.</returns>
    Task<RagResponse> AskAsync(string workspaceId, string question, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Streams a workspace-scoped RAG answer, sources first.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="question">The user's question.</param>
    /// <param name="conversationHistory">Optional prior turns.</param>
    /// <param name="options">Optional LLM completion parameters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Sources, then tokens, then Completed.</returns>
    IAsyncEnumerable<RagStreamEvent> AskStreamWithSourcesAsync(string workspaceId, string question, IReadOnlyList<ChatMessage>? conversationHistory = null, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Streams a workspace-scoped RAG answer for one turn with per-turn overrides.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="question">The user's question.</param>
    /// <param name="overrides">Per-turn overrides, or null.</param>
    /// <param name="conversationHistory">Optional prior turns.</param>
    /// <param name="options">Optional LLM completion parameters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Sources, then tokens, then Completed.</returns>
    IAsyncEnumerable<RagStreamEvent> AskTurnStreamAsync(string workspaceId, string question, WorkspaceTurnOverrides? overrides, IReadOnlyList<ChatMessage>? conversationHistory = null, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Composes the workspace context for a question, pinned documents first.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="question">The question to retrieve context for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The composed, budgeted context.</returns>
    Task<IReadOnlyList<SearchResult>> BuildContextAsync(string workspaceId, string question, CancellationToken cancellationToken = default);

    /// <summary>Composes the workspace context and reports any eviction the context budget forced.</summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="question">The question to retrieve context for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The composed context with its inclusion and eviction counts.</returns>
    Task<WorkspaceContext> BuildContextWithDiagnosticsAsync(string workspaceId, string question, CancellationToken cancellationToken = default);
}
