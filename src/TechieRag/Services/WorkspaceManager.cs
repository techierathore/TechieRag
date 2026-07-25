using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// Creates a new workspace manager.
    /// </summary>
    /// <param name="rag">The TechieRag client used for ingestion, retrieval, and generation.</param>
    /// <param name="store">The persistent workspace store.</param>
    /// <param name="promptTemplate">The prompt template used to build workspace RAG prompts.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public WorkspaceManager(ITechieRag rag, IWorkspaceStore store, IPromptTemplate promptTemplate)
    {
        ArgumentNullException.ThrowIfNull(rag);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(promptTemplate);

        this.rag = rag;
        this.store = store;
        this.promptTemplate = promptTemplate;
    }

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
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string workspaceId,
        string query,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(query);

        var workspace = await RequireWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        var documents = await store.ListDocumentsAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        if (documents.Count == 0) return [];

        var effectiveTopK = topK ?? workspace.TopK ?? 5;
        var documentIds = documents.Select(d => d.DocumentId).ToHashSet(StringComparer.Ordinal);

        var candidates = await rag.SearchAsync(
            query,
            effectiveTopK * OversampleFactor,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var scoped = candidates
            .Where(r => documentIds.Contains(r.Chunk.DocumentId))
            .Where(r => workspace.SimilarityThreshold is null || r.Score >= workspace.SimilarityThreshold.Value)
            .Take(effectiveTopK)
            .ToList();

        return scoped;
    }

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
        var sources = await ComposeContextAsync(workspace, question, cancellationToken).ConfigureAwait(false);

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

        var systemPrompt = BuildSystemPrompt(workspace);
        var effectiveOptions = ApplyWorkspaceOptions(workspace, options);
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
    public async IAsyncEnumerable<RagStreamEvent> AskStreamWithSourcesAsync(
        string workspaceId,
        string question,
        IReadOnlyList<ChatMessage>? conversationHistory = null,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(question);

        var workspace = await RequireWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        var sources = await ComposeContextAsync(workspace, question, cancellationToken).ConfigureAwait(false);

        yield return RagStreamEvent.FromSources(sources);

        if (workspace.ChatMode == WorkspaceChatMode.Query && sources.Count == 0)
        {
            yield return RagStreamEvent.FromToken(QueryModeNoContextAnswer);
            yield return RagStreamEvent.FromCompleted(QueryModeNoContextAnswer);
            yield break;
        }

        var llmProvider = rag.GetLlmProvider() ?? throw new InvalidOperationException(NoLlmProviderMessage);
        var messages = BuildStreamPrompt(workspace, question, sources, conversationHistory);
        var effectiveOptions = ApplyWorkspaceOptions(workspace, options);
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
    /// <returns>The composed context, pinned chunks first, de-duplicated by chunk id.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the workspace does not exist.</exception>
    /// <remarks>
    /// <para><b>Purpose:</b> The reusable seam shared by <see cref="AskAsync"/> and
    /// <see cref="AskStreamWithSourcesAsync(string, string, IReadOnlyList{ChatMessage}, LlmCompletionOptions, CancellationToken)"/>,
    /// and exposed so callers building their own generation loop can reproduce the exact
    /// context TechieRag would use — including pinned documents that do not pass the
    /// similarity threshold.</para>
    /// </remarks>
    public async Task<IReadOnlyList<SearchResult>> BuildContextAsync(
        string workspaceId,
        string question,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        ArgumentException.ThrowIfNullOrEmpty(question);

        var workspace = await RequireWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return await ComposeContextAsync(workspace, question, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<ChatMessage> BuildStreamPrompt(
        Workspace workspace,
        string question,
        IReadOnlyList<SearchResult> sources,
        IReadOnlyList<ChatMessage>? conversationHistory)
    {
        var systemPrompt = BuildSystemPrompt(workspace);

        return conversationHistory is null || conversationHistory.Count == 0
            ? promptTemplate.BuildRagPrompt(question, sources, systemPrompt)
            : promptTemplate.BuildRagChatPrompt(question, sources, conversationHistory, systemPrompt);
    }

    private async Task<IReadOnlyList<SearchResult>> ComposeContextAsync(
        Workspace workspace,
        string question,
        CancellationToken cancellationToken)
    {
        var results = await SearchAsync(workspace.WorkspaceId, question, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var documents = await store.ListDocumentsAsync(workspace.WorkspaceId, cancellationToken).ConfigureAwait(false);
        var pinnedDocuments = documents.Where(d => d.IsPinned).ToList();
        if (pinnedDocuments.Count == 0) return results;

        var merged = new List<SearchResult>();
        var seenChunks = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pinned in pinnedDocuments)
        {
            var pinnedChunks = await rag.SearchAsync(
                question,
                PinnedChunksPerDocument,
                pinned.DocumentId,
                cancellationToken).ConfigureAwait(false);

            foreach (var result in pinnedChunks)
            {
                if (seenChunks.Add(result.Chunk.Id))
                {
                    merged.Add(result);
                }
            }
        }

        foreach (var result in results)
        {
            if (seenChunks.Add(result.Chunk.Id))
            {
                merged.Add(result);
            }
        }

        return merged;
    }

    private static string? BuildSystemPrompt(Workspace workspace)
    {
        var basePrompt = workspace.SystemPrompt;
        if (workspace.ChatMode != WorkspaceChatMode.Query) return basePrompt;

        return string.IsNullOrEmpty(basePrompt)
            ? QueryModeInstruction
            : $"{basePrompt}\n\n{QueryModeInstruction}";
    }

    private static LlmCompletionOptions? ApplyWorkspaceOptions(Workspace workspace, LlmCompletionOptions? options)
    {
        if (string.IsNullOrEmpty(workspace.LlmModel)) return options;

        options ??= new LlmCompletionOptions();
        options.Model = workspace.LlmModel;
        return options;
    }

    private async Task<Workspace> RequireWorkspaceAsync(string workspaceId, CancellationToken cancellationToken)
    {
        return await store.GetWorkspaceAsync(workspaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Workspace '{workspaceId}' does not exist.");
    }

    private static string ComputeContentHash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
