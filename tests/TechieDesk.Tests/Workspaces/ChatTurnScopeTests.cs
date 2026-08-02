using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Workspaces;
using TechieRag;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Services;
using Xunit;

namespace TechieDesk.Tests.Workspaces;

/// <summary>
/// Drives the exact call path WorkspaceChat.razor uses for a turn — a real
/// <see cref="WorkspaceManager"/> over a real temporary SQLite workspace store with in-memory
/// embedding, vector store and LLM — and proves the composer's per-turn mode, model and retrieval
/// scope actually change what the turn does (REQ-UI-044 / BRD-137).
/// </summary>
public class ChatTurnScopeTests : IDisposable
{
    private const string PinnedDoc = "doc-pinned";
    private const string PlainDoc = "doc-plain";
    private const string OtherDoc = "doc-other";

    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"tdturnscope-{Guid.NewGuid():N}.db");
    private readonly SqliteWorkspaceStore store;

    /// <summary>Creates a workspace store over a unique temp database file.</summary>
    public ChatTurnScopeTests() => store = new SqliteWorkspaceStore($"Data Source={dbPath}");

    /// <summary>
    /// The default whole-workspace scope retrieves from every document the workspace owns, which
    /// is the behaviour the narrower scopes have to be measured against.
    /// </summary>
    [Fact]
    public async Task WholeWorkspaceScopeRetrievesEveryDocument()
    {
        var manager = CreateManager(out _);
        var workspace = await SeedWorkspaceAsync(manager);
        var composer = new ChatComposerState { Scope = WorkspaceRetrievalScope.WholeWorkspace };

        var sources = await FirstSourcesAsync(manager, workspace, composer);

        Assert.Contains(sources, s => s.Chunk.DocumentId == PinnedDoc);
        Assert.Contains(sources, s => s.Chunk.DocumentId == PlainDoc);
    }

    /// <summary>
    /// The pinned-only scope drops every unpinned document from retrieval, so a turn can be
    /// answered from the workspace's pinned reference material alone.
    /// </summary>
    [Fact]
    public async Task PinnedOnlyScopeExcludesUnpinnedDocuments()
    {
        var manager = CreateManager(out _);
        var workspace = await SeedWorkspaceAsync(manager);
        var composer = new ChatComposerState { Scope = WorkspaceRetrievalScope.PinnedOnly };

        var sources = await FirstSourcesAsync(manager, workspace, composer);

        Assert.NotEmpty(sources);
        Assert.All(sources, s => Assert.Equal(PinnedDoc, s.Chunk.DocumentId));
    }

    /// <summary>
    /// The chosen-documents scope restricts retrieval to exactly the ticked documents, even when
    /// the excluded document is the higher-scoring match.
    /// </summary>
    [Fact]
    public async Task ChosenDocumentsScopeRetrievesOnlyThoseDocuments()
    {
        var manager = CreateManager(out _);
        var workspace = await SeedWorkspaceAsync(manager);
        var composer = new ChatComposerState { Scope = WorkspaceRetrievalScope.SelectedDocuments };
        composer.SelectedDocumentIds.Add(PlainDoc);

        var sources = await FirstSourcesAsync(manager, workspace, composer);

        Assert.NotEmpty(sources);
        Assert.All(sources, s => Assert.Equal(PlainDoc, s.Chunk.DocumentId));
    }

    /// <summary>
    /// A retrieval scope can only ever narrow the workspace's own document set: naming a document
    /// that belongs to another workspace retrieves nothing rather than reaching across the
    /// isolation boundary.
    /// </summary>
    [Fact]
    public async Task ChosenDocumentsScopeCannotReachOutsideTheWorkspace()
    {
        var manager = CreateManager(out _);
        var workspace = await SeedWorkspaceAsync(manager);
        var composer = new ChatComposerState { Scope = WorkspaceRetrievalScope.SelectedDocuments };
        composer.SelectedDocumentIds.Add(OtherDoc);

        var sources = await FirstSourcesAsync(manager, workspace, composer);

        Assert.Empty(sources);
    }

    /// <summary>
    /// Picking Query for one turn makes the turn answer strictly from the documents: with nothing
    /// in scope the deterministic "not covered" answer is returned and the LLM is never called,
    /// while the workspace stays a Chat workspace.
    /// </summary>
    [Fact]
    public async Task QueryModeTurnAnswersFromDocumentsOnly()
    {
        var manager = CreateManager(out var llm);
        var workspace = await SeedWorkspaceAsync(manager);
        var composer = new ChatComposerState { Mode = ChatAnswerMode.Query, Scope = WorkspaceRetrievalScope.SelectedDocuments };
        composer.SelectedDocumentIds.Add(OtherDoc);

        var answer = await RunTurnAsync(manager, workspace, composer);

        Assert.Contains("do not contain", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, llm.CallCount);
        Assert.Equal(WorkspaceChatMode.Chat, workspace.ChatMode);
    }

    /// <summary>
    /// The same empty-scope turn under Auto-RAG reaches the LLM, proving the Query selection —
    /// not the empty result set — is what suppressed generation in the previous test.
    /// </summary>
    [Fact]
    public async Task AutoRagTurnStillCallsTheModelWithNoContext()
    {
        var manager = CreateManager(out var llm);
        var workspace = await SeedWorkspaceAsync(manager);
        var composer = new ChatComposerState { Mode = ChatAnswerMode.AutoRag, Scope = WorkspaceRetrievalScope.SelectedDocuments };
        composer.SelectedDocumentIds.Add(OtherDoc);

        await RunTurnAsync(manager, workspace, composer);

        Assert.Equal(1, llm.CallCount);
    }

    /// <summary>
    /// The per-turn model reaches the provider for the turn it was chosen for, and the very next
    /// turn falls back to the workspace model — the override cannot leak forward.
    /// </summary>
    [Fact]
    public async Task PerTurnModelReachesTheProviderForOneTurnOnly()
    {
        var manager = CreateManager(out var llm);
        var workspace = await SeedWorkspaceAsync(manager);
        workspace.LlmModel = "workspace-model";
        await manager.UpdateWorkspaceAsync(workspace);

        var composer = new ChatComposerState { TurnModel = "one-off-model" };

        await RunTurnAsync(manager, workspace, composer);
        Assert.Equal("one-off-model", llm.LastOptions?.Model);

        await RunTurnAsync(manager, workspace, composer);
        Assert.Equal("workspace-model", llm.LastOptions?.Model);
    }

    /// <summary>
    /// A turn never writes its model back onto the caller's options object, so a page that reuses
    /// one options instance across turns cannot carry a one-off model into every later turn.
    /// </summary>
    [Fact]
    public async Task PerTurnModelDoesNotMutateTheCallersOptions()
    {
        var manager = CreateManager(out var llm);
        var workspace = await SeedWorkspaceAsync(manager);
        var reused = new LlmCompletionOptions { Temperature = 0.3f };

        var overrides = new WorkspaceTurnOverrides { LlmModel = "one-off-model" };
        await DrainAsync(manager.AskTurnStreamAsync(workspace.WorkspaceId, "question", overrides, options: reused));

        Assert.Equal("one-off-model", llm.LastOptions?.Model);
        Assert.Null(reused.Model);

        await DrainAsync(manager.AskTurnStreamAsync(workspace.WorkspaceId, "question", null, options: reused));
        Assert.Null(llm.LastOptions?.Model);
    }

    /// <summary>
    /// Passing no overrides at all behaves exactly like the pre-REQ-UI-044 call, so existing
    /// callers of the workspace stream are unaffected by the per-turn seam.
    /// </summary>
    [Fact]
    public async Task NoOverridesBehavesLikeTheWorkspaceDefaults()
    {
        var manager = CreateManager(out _);
        var workspace = await SeedWorkspaceAsync(manager);

        var withNull = await FirstSourcesAsync(manager, workspace.WorkspaceId, null);
        var legacy = new List<SearchResult>();
        await foreach (var evt in manager.AskStreamWithSourcesAsync(workspace.WorkspaceId, "question"))
        {
            if (evt.Type == RagStreamEventType.Sources) legacy.AddRange(evt.Sources!);
        }

        Assert.Equal(
            legacy.Select(s => s.Chunk.DocumentId),
            withNull.Select(s => s.Chunk.DocumentId));
    }

    private static async Task<IReadOnlyList<SearchResult>> FirstSourcesAsync(
        WorkspaceManager manager,
        Workspace workspace,
        ChatComposerState composer) =>
        await FirstSourcesAsync(manager, workspace.WorkspaceId, composer.TakeTurn(workspace).Overrides);

    private static async Task<IReadOnlyList<SearchResult>> FirstSourcesAsync(
        WorkspaceManager manager,
        string workspaceId,
        WorkspaceTurnOverrides? overrides)
    {
        await foreach (var evt in manager.AskTurnStreamAsync(workspaceId, "question", overrides))
        {
            if (evt.Type == RagStreamEventType.Sources) return evt.Sources!;
        }

        throw new InvalidOperationException("The stream produced no Sources event.");
    }

    private static async Task<string> RunTurnAsync(
        WorkspaceManager manager,
        Workspace workspace,
        ChatComposerState composer)
    {
        var plan = composer.TakeTurn(workspace);
        var answer = string.Empty;
        await foreach (var evt in manager.AskTurnStreamAsync(
            workspace.WorkspaceId, "question", plan.Overrides))
        {
            if (evt.Type == RagStreamEventType.Completed) answer = evt.Answer ?? string.Empty;
        }

        return answer;
    }

    private static async Task DrainAsync(IAsyncEnumerable<RagStreamEvent> stream)
    {
        await foreach (var _ in stream) { }
    }

    private async Task<Workspace> SeedWorkspaceAsync(WorkspaceManager manager)
    {
        var workspace = await manager.CreateWorkspaceAsync("Contracts");
        await manager.AddExistingDocumentAsync(workspace.WorkspaceId, PinnedDoc, pinned: true);
        await manager.AddExistingDocumentAsync(workspace.WorkspaceId, PlainDoc);
        return workspace;
    }

    private WorkspaceManager CreateManager(out RecordingLlmProvider llm)
    {
        llm = new RecordingLlmProvider("ok");
        var results = new[]
        {
            Result(PinnedDoc, "pinned passage", 0.95f),
            Result(PlainDoc, "plain passage", 0.90f),
            Result(OtherDoc, "other workspace passage", 0.99f)
        };

        var client = new TechieRagClient(
            new StubVectorStore(results),
            new StubEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            new TechieRagConfig(),
            NullLogger<TechieRagClient>.Instance,
            llmProvider: llm,
            workspaceStore: store);

        return client.GetWorkspaceManager()!;
    }

    private static SearchResult Result(string documentId, string text, float score) => new()
    {
        Chunk = new TextChunk { DocumentId = documentId, Text = text },
        Score = score
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
        GC.SuppressFinalize(this);
    }
}

/// <summary>Deterministic embedding provider so the tests need no embedding service.</summary>
internal sealed class StubEmbeddingProvider : IEmbeddingProvider
{
    /// <inheritdoc/>
    public string Name => "Stub";

    /// <inheritdoc/>
    public string ModelName => "stub-embed";

    /// <inheritdoc/>
    public int Dimensions => 3;

    /// <inheritdoc/>
#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;
#pragma warning restore CS0067

    /// <inheritdoc/>
    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(new float[] { 0.1f, 0.2f, 0.3f });

    /// <inheritdoc/>
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 0.1f, 0.2f, 0.3f }).ToList());
}

/// <summary>Vector store returning a fixed candidate set, filterable by document id.</summary>
internal sealed class StubVectorStore : IVectorStore
{
    private readonly List<SearchResult> results;

    /// <summary>Creates a store that returns the given results from every search.</summary>
    /// <param name="results">The candidates, highest relevance first.</param>
    public StubVectorStore(IEnumerable<SearchResult> results) => this.results = results.ToList();

    /// <inheritdoc/>
    public string Name => "StubVectorStore";

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<string> UpsertAsync(TextChunk chunk, CancellationToken cancellationToken = default) =>
        Task.FromResult(chunk.Id);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(chunks.Select(c => c.Id).ToList());

    /// <inheritdoc/>
    public Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default)
    {
        var filtered = documentFilter is null
            ? results
            : results.Where(r => r.Chunk.DocumentId == documentFilter).ToList();
        return Task.FromResult<IReadOnlyList<SearchResult>>(filtered.Take(topK).ToList());
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Document>>([]);

    /// <inheritdoc/>
    public Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new IngestionStats());

    /// <inheritdoc/>
    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// LLM provider that records the completion options and call count of every request, so the
/// per-turn model override can be asserted at the seam the real providers read it from.
/// </summary>
internal sealed class RecordingLlmProvider : ILlmProvider
{
    private readonly string[] tokens;

    /// <summary>Creates a provider that streams the given tokens from every call.</summary>
    /// <param name="tokens">The tokens to stream, in order.</param>
    public RecordingLlmProvider(params string[] tokens) => this.tokens = tokens;

    /// <summary>Gets the options passed to the most recent chat/stream call.</summary>
    public LlmCompletionOptions? LastOptions { get; private set; }

    /// <summary>Gets how many chat/stream calls the provider has served.</summary>
    public int CallCount { get; private set; }

    /// <inheritdoc/>
    public string Name => "RecordingLlm";

    /// <inheritdoc/>
    public string ModelName => "recording-model";

    /// <inheritdoc/>
    public bool SupportsToolCalling => false;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;
#pragma warning restore CS0067

    /// <inheritdoc/>
    public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        CallCount++;
        return Task.FromResult(new LlmResponse { Content = string.Concat(tokens), Usage = new TokenUsage() });
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        CallCount++;
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }

    /// <inheritdoc/>
    public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        CallCount++;
        return Task.FromResult(new LlmResponse { Content = string.Concat(tokens), Usage = new TokenUsage(), ModelName = ModelName });
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastOptions = options;
        CallCount++;
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }

    /// <inheritdoc/>
    public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public int EstimateTokenCount(string text) => Math.Max(1, text.Length / 4);
}
