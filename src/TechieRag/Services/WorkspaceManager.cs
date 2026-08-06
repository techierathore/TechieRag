using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// High-level service for workspace (collection) operations: isolated document sets with
/// per-workspace retrieval and generation settings.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements the workspace primitives on top of ITechieRag and
/// IWorkspaceStore: document membership with content-hash deduplication (embed once,
/// reference from many workspaces), document pinning (always-in-context), workspace-scoped
/// retrieval, per-workspace settings, and context-only query mode.</para>
/// <para><b>Code Flow:</b> Created by TechieRagClient when a workspace store is configured
/// via TechieRagBuilder.WithPersistence; retrieved via ITechieRag.GetWorkspaceManager().</para>
/// <para><b>Scoping:</b> Workspace-scoped search oversamples the global vector search and
/// filters the results to the workspace's document set, since the vector stores filter by a
/// single document ID only.</para>
/// </remarks>
public class WorkspaceManager
{
    private const string QueryModeInstruction =
        "Answer ONLY using the provided context. Do not use outside knowledge. " +
        "If the context does not contain the answer, reply that the workspace documents do not cover this question.";

    private const string QueryModeNoContextAnswer =
        "The workspace documents do not contain information relevant to this question.";

    private const string NoLlmProviderMessage =
        "No LLM provider configured. Configure an LLM provider to use workspace ask operations.";

    private const int OversampleFactor = 5;
    private const int PinnedChunksPerDocument = 3;

    private readonly ITechieRag rag;
    private readonly IWorkspaceStore store;
    private readonly IPromptTemplate promptTemplate;
    private readonly PromptConfig promptConfig;
    private readonly ILogger logger;

    /// <summary>
    /// Creates a new workspace manager.
    /// </summary>
    /// <param name="rag">The TechieRag client used for ingestion, retrieval, and generation.</param>
    /// <param name="store">The persistent workspace store.</param>
    /// <param name="promptTemplate">The prompt template used to build workspace RAG prompts.</param>
    /// <param name="promptConfig">Prompt configuration supplying the context budget
    /// (<c>MaxContextChunks</c>). When null, library defaults are used.</param>
    /// <param name="logger">Optional logger used to report context truncation. When null,
    /// truncation is still observable via <see cref="ContextTruncated"/> and
    /// <see cref="WorkspaceContext.WasTruncated"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rag"/>,
    /// <paramref name="store"/>, or <paramref name="promptTemplate"/> is null.</exception>
    /// <remarks>
    /// <para><b>Back-compat:</b> <paramref name="promptConfig"/> and <paramref name="logger"/> were
    /// added for REQ-RAG-048 and are optional, so the original three-argument construction still
    /// compiles and behaves identically.</para>
    /// </remarks>
    public WorkspaceManager(
        ITechieRag rag,
        IWorkspaceStore store,
        IPromptTemplate promptTemplate,
        PromptConfig? promptConfig = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(rag);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(promptTemplate);

        this.rag = rag;
        this.store = store;
        this.promptTemplate = promptTemplate;
        this.promptConfig = promptConfig ?? new PromptConfig();
        this.logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Raised when a composed workspace context did not fit <c>PromptConfig.MaxContextChunks</c>
    /// and one or more chunks were evicted before the prompt was built.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> REQ-RAG-048 (TR-RAG-006) — the push-based truncation signal. Every
    /// workspace retrieval path raises it: <see cref="AskAsync"/>,
    /// <see cref="AskStreamWithSourcesAsync(string, string, IReadOnlyList{ChatMessage}, LlmCompletionOptions, CancellationToken)"/>,
    /// <see cref="BuildContextAsync"/>, and <see cref="BuildContextWithDiagnosticsAsync"/>.</para>
    /// <para><b>Threading:</b> Raised synchronously on the retrieving task before generation
    /// starts; handlers should be fast and must not throw.</para>
    /// </remarks>
    public event EventHandler<ContextTruncatedEventArgs>? ContextTruncated;

    /// <summary>
    /// Gets the underlying workspace store for direct access.
    /// </summary>
    /// <returns>The configured IWorkspaceStore.</returns>
    public IWorkspaceStore GetStore() => store;

    /// <summary>
    /// Creates a new workspace.
    /// </summary>
    /// <param name="name">The workspace display name.</param>
    /// <param name="configure">Optional action to set workspace settings before saving.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The created workspace.</returns>
    public async Task<Workspace> CreateWorkspaceAsync(
        string name,
        Action<Workspace>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var workspace = new Workspace { Name = name };
        configure?.Invoke(workspace);
        return await store.CreateWorkspaceAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists all workspaces, most recently updated first.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>All workspaces.</returns>
    public Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken cancellationToken = default) =>
        store.ListWorkspacesAsync(cancellationToken);

    /// <summary>
    /// Retrieves a workspace by identifier.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The workspace, or null when not found.</returns>
    public Task<Workspace?> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        store.GetWorkspaceAsync(workspaceId, cancellationToken);

    /// <summary>
    /// Updates a workspace's settings.
    /// </summary>
    /// <param name="workspace">The workspace with updated values.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous update.</returns>
    public Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default) =>
        store.UpdateWorkspaceAsync(workspace, cancellationToken);

    /// <summary>
    /// Deletes a workspace and its memberships. Documents remain in the vector store
    /// because they may be referenced by other workspaces.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete.</returns>
    public Task DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) =>
        store.DeleteWorkspaceAsync(workspaceId, cancellationToken);

    /// <summary>
    /// Ingests text into a workspace with content-hash deduplication: when the same content
    /// was already ingested (in any workspace), the existing document is referenced instead
    /// of being embedded again.
    /// </summary>
    /// <param name="workspaceId">The target workspace identifier.</param>
    /// <param name="text">The text content to ingest.</param>
    /// <param name="documentName">A friendly name for the document.</param>
    /// <param name="metadata">Optional metadata for the document chunks.</param>
    /// <param name="pinned">Whether the document should be pinned (always in context).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The document identifier (existing when deduplicated, new otherwise).</returns>
    public async Task<string> IngestTextAsync(
        string workspaceId,
        string text,
        string documentName,
        Dictionary<string, object>? metadata = null,
        bool pinned = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentException.ThrowIfNullOrEmpty(documentName);

        var contentHash = ComputeContentHash(text);
        var documentId = await store.FindDocumentIdByHashAsync(contentHash, cancellationToken).ConfigureAwait(false);

        documentId ??= await rag.IngestTextAsync(text, documentName, metadata, cancellationToken).ConfigureAwait(false);

        await store.AddDocumentAsync(new WorkspaceDocument
        {
            WorkspaceId = workspaceId,
            DocumentId = documentId,
            ContentHash = contentHash,
            IsPinned = pinned
        }, cancellationToken).ConfigureAwait(false);

        return documentId;
    }

    /// <summary>
    /// Ingests a file into a workspace with content-hash deduplication.
    /// </summary>
    /// <param name="workspaceId">The target workspace identifier.</param>
    /// <param name="filePath">Absolute path to the file to ingest.</param>
    /// <param name="pinned">Whether the document should be pinned (always in context).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The document identifier (existing when deduplicated, new otherwise).</returns>
    public async Task<string> IngestFileAsync(
        string workspaceId,
        string filePath,
        bool pinned = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var documentId = await store.FindDocumentIdByHashAsync(contentHash, cancellationToken).ConfigureAwait(false);

        documentId ??= await rag.IngestAsync(filePath, cancellationToken).ConfigureAwait(false);

        await store.AddDocumentAsync(new WorkspaceDocument
        {
            WorkspaceId = workspaceId,
            DocumentId = documentId,
            ContentHash = contentHash,
            IsPinned = pinned
        }, cancellationToken).ConfigureAwait(false);

        return documentId;
    }

    /// <summary>
    /// References an already-ingested document from a workspace without re-embedding it.
    /// </summary>
    /// <param name="workspaceId">The target workspace identifier.</param>
    /// <param name="documentId">The existing document identifier.</param>
    /// <param name="contentHash">Optional content hash to record for deduplication.</param>
    /// <param name="pinned">Whether the document should be pinned.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous add.</returns>
    public Task AddExistingDocumentAsync(
        string workspaceId,
        string documentId,
        string? contentHash = null,
        bool pinned = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(documentId);

        return store.AddDocumentAsync(new WorkspaceDocument
        {
            WorkspaceId = workspaceId,
            DocumentId = documentId,
            ContentHash = contentHash ?? string.Empty,
            IsPinned = pinned
        }, cancellationToken);
    }

    /// <summary>
    /// Pins or unpins a workspace document. Pinned documents are always included in context.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="documentId">The document identifier.</param>
    /// <param name="pinned">True to pin, false to unpin.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous update.</returns>
    public Task SetPinnedAsync(string workspaceId, string documentId, bool pinned, CancellationToken cancellationToken = default) =>
        store.SetPinnedAsync(workspaceId, documentId, pinned, cancellationToken);

    /// <summary>
    /// Performs semantic search scoped to a workspace's documents, honoring the workspace's
    /// topK and similarity threshold settings.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="query">The natural language query.</param>
    /// <param name="topK">Optional topK override; defaults to the workspace setting or 5.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked results restricted to the workspace's documents.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workspace does not exist.</exception>
    /// <remarks>
    /// <para><b>Reranking (REQ-RAG-047):</b> The workspace's <see cref="Workspace.RerankEnabled"/>
    /// flag is passed down as <see cref="SearchOptions.Rerank"/>, so it decides reranking for this
    /// workspace and overrides the library-wide <c>Rerank.Enabled</c> setting in both directions.
    /// Two workspaces backed by the same vector store therefore get different result ordering when
    /// their flags differ, and changing one workspace's flag never affects another's.</para>
    /// </remarks>
    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string workspaceId,
        string query,
        int? topK = null,
        CancellationToken cancellationToken = default) =>
        SearchScopedAsync(workspaceId, query, null, topK, cancellationToken);

    /// <summary>
    /// Performs semantic search scoped to a workspace's documents and further narrowed by a
    /// per-turn retrieval scope (whole workspace, pinned documents only, or a chosen subset).
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="query">The natural language query.</param>
    /// <param name="overrides">Per-turn overrides supplying the retrieval scope; null means the
    /// whole workspace.</param>
    /// <param name="topK">Optional topK override; defaults to the workspace setting or 5.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked results restricted to the in-scope documents.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workspace does not exist.</exception>
    /// <remarks>
    /// <para><b>Purpose:</b> BRD-137 / REQ-UI-044. The scope can only ever narrow the workspace's
    /// own document set, so workspace isolation stays the outer boundary.</para>
    /// </remarks>
    public async Task<IReadOnlyList<SearchResult>> SearchScopedAsync(
        string workspaceId,
        string query,
        WorkspaceTurnOverrides? overrides,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(query);

        var workspace = await RequireWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        var scope = await ResolveScopeAsync(workspaceId, overrides, cancellationToken).ConfigureAwait(false);
        return await SearchInScopeAsync(workspace, query, scope, topK, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the oversampled vector search and filters it down to the documents already resolved
    /// as in scope for this call.
    /// </summary>
    /// <param name="workspace">The workspace supplying topK, threshold and rerank settings.</param>
    /// <param name="query">The natural language query.</param>
    /// <param name="scope">The resolved in-scope document set.</param>
    /// <param name="topK">Optional topK override.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked results restricted to <paramref name="scope"/>.</returns>
    private async Task<IReadOnlyList<SearchResult>> SearchInScopeAsync(
        Workspace workspace,
        string query,
        ScopedDocuments scope,
        int? topK,
        CancellationToken cancellationToken)
    {
        if (scope.DocumentIds.Count == 0) return [];

        var effectiveTopK = topK ?? workspace.TopK ?? 5;

        var candidates = await rag.SearchAsync(
            query,
            new SearchOptions
            {
                TopK = effectiveTopK * OversampleFactor,
                Rerank = workspace.RerankEnabled
            },
            cancellationToken).ConfigureAwait(false);

        var scoped = candidates
            .Where(r => scope.DocumentIds.Contains(r.Chunk.DocumentId))
            .Where(r => workspace.SimilarityThreshold is null || r.Score >= workspace.SimilarityThreshold.Value)
            .Take(effectiveTopK)
            .ToList();

        return scoped;
    }

    /// <summary>
    /// Resolves which of the workspace's documents this call may retrieve from, applying the
    /// per-turn retrieval scope.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="overrides">Per-turn overrides; null means the whole workspace.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The in-scope document ids together with the in-scope pinned documents.</returns>
    private async Task<ScopedDocuments> ResolveScopeAsync(
        string workspaceId,
        WorkspaceTurnOverrides? overrides,
        CancellationToken cancellationToken)
    {
        var documents = await store.ListDocumentsAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        var scope = overrides?.Scope ?? WorkspaceRetrievalScope.WholeWorkspace;

        IEnumerable<WorkspaceDocument> inScope = documents;
        if (scope == WorkspaceRetrievalScope.PinnedOnly)
        {
            inScope = documents.Where(d => d.IsPinned);
        }
        else if (scope == WorkspaceRetrievalScope.SelectedDocuments)
        {
            var chosen = (overrides?.DocumentIds ?? []).ToHashSet(StringComparer.Ordinal);
            inScope = documents.Where(d => chosen.Contains(d.DocumentId));
        }

        var materialized = inScope.ToList();
        return new ScopedDocuments(
            materialized.Select(d => d.DocumentId).ToHashSet(StringComparer.Ordinal),
            materialized.Where(d => d.IsPinned).ToList());
    }

    /// <summary>
    /// The workspace documents a single retrieval call is allowed to use.
    /// </summary>
    /// <param name="DocumentIds">Every in-scope document identifier.</param>
    /// <param name="Pinned">The in-scope documents that are pinned.</param>
    private sealed record ScopedDocuments(
        HashSet<string> DocumentIds,
        IReadOnlyList<WorkspaceDocument> Pinned);

    /// <summary>
    /// Performs a complete workspace-scoped RAG operation, applying the workspace's system
    /// prompt, topK, similarity threshold, pinned documents, and chat-vs-query mode.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="question">The user's question.</param>
    /// <param name="options">Optional LLM completion parameters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The answer with its workspace-scoped sources and token usage.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workspace does not exist
    /// or no LLM provider is configured.</exception>
    /// <remarks>
    /// <para><b>Query mode:</b> When the workspace's ChatMode is Query, the LLM is instructed
    /// to answer only from the retrieved context; when no context passes the similarity
    /// threshold, a deterministic "not covered" answer is returned without calling the LLM.</para>
    /// <para><b>Pinned documents:</b> The most relevant chunks of every pinned document are
    /// always merged into the context ahead of regular results.</para>
    /// </remarks>
    public async Task<RagResponse> AskAsync(
        string workspaceId,
        string question,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(question);

        var llmProvider = rag.GetLlmProvider()
            ?? throw new InvalidOperationException(NoLlmProviderMessage);

        var workspace = await RequireWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        var context = await ComposeContextAsync(workspace, question, null, cancellationToken).ConfigureAwait(false);
        var sources = context.Results;

        if (workspace.ChatMode == WorkspaceChatMode.Query && sources.Count == 0)
        {
            return new RagResponse
            {
                Answer = QueryModeNoContextAnswer,
                Sources = sources,
                Usage = new TokenUsage { ModelName = llmProvider.ModelName, ProviderName = llmProvider.Name },
                Query = question,
                ModelName = llmProvider.ModelName
            };
        }

        var systemPrompt = BuildSystemPrompt(workspace, null);
        var effectiveOptions = ApplyWorkspaceOptions(workspace, options, null);
        var messages = promptTemplate.BuildRagPrompt(question, sources, systemPrompt);
        var response = await llmProvider.ChatAsync(messages, effectiveOptions, cancellationToken).ConfigureAwait(false);

        return new RagResponse
        {
            Answer = response.Content ?? string.Empty,
            Sources = sources,
            Usage = response.Usage,
            Query = question,
            ModelName = response.ModelName
        };
    }

    /// <summary>
    /// Streams a workspace-scoped RAG answer, emitting the retrieval sources before the first
    /// answer token so a streaming UI can render citations immediately (BRD-109, TR-RAG-003).
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="question">The user's question.</param>
    /// <param name="conversationHistory">Optional prior turns; when supplied the chat prompt
    /// template is used so the workspace context is combined with history. Must not already
    /// contain <paramref name="question"/> as a trailing user turn — it is appended by the
    /// template.</param>
    /// <param name="options">Optional LLM completion parameters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async sequence of <see cref="RagStreamEvent"/> in the order
    /// Sources → Token(s) → Completed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workspace does not exist
    /// or no LLM provider is configured.</exception>
    /// <remarks>
    /// <para><b>Scoping:</b> Retrieval is restricted to this workspace's document set and honors
    /// the workspace's topK, similarity threshold, system prompt, LLM model override, and
    /// chat-vs-query mode — the same rules as <see cref="AskAsync"/>.</para>
    /// <para><b>Pinned documents:</b> The workspace's pinned documents are merged into the
    /// context ahead of regular results via <see cref="BuildContextAsync"/>, so pinning is
    /// honored while streaming (BRD-44). This is what a caller composing its own stream from
    /// <see cref="SearchAsync"/> plus a raw provider stream silently loses.</para>
    /// <para><b>Query mode:</b> When ChatMode is Query and nothing passes the threshold, the
    /// deterministic "not covered" answer is emitted as a single token followed by Completed,
    /// without calling — or even requiring — an LLM provider.</para>
    /// </remarks>
    public IAsyncEnumerable<RagStreamEvent> AskStreamWithSourcesAsync(
        string workspaceId,
        string question,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default) =>
        AskTurnStreamAsync(workspaceId, question, null, conversationHistory, options, cancellationToken);

    /// <summary>
    /// Streams a workspace-scoped RAG answer for a single turn, applying per-turn overrides for
    /// the answer mode, the model, and the retrieval scope on top of the workspace's settings.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="question">The user's question.</param>
    /// <param name="overrides">Per-turn overrides; null behaves exactly like
    /// <see cref="AskStreamWithSourcesAsync(string, string, IReadOnlyList{ChatMessage}, LlmCompletionOptions, CancellationToken)"/>.</param>
    /// <param name="conversationHistory">Optional prior turns; must not already contain
    /// <paramref name="question"/> as a trailing user turn.</param>
    /// <param name="options">Optional LLM completion parameters.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An async sequence of <see cref="RagStreamEvent"/> in the order
    /// Sources → Token(s) → Completed.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workspace does not exist
    /// or no LLM provider is configured.</exception>
    /// <remarks>
    /// <para><b>Purpose:</b> BRD-137 / REQ-UI-044 — the composer chooses the answer mode, model and
    /// retrieval scope per turn. BRD-48's chat-vs-query modes were previously reachable only by
    /// editing the workspace, so they could not be applied to a single question.</para>
    /// <para><b>No leakage:</b> <paramref name="overrides"/> is read for this call only and is
    /// never written back to the workspace store, and <paramref name="options"/> is copied rather
    /// than mutated, so a per-turn model cannot survive into the next turn.</para>
    /// </remarks>
    public async IAsyncEnumerable<RagStreamEvent> AskTurnStreamAsync(
        string workspaceId,
        string question,
        WorkspaceTurnOverrides? overrides,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(question);

        var workspace = await RequireWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        var context = await ComposeContextAsync(workspace, question, overrides, cancellationToken).ConfigureAwait(false);
        var sources = context.Results;

        yield return RagStreamEvent.FromSources(sources);

        if (EffectiveChatMode(workspace, overrides) == WorkspaceChatMode.Query && sources.Count == 0)
        {
            yield return RagStreamEvent.FromToken(QueryModeNoContextAnswer);
            yield return RagStreamEvent.FromCompleted(QueryModeNoContextAnswer);
            yield break;
        }

        var llmProvider = rag.GetLlmProvider() ?? throw new InvalidOperationException(NoLlmProviderMessage);
        var messages = BuildStreamPrompt(workspace, question, sources, conversationHistory, overrides);
        var effectiveOptions = ApplyWorkspaceOptions(workspace, options, overrides);
        var answer = new StringBuilder();

        await foreach (var token in llmProvider.ChatStreamAsync(messages, effectiveOptions, cancellationToken).ConfigureAwait(false))
        {
            answer.Append(token);
            yield return RagStreamEvent.FromToken(token);
        }

        yield return RagStreamEvent.FromCompleted(answer.ToString());
    }

    /// <summary>
    /// Composes the workspace context for a question: the workspace-scoped search results with
    /// the workspace's pinned documents merged in ahead of them.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="question">The question to retrieve context for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The composed context, pinned chunks first, de-duplicated by chunk id, and already
    /// trimmed to <c>PromptConfig.MaxContextChunks</c>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workspace does not exist.</exception>
    /// <remarks>
    /// <para><b>Purpose:</b> The reusable seam shared by <see cref="AskAsync"/> and
    /// <see cref="AskStreamWithSourcesAsync(string, string, IReadOnlyList{ChatMessage}, LlmCompletionOptions, CancellationToken)"/>,
    /// and exposed so callers building their own generation loop can reproduce the exact
    /// context TechieRag would use — including pinned documents that do not pass the
    /// similarity threshold.</para>
    /// <para><b>Truncation:</b> Use <see cref="BuildContextWithDiagnosticsAsync"/> or subscribe to
    /// <see cref="ContextTruncated"/> when you need to know that chunks were dropped to fit the
    /// context budget (REQ-RAG-048); this overload returns only the surviving chunks.</para>
    /// </remarks>
    public async Task<IReadOnlyList<SearchResult>> BuildContextAsync(
        string workspaceId,
        string question,
        CancellationToken cancellationToken = default)
    {
        var context = await BuildContextWithDiagnosticsAsync(workspaceId, question, cancellationToken)
            .ConfigureAwait(false);
        return context.Results;
    }

    /// <summary>
    /// Composes the workspace context for a question and reports whether the configured context
    /// budget forced any chunk to be evicted.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="question">The question to retrieve context for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The composed context together with pinned/retrieved inclusion and eviction counts.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workspace does not exist.</exception>
    /// <remarks>
    /// <para><b>Purpose:</b> REQ-RAG-048 (TR-RAG-006). <c>PromptConfig.MaxContextChunks</c>
    /// (default 5) used to truncate the merged pinned + retrieved context silently inside the
    /// prompt template, so more than five pinned documents evicted every retrieved result with no
    /// signal. Truncation is now applied here, deliberately, and reported.</para>
    /// <para><b>Eviction policy:</b> Pinned chunks keep their slots; retrieved chunks are dropped
    /// from the tail first. Pinned chunks are dropped only when they alone exceed the budget.</para>
    /// </remarks>
    public async Task<WorkspaceContext> BuildContextWithDiagnosticsAsync(
        string workspaceId,
        string question,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(question);

        var workspace = await RequireWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return await ComposeContextAsync(workspace, question, null, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<ChatMessage> BuildStreamPrompt(
        Workspace workspace,
        string question,
        IReadOnlyList<SearchResult> sources,
        IReadOnlyList<ChatMessage>? conversationHistory,
        WorkspaceTurnOverrides? overrides)
    {
        var systemPrompt = BuildSystemPrompt(workspace, overrides);

        return conversationHistory is null || conversationHistory.Count == 0
            ? promptTemplate.BuildRagPrompt(question, sources, systemPrompt)
            : promptTemplate.BuildRagChatPrompt(question, sources, conversationHistory, systemPrompt);
    }

    /// <summary>
    /// Merges the workspace's pinned chunks ahead of its retrieved chunks, de-duplicates by chunk
    /// id, applies the context budget, and signals truncation.
    /// </summary>
    /// <param name="workspace">The workspace being queried.</param>
    /// <param name="question">The question to retrieve context for.</param>
    /// <param name="overrides">Per-turn overrides supplying the retrieval scope; null means the
    /// whole workspace.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The budgeted context with its truncation diagnostics.</returns>
    private async Task<WorkspaceContext> ComposeContextAsync(
        Workspace workspace,
        string question,
        WorkspaceTurnOverrides? overrides,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(workspace.WorkspaceId, overrides, cancellationToken).ConfigureAwait(false);
        var retrieved = await SearchInScopeAsync(workspace, question, scope, null, cancellationToken)
            .ConfigureAwait(false);
        var pinned = await CollectPinnedChunksAsync(question, scope, cancellationToken)
            .ConfigureAwait(false);

        var seenChunks = new HashSet<string>(StringComparer.Ordinal);
        var pinnedUnique = pinned.Where(r => seenChunks.Add(r.Chunk.Id)).ToList();
        var retrievedUnique = retrieved.Where(r => seenChunks.Add(r.Chunk.Id)).ToList();

        var context = ApplyContextBudget(pinnedUnique, retrievedUnique);
        SignalTruncation(workspace.WorkspaceId, question, context);
        return context;
    }

    /// <summary>
    /// Retrieves the most relevant chunks of every in-scope pinned document.
    /// </summary>
    /// <param name="question">The question the chunks are scored against.</param>
    /// <param name="scope">The resolved in-scope document set for this call.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Pinned chunks in pinned-document order, at most
    /// <see cref="PinnedChunksPerDocument"/> per document, not yet de-duplicated.</returns>
    private async Task<List<SearchResult>> CollectPinnedChunksAsync(
        string question,
        ScopedDocuments scope,
        CancellationToken cancellationToken)
    {
        var chunks = new List<SearchResult>();

        foreach (var pinned in scope.Pinned)
        {
            var pinnedChunks = await rag.SearchAsync(
                question,
                PinnedChunksPerDocument,
                pinned.DocumentId,
                cancellationToken).ConfigureAwait(false);
            chunks.AddRange(pinnedChunks);
        }

        return chunks;
    }

    /// <summary>
    /// Trims the merged context to <c>PromptConfig.MaxContextChunks</c> using an explicit
    /// pinned-before-retrieved eviction order.
    /// </summary>
    /// <param name="pinned">Pinned chunks, highest priority first.</param>
    /// <param name="retrieved">Retrieved chunks, most relevant first.</param>
    /// <returns>The budgeted context with per-category inclusion and eviction counts.</returns>
    /// <remarks>
    /// <para><b>Policy (REQ-RAG-048):</b> Retrieved chunks are evicted from the tail before any
    /// pinned chunk is touched. Pinned chunks are evicted, lowest-priority first, only when the
    /// pinned set alone exceeds the budget — the "more than five pinned documents" case. A budget
    /// of zero or less disables trimming entirely.</para>
    /// </remarks>
    private WorkspaceContext ApplyContextBudget(
        IReadOnlyList<SearchResult> pinned,
        IReadOnlyList<SearchResult> retrieved)
    {
        var limit = promptConfig.MaxContextChunks;
        var unlimited = limit <= 0;

        var pinnedKept = unlimited ? pinned.Count : Math.Min(pinned.Count, limit);
        var retrievedKept = unlimited
            ? retrieved.Count
            : Math.Max(0, Math.Min(retrieved.Count, limit - pinnedKept));

        return new WorkspaceContext
        {
            Results = pinned.Take(pinnedKept).Concat(retrieved.Take(retrievedKept)).ToList(),
            PinnedIncluded = pinnedKept,
            RetrievedIncluded = retrievedKept,
            PinnedEvicted = pinned.Count - pinnedKept,
            RetrievedEvicted = retrieved.Count - retrievedKept,
            MaxContextChunks = limit
        };
    }

    /// <summary>
    /// Logs and raises <see cref="ContextTruncated"/> when the context budget dropped chunks.
    /// </summary>
    /// <param name="workspaceId">The workspace whose context was composed.</param>
    /// <param name="question">The question the context was composed for.</param>
    /// <param name="context">The budgeted context.</param>
    private void SignalTruncation(string workspaceId, string question, WorkspaceContext context)
    {
        if (!context.WasTruncated) return;

        logger.LogWarning(
            "Workspace {WorkspaceId} context truncated to MaxContextChunks={Limit}: dropped {PinnedEvicted} pinned and {RetrievedEvicted} retrieved chunk(s).",
            workspaceId, context.MaxContextChunks, context.PinnedEvicted, context.RetrievedEvicted);

        ContextTruncated?.Invoke(this, new ContextTruncatedEventArgs
        {
            WorkspaceId = workspaceId,
            Question = question,
            Context = context
        });
    }

    /// <summary>
    /// Resolves the answer mode for a call: the per-turn override when supplied, otherwise the
    /// workspace's stored mode (BRD-137 / REQ-UI-044).
    /// </summary>
    /// <param name="workspace">The workspace being queried.</param>
    /// <param name="overrides">Per-turn overrides, or null.</param>
    /// <returns>The mode that governs this call.</returns>
    private static WorkspaceChatMode EffectiveChatMode(Workspace workspace, WorkspaceTurnOverrides? overrides) =>
        overrides?.ChatMode ?? workspace.ChatMode;

    private static string? BuildSystemPrompt(Workspace workspace, WorkspaceTurnOverrides? overrides)
    {
        var basePrompt = workspace.SystemPrompt;
        if (EffectiveChatMode(workspace, overrides) != WorkspaceChatMode.Query) return basePrompt;

        return string.IsNullOrEmpty(basePrompt)
            ? QueryModeInstruction
            : $"{basePrompt}\n\n{QueryModeInstruction}";
    }

    /// <summary>
    /// Produces the completion options for a call by layering the per-turn model over the
    /// workspace model, without mutating the caller's instance.
    /// </summary>
    /// <param name="workspace">The workspace supplying the default model override.</param>
    /// <param name="options">The caller's options, or null.</param>
    /// <param name="overrides">Per-turn overrides whose model wins when set.</param>
    /// <returns>Options carrying the effective model, or the caller's options unchanged when no
    /// model override applies.</returns>
    /// <remarks>
    /// <para><b>No leakage (REQ-UI-044):</b> a copy is returned rather than the caller's instance.
    /// The previous in-place assignment stamped the model onto a caller-owned object, so a UI that
    /// reused one options instance carried a one-turn model choice into every later turn.</para>
    /// </remarks>
    private static LlmCompletionOptions? ApplyWorkspaceOptions(
        Workspace workspace,
        LlmCompletionOptions? options,
        WorkspaceTurnOverrides? overrides)
    {
        var model = !string.IsNullOrEmpty(overrides?.LlmModel) ? overrides.LlmModel : workspace.LlmModel;
        if (string.IsNullOrEmpty(model)) return options;

        return Copy(options, model);
    }

    /// <summary>
    /// Shallow-copies completion options, replacing the model.
    /// </summary>
    /// <param name="options">The options to copy; null yields a fresh instance.</param>
    /// <param name="model">The model to stamp on the copy.</param>
    /// <returns>A new options instance the caller does not own.</returns>
    private static LlmCompletionOptions Copy(LlmCompletionOptions? options, string model) =>
        new()
        {
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxTokens,
            TopP = options?.TopP,
            FrequencyPenalty = options?.FrequencyPenalty,
            PresencePenalty = options?.PresencePenalty,
            StopSequences = options?.StopSequences,
            SystemPrompt = options?.SystemPrompt,
            JsonMode = options?.JsonMode ?? false,
            JsonSchema = options?.JsonSchema,
            Tools = options?.Tools,
            ToolChoice = options?.ToolChoice,
            Seed = options?.Seed,
            Model = model
        };

    private async Task<Workspace> RequireWorkspaceAsync(string workspaceId, CancellationToken cancellationToken)
    {
        return await store.GetWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' does not exist.");
    }

    private static string ComputeContentHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
