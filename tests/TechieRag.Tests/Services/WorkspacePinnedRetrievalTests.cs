using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.DependencyInjection;
using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Services;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// Pinned-document retrieval (REQ-RAG-114 / BRD-174, REQ-RAG-115 / BRD-175) and the
/// <see cref="IWorkspaceManager"/> seam (REQ-RAG-116 / BRD-176), from Sevak feedback TR-RAG-010,
/// TR-RAG-011 and TR-RAG-013.
/// </summary>
public sealed class WorkspacePinnedRetrievalTests : IDisposable
{
    private static readonly string[] PinnedIds = ["pin-1", "pin-2", "pin-3", "pin-4", "pin-5"];

    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"trwspin-{Guid.NewGuid():N}.db");
    private readonly SqliteWorkspaceStore store;

    /// <summary>Creates a workspace store over a unique temp database file.</summary>
    public WorkspacePinnedRetrievalTests() => store = new SqliteWorkspaceStore($"Data Source={dbPath}");

    /// <summary>
    /// A developer asks a question in a workspace with five pinned documents: the pinned chunks come
    /// from ONE store search filtered to all five documents, not five single-document searches, and
    /// every pinned document still reaches the context.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-114 PinnedDocumentsUseOneSearch")]
    public async Task PinnedDocumentsUseOneSearch()
    {
        var vectorStore = new CountingVectorStore(PinnedIds.Select((id, i) => TestData.Result(id, $"text {id}", 0.9f - (i * 0.1f))));
        var manager = CreateManager(vectorStore, new CountingReranker(), globalRerank: false, maxContextChunks: 0);
        var workspace = await CreatePinnedWorkspaceAsync(manager, rerank: false);

        var context = await manager.BuildContextAsync(workspace.WorkspaceId, "what do the pinned documents say?");

        var pinnedSearch = Assert.Single(vectorStore.DocumentSetSearches);
        Assert.Equal(PinnedIds.Order(), pinnedSearch.Order());
        Assert.Empty(vectorStore.SingleDocumentSearches);
        Assert.Equal(PinnedIds, context.Select(r => r.Chunk.DocumentId).Distinct());
    }

    /// <summary>
    /// The SQLite store answers a document-set search with chunks from those documents only.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-114 SqliteDocumentSetSearchFiltersToTheSet")]
    public async Task SqliteDocumentSetSearchFiltersToTheSet()
    {
        var vectorPath = Path.Combine(Path.GetTempPath(), $"trvecset-{Guid.NewGuid():N}.db");
        try
        {
            var sqlite = new TechieRag.VectorStores.SqliteVecStore($"Data Source={vectorPath}", 3);
            await sqlite.UpsertBatchAsync(
            [
                new TextChunk { DocumentId = "a", Text = "a", Vector = [1f, 0f, 0f] },
                new TextChunk { DocumentId = "b", Text = "b", Vector = [0.9f, 0.1f, 0f] },
                new TextChunk { DocumentId = "c", Text = "c", Vector = [1f, 0f, 0f] }
            ]);

            var results = await sqlite.SearchDocumentsAsync([1f, 0f, 0f], 10, ["a", "b"]);

            Assert.Equal(["a", "b"], results.Select(r => r.Chunk.DocumentId));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(vectorPath);
        }
    }

    /// <summary>
    /// A workspace turns reranking off while the library default has it on: the pinned results are
    /// not reranked — the reranker is never called for this workspace's retrieval.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-115 PinnedResultsFollowWorkspaceRerankOff")]
    public async Task PinnedResultsFollowWorkspaceRerankOff()
    {
        var reranker = new CountingReranker();
        var vectorStore = new CountingVectorStore(PinnedIds.Select((id, i) => TestData.Result(id, $"text {id}", 0.9f - (i * 0.1f))));
        var manager = CreateManager(vectorStore, reranker, globalRerank: true, maxContextChunks: 0);
        var workspace = await CreatePinnedWorkspaceAsync(manager, rerank: false);

        var context = await manager.BuildContextAsync(workspace.WorkspaceId, "q");

        Assert.Equal(0, reranker.Calls);
        Assert.Equal(PinnedIds, context.Select(r => r.Chunk.DocumentId).Distinct());
    }

    /// <summary>
    /// The converse: a workspace that turns reranking on while the library default is off gets its
    /// pinned results reranked too.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-115 PinnedResultsFollowWorkspaceRerankOn")]
    public async Task PinnedResultsFollowWorkspaceRerankOn()
    {
        var reranker = new CountingReranker();
        var vectorStore = new CountingVectorStore(PinnedIds.Select((id, i) => TestData.Result(id, $"text {id}", 0.9f - (i * 0.1f))));
        var manager = CreateManager(vectorStore, reranker, globalRerank: false, maxContextChunks: 0);
        var workspace = await CreatePinnedWorkspaceAsync(manager, rerank: true);

        await manager.BuildContextAsync(workspace.WorkspaceId, "q");

        // One call for the workspace search and one for the pinned search; before REQ-RAG-115 the
        // pinned search followed the library default (off) and only the first call happened.
        Assert.Equal(2, reranker.Calls);
        Assert.Single(vectorStore.DocumentSetSearches);
    }

    /// <summary>
    /// A developer passes a test double of <see cref="IWorkspaceManager"/> to a service: the service
    /// runs without building a library client, a vector store or an embedder.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-116 ServiceRunsOnWorkspaceManagerTestDouble")]
    public async Task ServiceRunsOnWorkspaceManagerTestDouble()
    {
        var service = new WorkspaceTitleService(new StubWorkspaceManager());

        var titles = await service.TitlesAsync();

        Assert.Equal(["Contracts", "Support"], titles);
    }

    /// <summary>
    /// The library implements the interface and accepts a stand-in through it: <c>AddTechieRag</c>
    /// registers <see cref="IWorkspaceManager"/> with TryAdd, so a stand-in registered first wins.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-116 LibraryAcceptsWorkspaceManagerStandIn")]
    public void LibraryAcceptsWorkspaceManagerStandIn()
    {
        Assert.True(typeof(IWorkspaceManager).IsAssignableFrom(typeof(WorkspaceManager)));

        var standIn = new StubWorkspaceManager();
        var services = new ServiceCollection();
        services.AddSingleton<IWorkspaceManager>(standIn);
        services.AddTechieRag(builder => builder.UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider()));

        using var provider = services.BuildServiceProvider();

        Assert.Same(standIn, provider.GetRequiredService<IWorkspaceManager>());
    }

    /// <summary>
    /// A host that registers TechieRag without persistence and asks the container for
    /// <see cref="IWorkspaceManager"/> gets an error that names the missing persistence and the way
    /// out, not a null that fails later somewhere else.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-116 ResolvingWithoutPersistenceSaysWhy")]
    public void ResolvingWithoutPersistenceSaysWhy()
    {
        var folder = Path.Combine(Path.GetTempPath(), "techierag-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var services = new ServiceCollection();
            services.AddTechieRag(builder => builder
                .UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider())
                .UseVectorStore(VectorStoreType.SqliteVec, "Data Source=" + Path.Combine(folder, "store.db")));

            using var provider = services.BuildServiceProvider();

            var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IWorkspaceManager>());
            Assert.Contains("WithPersistence", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
    }

    private WorkspaceManager CreateManager(IVectorStore vectorStore, IReranker reranker, bool globalRerank, int maxContextChunks)
    {
        var config = new TechieRagConfig();
        config.Rerank.Enabled = globalRerank;
        config.Prompt.MaxContextChunks = maxContextChunks;

        var client = new TechieRagClient(
            vectorStore,
            new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            config,
            NullLogger<TechieRagClient>.Instance,
            reranker: reranker,
            workspaceStore: store);

        return client.GetWorkspaceManager()!;
    }

    private static async Task<Workspace> CreatePinnedWorkspaceAsync(WorkspaceManager manager, bool rerank)
    {
        var workspace = await manager.CreateWorkspaceAsync("Pinned", w => w.RerankEnabled = rerank);
        foreach (var id in PinnedIds)
        {
            await manager.AddExistingDocumentAsync(workspace.WorkspaceId, id, pinned: true);
        }

        return workspace;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }

    /// <summary>A host-side service written against the interface, as a consumer would.</summary>
    private sealed class WorkspaceTitleService(IWorkspaceManager workspaces)
    {
        public async Task<IReadOnlyList<string>> TitlesAsync() =>
            (await workspaces.ListWorkspacesAsync()).Select(w => w.Name).Order().ToList();
    }

    /// <summary>A hand-written test double: only what the service calls is implemented.</summary>
    private sealed class StubWorkspaceManager : IWorkspaceManager
    {
#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
        public event EventHandler<ContextTruncatedEventArgs>? ContextTruncated;
#pragma warning restore CS0067

        public Task<IReadOnlyList<Workspace>> ListWorkspacesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>([new Workspace { Name = "Support" }, new Workspace { Name = "Contracts" }]);

        public IWorkspaceStore GetStore() => throw new NotSupportedException();
        public Task<Workspace> CreateWorkspaceAsync(string name, Action<Workspace>? configure = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Workspace?> GetWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateWorkspaceAsync(Workspace workspace, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> IngestTextAsync(string workspaceId, string text, string documentName, Dictionary<string, object>? metadata = null, bool pinned = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> IngestFileAsync(string workspaceId, string filePath, bool pinned = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddExistingDocumentAsync(string workspaceId, string documentId, string? contentHash = null, bool pinned = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetPinnedAsync(string workspaceId, string documentId, bool pinned, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SearchResult>> SearchAsync(string workspaceId, string query, int? topK = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SearchResult>> SearchScopedAsync(string workspaceId, string query, WorkspaceTurnOverrides? overrides, int? topK = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RagResponse> AskAsync(string workspaceId, string question, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<RagStreamEvent> AskStreamWithSourcesAsync(string workspaceId, string question, IReadOnlyList<ChatMessage>? conversationHistory = null, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<RagStreamEvent> AskTurnStreamAsync(string workspaceId, string question, WorkspaceTurnOverrides? overrides, IReadOnlyList<ChatMessage>? conversationHistory = null, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SearchResult>> BuildContextAsync(string workspaceId, string question, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceContext> BuildContextWithDiagnosticsAsync(string workspaceId, string question, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A vector store that records every search it receives, by kind.</summary>
    private sealed class CountingVectorStore(IEnumerable<SearchResult> seed) : IVectorStore
    {
        private readonly List<SearchResult> results = seed.ToList();

        public List<string?> SingleDocumentSearches { get; } = [];

        public List<IReadOnlyCollection<string>> DocumentSetSearches { get; } = [];

        public string Name => "Counting";

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> UpsertAsync(TextChunk chunk, CancellationToken cancellationToken = default) => Task.FromResult(chunk.Id);

        public Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(chunks.Select(c => c.Id).ToList());

        public Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default)
        {
            if (documentFilter is not null)
            {
                SingleDocumentSearches.Add(documentFilter);
            }

            return Task.FromResult<IReadOnlyList<SearchResult>>(results
                .Where(r => documentFilter is null || r.Chunk.DocumentId == documentFilter)
                .Take(topK)
                .ToList());
        }

        public Task<IReadOnlyList<SearchResult>> SearchDocumentsAsync(float[] queryVector, int topK, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken = default)
        {
            DocumentSetSearches.Add(documentIds.ToList());
            return Task.FromResult<IReadOnlyList<SearchResult>>(results.Where(r => documentIds.Contains(r.Chunk.DocumentId)).Take(topK).ToList());
        }

        public Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Document>>([]);

        public Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new IngestionStats());

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>A reranker that keeps the order and records every call.</summary>
    private sealed class CountingReranker : IReranker
    {
        public int Calls => CandidateSets.Count;

        public List<IReadOnlyList<SearchResult>> CandidateSets { get; } = [];

        public string Name => "Counting";

        public Task<IReadOnlyList<SearchResult>> RerankAsync(string query, IReadOnlyList<SearchResult> results, int topN, CancellationToken cancellationToken = default)
        {
            CandidateSets.Add(results);
            return Task.FromResult<IReadOnlyList<SearchResult>>(results.Take(topN).ToList());
        }
    }
}
