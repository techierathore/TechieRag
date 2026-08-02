using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Services;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// Verification tests for the two halves of REQ-RAG-014 that no existing test asserted — the
/// per-workspace <see cref="Workspace.TopK"/> and <see cref="Workspace.SimilarityThreshold"/>
/// actually shaping retrieval rather than merely round-tripping through the store — and for
/// REQ-RAG-012's "embed once, reuse across workspaces" content-hash deduplication.
/// The rerank third of REQ-RAG-014 is covered by <c>WorkspaceRerankTests</c> (REQ-RAG-047).
/// </summary>
public sealed class WorkspaceRetrievalSettingsTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"trwsset-{Guid.NewGuid():N}.db");
    private readonly SqliteWorkspaceStore store;

    /// <summary>Creates a workspace store over a unique temp database file.</summary>
    public WorkspaceRetrievalSettingsTests() => store = new SqliteWorkspaceStore($"Data Source={dbPath}");

    /// <summary>
    /// A workspace whose <c>TopK</c> is set returns exactly that many results even though the
    /// vector store could supply more. With the setting ignored the caller's default of five
    /// would win and this assertion fails.
    /// </summary>
    [Fact]
    public async Task WorkspaceTopKCapsTheNumberOfResults()
    {
        var manager = CreateManager();
        var workspace = await CreateWorkspaceAsync(manager, "Capped", w => w.TopK = 2);

        var results = await manager.SearchAsync(workspace.WorkspaceId, "q");

        Assert.Equal(["doc-a", "doc-b"], results.Select(r => r.Chunk.DocumentId));
    }

    /// <summary>
    /// An explicit per-call topK still wins over the workspace setting, so the workspace value is
    /// a default rather than a ceiling the caller cannot move.
    /// </summary>
    [Fact]
    public async Task ExplicitTopKOverridesTheWorkspaceSetting()
    {
        var manager = CreateManager();
        var workspace = await CreateWorkspaceAsync(manager, "Capped", w => w.TopK = 1);

        var results = await manager.SearchAsync(workspace.WorkspaceId, "q", topK: 3);

        Assert.Equal(3, results.Count);
    }

    /// <summary>
    /// A workspace similarity threshold drops results that score below it, so weak matches never
    /// reach the prompt.
    /// </summary>
    [Fact]
    public async Task WorkspaceSimilarityThresholdDropsWeakMatches()
    {
        var manager = CreateManager();
        var workspace = await CreateWorkspaceAsync(manager, "Strict", w => w.SimilarityThreshold = 0.85f);

        var results = await manager.SearchAsync(workspace.WorkspaceId, "q");

        Assert.Equal(["doc-a"], results.Select(r => r.Chunk.DocumentId));
    }

    /// <summary>
    /// Two workspaces over the same documents with different retrieval settings get different
    /// result sets in the same process — the settings are per workspace, not global.
    /// </summary>
    [Fact]
    public async Task RetrievalSettingsAreIsolatedBetweenWorkspaces()
    {
        var manager = CreateManager();
        var strict = await CreateWorkspaceAsync(manager, "Strict", w => w.SimilarityThreshold = 0.85f);
        var open = await CreateWorkspaceAsync(manager, "Open", _ => { });

        var strictResults = await manager.SearchAsync(strict.WorkspaceId, "q");
        var openResults = await manager.SearchAsync(open.WorkspaceId, "q");

        Assert.Single(strictResults);
        Assert.Equal(3, openResults.Count);
    }

    /// <summary>
    /// REQ-RAG-012: ingesting identical content into a second workspace reuses the existing
    /// document instead of embedding it again. The counting embedding provider fails this test if
    /// the content hash lookup is skipped.
    /// </summary>
    [Fact]
    public async Task IdenticalContentIsEmbeddedOnceAndReusedAcrossWorkspaces()
    {
        var embeddings = new CountingEmbeddingProvider();
        var manager = CreateManager(embeddings);
        var first = await manager.CreateWorkspaceAsync("First");
        var second = await manager.CreateWorkspaceAsync("Second");

        var firstId = await manager.IngestTextAsync(first.WorkspaceId, "shared body text", "shared.txt");
        var secondId = await manager.IngestTextAsync(second.WorkspaceId, "shared body text", "shared.txt");

        Assert.Equal(firstId, secondId);
        Assert.Equal(1, embeddings.BatchCalls);
        Assert.Single(await manager.GetStore().ListDocumentsAsync(first.WorkspaceId));
        Assert.Single(await manager.GetStore().ListDocumentsAsync(second.WorkspaceId));
    }

    /// <summary>
    /// Different content is not deduplicated: a second, distinct document is embedded on its own
    /// and gets its own identifier.
    /// </summary>
    [Fact]
    public async Task DifferentContentIsEmbeddedSeparately()
    {
        var embeddings = new CountingEmbeddingProvider();
        var manager = CreateManager(embeddings);
        var workspace = await manager.CreateWorkspaceAsync("Only");

        var firstId = await manager.IngestTextAsync(workspace.WorkspaceId, "first body", "one.txt");
        var secondId = await manager.IngestTextAsync(workspace.WorkspaceId, "second body", "two.txt");

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, embeddings.BatchCalls);
    }

    private static async Task<Workspace> CreateWorkspaceAsync(
        WorkspaceManager manager,
        string name,
        Action<Workspace> configure)
    {
        var workspace = await manager.CreateWorkspaceAsync(name, configure);
        foreach (var documentId in new[] { "doc-a", "doc-b", "doc-c" })
        {
            await manager.AddExistingDocumentAsync(workspace.WorkspaceId, documentId);
        }

        return workspace;
    }

    private WorkspaceManager CreateManager(IEmbeddingProvider? embeddingProvider = null)
    {
        var results = new[]
        {
            TestData.Result("doc-a", "alpha", 0.9f),
            TestData.Result("doc-b", "beta", 0.8f),
            TestData.Result("doc-c", "gamma", 0.7f)
        };

        var client = new TechieRagClient(
            new FakeVectorStore(results),
            embeddingProvider ?? new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            new TechieRagConfig(),
            NullLogger<TechieRagClient>.Instance,
            workspaceStore: store);

        return client.GetWorkspaceManager()!;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Embedding provider double that counts how often it was asked to embed, so deduplication can
/// be asserted on the thing that actually costs money rather than on an identifier comparison.
/// </summary>
internal sealed class CountingEmbeddingProvider : IEmbeddingProvider
{
    /// <summary>Gets how many batch embedding calls have been made.</summary>
    public int BatchCalls { get; private set; }

    /// <inheritdoc/>
    public string Name => "Counting";

    /// <inheritdoc/>
    public string ModelName => "counting-embed";

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
    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        BatchCalls++;
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new float[] { 0.1f, 0.2f, 0.3f }).ToList());
    }
}
