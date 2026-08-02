using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Services;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// Tests that context truncation by <c>PromptConfig.MaxContextChunks</c> is deliberate and
/// observable (REQ-RAG-048, TR-RAG-006). Before this, the merged pinned + retrieved context was
/// trimmed silently inside the prompt template, so more than five pinned documents evicted every
/// retrieved result with no signal to the caller.
/// </summary>
public class WorkspaceContextTruncationTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"trwstrunc-{Guid.NewGuid():N}.db");
    private readonly SqliteWorkspaceStore store;

    /// <summary>Creates a workspace store over a unique temp database file.</summary>
    public WorkspaceContextTruncationTests() => store = new SqliteWorkspaceStore($"Data Source={dbPath}");

    /// <summary>
    /// A context that fits the budget reports no truncation, so callers can trust the flag as a
    /// negative signal and not just as an alarm.
    /// </summary>
    [Fact]
    public async Task ContextUnderTheLimitReportsNoTruncation()
    {
        var manager = CreateManager(DocumentSet(4));
        var workspace = await manager.CreateWorkspaceAsync("Small");
        await AddDocumentsAsync(manager, workspace.WorkspaceId, pinned: 1, free: 3);

        var context = await manager.BuildContextWithDiagnosticsAsync(workspace.WorkspaceId, "q");

        Assert.False(context.WasTruncated);
        Assert.Equal(0, context.EvictedCount);
        Assert.Equal(4, context.Results.Count);
        Assert.Equal(5, context.MaxContextChunks);
    }

    /// <summary>
    /// Exactly at the budget is still not truncation — the boundary is inclusive.
    /// </summary>
    [Fact]
    public async Task ContextExactlyAtTheLimitReportsNoTruncation()
    {
        var manager = CreateManager(DocumentSet(5));
        var workspace = await manager.CreateWorkspaceAsync("Exact");
        await AddDocumentsAsync(manager, workspace.WorkspaceId, pinned: 2, free: 3);

        var context = await manager.BuildContextWithDiagnosticsAsync(workspace.WorkspaceId, "q");

        Assert.False(context.WasTruncated);
        Assert.Equal(5, context.Results.Count);
    }

    /// <summary>
    /// Over the budget, retrieved chunks are evicted from the tail and pinned chunks keep their
    /// slots — the eviction order is deliberate, not a by-product of sort order.
    /// </summary>
    [Fact]
    public async Task RetrievedChunksAreEvictedBeforePinnedChunks()
    {
        var manager = CreateManager(DocumentSet(7));
        var workspace = await manager.CreateWorkspaceAsync("Crowded");
        await AddDocumentsAsync(manager, workspace.WorkspaceId, pinned: 2, free: 5);

        var context = await manager.BuildContextWithDiagnosticsAsync(workspace.WorkspaceId, "q");

        Assert.True(context.WasTruncated);
        Assert.Equal(2, context.PinnedIncluded);
        Assert.Equal(0, context.PinnedEvicted);
        Assert.Equal(3, context.RetrievedIncluded);
        Assert.Equal(2, context.RetrievedEvicted);
        Assert.Equal(5, context.Results.Count);
    }

    /// <summary>
    /// The problem statement's case: more than five pinned documents evicts every retrieved
    /// result. That still happens — pinned documents win by design — but it is now reported
    /// instead of being silent, and the pinned overflow is reported too.
    /// </summary>
    [Fact]
    public async Task MoreThanFivePinnedDocumentsEvictsRetrievedResultsWithASignal()
    {
        var manager = CreateManager(DocumentSet(7));
        var workspace = await manager.CreateWorkspaceAsync("HeavilyPinned");
        await AddDocumentsAsync(manager, workspace.WorkspaceId, pinned: 6, free: 1);

        var context = await manager.BuildContextWithDiagnosticsAsync(workspace.WorkspaceId, "q");

        Assert.True(context.WasTruncated);
        Assert.Equal(5, context.PinnedIncluded);
        Assert.Equal(1, context.PinnedEvicted);
        Assert.Equal(0, context.RetrievedIncluded);
        Assert.Equal(1, context.RetrievedEvicted);
        Assert.Equal(2, context.EvictedCount);
    }

    /// <summary>
    /// Truncation raises <see cref="WorkspaceManager.ContextTruncated"/> with the workspace, the
    /// question, and the diagnostics, so a UI can warn the user without polling every build.
    /// </summary>
    [Fact]
    public async Task TruncationRaisesTheContextTruncatedEvent()
    {
        var manager = CreateManager(DocumentSet(7));
        var workspace = await manager.CreateWorkspaceAsync("Crowded");
        await AddDocumentsAsync(manager, workspace.WorkspaceId, pinned: 2, free: 5);

        ContextTruncatedEventArgs? captured = null;
        manager.ContextTruncated += (_, args) => captured = args;

        await manager.BuildContextAsync(workspace.WorkspaceId, "how do refunds work?");

        Assert.NotNull(captured);
        Assert.Equal(workspace.WorkspaceId, captured!.WorkspaceId);
        Assert.Equal("how do refunds work?", captured.Question);
        Assert.Equal(2, captured.Context.RetrievedEvicted);
    }

    /// <summary>
    /// No event is raised when nothing was dropped, so the signal stays meaningful.
    /// </summary>
    [Fact]
    public async Task NoEventIsRaisedWhenTheContextFits()
    {
        var manager = CreateManager(DocumentSet(3));
        var workspace = await manager.CreateWorkspaceAsync("Small");
        await AddDocumentsAsync(manager, workspace.WorkspaceId, pinned: 1, free: 2);

        var raised = false;
        manager.ContextTruncated += (_, _) => raised = true;

        await manager.BuildContextAsync(workspace.WorkspaceId, "q");

        Assert.False(raised);
    }

    /// <summary>
    /// The streamed Sources event carries the same trimmed context that reaches the prompt, so a
    /// citation list can no longer advertise chunks the model never saw.
    /// </summary>
    [Fact]
    public async Task StreamedSourcesMatchTheTruncatedContext()
    {
        var manager = CreateManager(DocumentSet(7), out var llm);
        var workspace = await manager.CreateWorkspaceAsync("Crowded");
        await AddDocumentsAsync(manager, workspace.WorkspaceId, pinned: 2, free: 5);

        IReadOnlyList<SearchResult> sources = [];
        await foreach (var evt in manager.AskStreamWithSourcesAsync(workspace.WorkspaceId, "q"))
        {
            if (evt.Type == RagStreamEventType.Sources) sources = evt.Sources!;
        }

        var promptText = string.Join("\n", llm.LastMessages!.Select(m => m.Content));
        Assert.Equal(5, sources.Count);
        Assert.All(sources, s => Assert.Contains(s.Chunk.Text, promptText));
    }

    /// <summary>
    /// A non-positive budget disables trimming altogether, for callers that manage the context
    /// window themselves.
    /// </summary>
    [Fact]
    public async Task NonPositiveBudgetDisablesTruncation()
    {
        var manager = CreateManager(DocumentSet(7), out _, maxContextChunks: 0);
        var workspace = await manager.CreateWorkspaceAsync("Unlimited");
        await AddDocumentsAsync(manager, workspace.WorkspaceId, pinned: 2, free: 5);

        var context = await manager.BuildContextWithDiagnosticsAsync(workspace.WorkspaceId, "q");

        Assert.False(context.WasTruncated);
        Assert.Equal(7, context.Results.Count);
    }

    private static IReadOnlyList<SearchResult> DocumentSet(int count) =>
        Enumerable.Range(0, count)
            .Select(i => TestData.Result($"doc-{i}", $"passage {i}", 0.9f - (i * 0.01f)))
            .ToList();

    private static async Task AddDocumentsAsync(WorkspaceManager manager, string workspaceId, int pinned, int free)
    {
        // Free (unpinned) documents are added first so they dominate the retrieved ranking;
        // pinned documents follow, exercising the pinned-vs-retrieved eviction order.
        for (var i = 0; i < free; i++)
        {
            await manager.AddExistingDocumentAsync(workspaceId, $"doc-{i}");
        }

        for (var i = free; i < free + pinned; i++)
        {
            await manager.AddExistingDocumentAsync(workspaceId, $"doc-{i}", pinned: true);
        }
    }

    private WorkspaceManager CreateManager(IReadOnlyList<SearchResult> results) =>
        CreateManager(results, out _);

    private WorkspaceManager CreateManager(
        IReadOnlyList<SearchResult> results,
        out FakeStreamingLlmProvider llm,
        int maxContextChunks = 5)
    {
        var config = new TechieRagConfig();
        config.Prompt.MaxContextChunks = maxContextChunks;

        llm = new FakeStreamingLlmProvider("ok");
        var client = new TechieRagClient(
            new FakeVectorStore(results),
            new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            config,
            NullLogger<TechieRagClient>.Instance,
            llmProvider: llm,
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
