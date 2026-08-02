using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Services;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// Tests that <see cref="Workspace.RerankEnabled"/> actually drives retrieval ordering
/// (REQ-RAG-047, TR-RAG-005) rather than merely round-tripping through the store, which is the
/// gap that demoted REQ-RAG-014. Every test here fails if <see cref="WorkspaceManager"/> stops
/// passing the workspace flag down as <see cref="SearchOptions.Rerank"/>.
/// </summary>
public class WorkspaceRerankTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"trwsrerank-{Guid.NewGuid():N}.db");
    private readonly SqliteWorkspaceStore store;

    /// <summary>Creates a workspace store over a unique temp database file.</summary>
    public WorkspaceRerankTests() => store = new SqliteWorkspaceStore($"Data Source={dbPath}");

    /// <summary>
    /// Turning rerank on for a workspace changes that workspace's retrieval ordering even when the
    /// library-wide rerank flag is off. With the flag ignored the results would come back in raw
    /// vector-similarity order and this assertion fails.
    /// </summary>
    [Fact]
    public async Task RerankEnabledWorkspaceChangesRetrievalOrdering()
    {
        var manager = CreateManager(globalRerank: false);
        var workspace = await CreateWorkspaceWithDocumentsAsync(manager, "Reranked", rerank: true);

        var results = await manager.SearchAsync(workspace.WorkspaceId, "q");

        Assert.Equal(["doc-c", "doc-b", "doc-a"], results.Select(r => r.Chunk.DocumentId));
    }

    /// <summary>
    /// Turning rerank off for a workspace suppresses reranking even when the library-wide flag is
    /// on, so the workspace toggle is authoritative in both directions.
    /// </summary>
    [Fact]
    public async Task RerankDisabledWorkspaceSuppressesGlobalRerank()
    {
        var manager = CreateManager(globalRerank: true);
        var workspace = await CreateWorkspaceWithDocumentsAsync(manager, "Plain", rerank: false);

        var results = await manager.SearchAsync(workspace.WorkspaceId, "q");

        Assert.Equal(["doc-a", "doc-b", "doc-c"], results.Select(r => r.Chunk.DocumentId));
    }

    /// <summary>
    /// Two workspaces over the same documents with opposite rerank settings get different
    /// orderings in the same process, and flipping one leaves the other untouched — the isolation
    /// half of the REQ-RAG-047 acceptance.
    /// </summary>
    [Fact]
    public async Task RerankSettingIsIsolatedBetweenWorkspaces()
    {
        var manager = CreateManager(globalRerank: false);
        var reranked = await CreateWorkspaceWithDocumentsAsync(manager, "Reranked", rerank: true);
        var plain = await CreateWorkspaceWithDocumentsAsync(manager, "Plain", rerank: false);

        var rerankedResults = await manager.SearchAsync(reranked.WorkspaceId, "q");
        var plainResults = await manager.SearchAsync(plain.WorkspaceId, "q");

        Assert.Equal(["doc-c", "doc-b", "doc-a"], rerankedResults.Select(r => r.Chunk.DocumentId));
        Assert.Equal(["doc-a", "doc-b", "doc-c"], plainResults.Select(r => r.Chunk.DocumentId));
    }

    /// <summary>
    /// Flipping the toggle on a persisted workspace changes the next search's ordering, which is
    /// what the workspace settings screen does — persistence alone is not enough.
    /// </summary>
    [Fact]
    public async Task FlippingWorkspaceToggleChangesTheNextSearchOrdering()
    {
        var manager = CreateManager(globalRerank: false);
        var workspace = await CreateWorkspaceWithDocumentsAsync(manager, "Toggled", rerank: false);

        var before = await manager.SearchAsync(workspace.WorkspaceId, "q");

        workspace.RerankEnabled = true;
        await manager.UpdateWorkspaceAsync(workspace);
        var after = await manager.SearchAsync(workspace.WorkspaceId, "q");

        Assert.Equal("doc-a", before[0].Chunk.DocumentId);
        Assert.Equal("doc-c", after[0].Chunk.DocumentId);
    }

    /// <summary>
    /// The workspace toggle also reaches the streamed context, so citation chips reflect the
    /// reranked ordering on the AskStreamWithSourcesAsync seam.
    /// </summary>
    [Fact]
    public async Task RerankEnabledWorkspaceReordersStreamedSources()
    {
        var manager = CreateManager(globalRerank: false);
        var workspace = await CreateWorkspaceWithDocumentsAsync(manager, "Reranked", rerank: true);

        var context = await manager.BuildContextAsync(workspace.WorkspaceId, "q");

        Assert.Equal(["doc-c", "doc-b", "doc-a"], context.Select(r => r.Chunk.DocumentId));
    }

    private static async Task<Workspace> CreateWorkspaceWithDocumentsAsync(
        WorkspaceManager manager,
        string name,
        bool rerank)
    {
        var workspace = await manager.CreateWorkspaceAsync(name, w => w.RerankEnabled = rerank);
        foreach (var documentId in new[] { "doc-a", "doc-b", "doc-c" })
        {
            await manager.AddExistingDocumentAsync(workspace.WorkspaceId, documentId);
        }

        return workspace;
    }

    private WorkspaceManager CreateManager(bool globalRerank)
    {
        var config = new TechieRagConfig();
        config.Rerank.Enabled = globalRerank;

        var results = new[]
        {
            TestData.Result("doc-a", "alpha", 0.9f),
            TestData.Result("doc-b", "beta", 0.8f),
            TestData.Result("doc-c", "gamma", 0.7f)
        };

        var client = new TechieRagClient(
            new FakeVectorStore(results),
            new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            config,
            NullLogger<TechieRagClient>.Instance,
            reranker: new ReversingReranker(),
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
