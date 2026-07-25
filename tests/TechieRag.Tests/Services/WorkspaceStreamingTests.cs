using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Services;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// Tests for workspace-scoped streaming RAG (TR-RAG-003, BRD-109) and for pinned documents
/// surviving the streaming path (REQ-RAG-013, BRD-44). Drives the real
/// <see cref="WorkspaceManager"/> over a real temporary SQLite workspace store with in-memory
/// fakes for embedding, vector store, and LLM.
/// </summary>
public class WorkspaceStreamingTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"trwsstream-{Guid.NewGuid():N}.db");
    private readonly SqliteWorkspaceStore store;

    /// <summary>Creates a workspace store over a unique temp database file.</summary>
    public WorkspaceStreamingTests() => store = new SqliteWorkspaceStore($"Data Source={dbPath}");

    /// <summary>
    /// The workspace-scoped stream yields the sources first, then answer tokens, then a final
    /// completed event carrying the aggregated answer — the same contract as the global
    /// streaming API (REQ-RAG-024), so a streaming UI can render citations before the answer.
    /// </summary>
    [Fact]
    public async Task StreamsScopedSourcesThenTokensThenCompleted()
    {
        var results = new[] { TestData.Result("doc-a", "alpha passage", 0.9f) };
        var manager = CreateManager(results, out _, "Hel", "lo");
        var workspace = await manager.CreateWorkspaceAsync("Alpha");
        await manager.AddExistingDocumentAsync(workspace.WorkspaceId, "doc-a");

        var events = new List<RagStreamEvent>();
        await foreach (var evt in manager.AskStreamWithSourcesAsync(workspace.WorkspaceId, "what is up?"))
        {
            events.Add(evt);
        }

        Assert.Equal(RagStreamEventType.Sources, events[0].Type);
        Assert.Equal("doc-a", events[0].Sources![0].Chunk.DocumentId);
        Assert.Equal(2, events.Count(e => e.Type == RagStreamEventType.Token));
        Assert.Equal(RagStreamEventType.Completed, events[^1].Type);
        Assert.Equal("Hello", events[^1].Answer);

        // No Token or Completed event may precede the Sources event.
        Assert.Equal(0, events.FindIndex(e => e.Type == RagStreamEventType.Sources));
    }

    /// <summary>
    /// Streamed sources are restricted to the asking workspace's document set: a chunk that the
    /// vector store would happily return is excluded when it belongs to another workspace
    /// (REQ-RAG-007 isolation, carried into the streaming path).
    /// </summary>
    [Fact]
    public async Task StreamedSourcesExcludeOtherWorkspaceDocuments()
    {
        var results = new[]
        {
            TestData.Result("doc-a", "alpha passage", 0.9f),
            TestData.Result("doc-b", "beta passage", 0.8f)
        };
        var manager = CreateManager(results, out _, "ok");

        var alpha = await manager.CreateWorkspaceAsync("Alpha");
        var beta = await manager.CreateWorkspaceAsync("Beta");
        await manager.AddExistingDocumentAsync(alpha.WorkspaceId, "doc-a");
        await manager.AddExistingDocumentAsync(beta.WorkspaceId, "doc-b");

        var sources = await FirstSourcesAsync(manager, alpha.WorkspaceId, "anything");

        Assert.Single(sources);
        Assert.Equal("doc-a", sources[0].Chunk.DocumentId);
        Assert.DoesNotContain(sources, s => s.Chunk.DocumentId == "doc-b");
    }

    /// <summary>
    /// A pinned document is merged into the streamed context even when its score is below the
    /// workspace similarity threshold, and it appears in the prompt the LLM receives — this is
    /// the gap the old app-side composed streaming path had (REQ-RAG-013, TR-RAG-003).
    /// </summary>
    [Fact]
    public async Task PinnedDocumentEntersStreamedContextBelowThreshold()
    {
        var results = new[]
        {
            TestData.Result("doc-a", "alpha passage", 0.9f),
            TestData.Result("doc-pinned", "the pinned policy text", 0.05f)
        };
        var manager = CreateManager(results, out var llm, "ok");

        var workspace = await manager.CreateWorkspaceAsync("Alpha", w => w.SimilarityThreshold = 0.5f);
        await manager.AddExistingDocumentAsync(workspace.WorkspaceId, "doc-a");
        await manager.AddExistingDocumentAsync(workspace.WorkspaceId, "doc-pinned", pinned: true);

        IReadOnlyList<SearchResult> sources = [];
        await foreach (var evt in manager.AskStreamWithSourcesAsync(workspace.WorkspaceId, "unrelated question"))
        {
            if (evt.Type == RagStreamEventType.Sources) sources = evt.Sources!;
        }

        // The low-scoring pinned chunk is present despite the 0.5 threshold, and comes first.
        Assert.Equal("doc-pinned", sources[0].Chunk.DocumentId);
        Assert.Contains(sources, s => s.Chunk.DocumentId == "doc-a");

        var promptText = string.Join("\n", llm.LastMessages!.Select(m => m.Content));
        Assert.Contains("the pinned policy text", promptText);
    }

    /// <summary>
    /// The same pinned chunk is not emitted twice when it also passes the similarity threshold,
    /// so citation chips do not duplicate.
    /// </summary>
    [Fact]
    public async Task StreamedContextDeduplicatesPinnedAndRetrievedChunks()
    {
        var results = new[] { TestData.Result("doc-pinned", "shared passage", 0.9f) };
        var manager = CreateManager(results, out _, "ok");

        var workspace = await manager.CreateWorkspaceAsync("Alpha");
        await manager.AddExistingDocumentAsync(workspace.WorkspaceId, "doc-pinned", pinned: true);

        var sources = await FirstSourcesAsync(manager, workspace.WorkspaceId, "q");

        Assert.Single(sources);
    }

    /// <summary>
    /// The public context seam returns exactly what the streaming path uses, so a caller that
    /// builds its own generation loop can reproduce the pinned-aware workspace context.
    /// </summary>
    [Fact]
    public async Task BuildContextExposesPinnedAwareContext()
    {
        var results = new[] { TestData.Result("doc-pinned", "pinned text", 0.05f) };
        var manager = CreateManager(results, out _, "ok");

        var workspace = await manager.CreateWorkspaceAsync("Alpha", w => w.SimilarityThreshold = 0.9f);
        await manager.AddExistingDocumentAsync(workspace.WorkspaceId, "doc-pinned", pinned: true);

        var context = await manager.BuildContextAsync(workspace.WorkspaceId, "q");

        Assert.Single(context);
        Assert.Equal("doc-pinned", context[0].Chunk.DocumentId);
    }

    /// <summary>
    /// In query mode with nothing passing the threshold, the stream emits the honest
    /// "not covered" answer and never calls the LLM (REQ-RAG-015 on the streaming path).
    /// </summary>
    [Fact]
    public async Task QueryModeStreamsNotCoveredWithoutCallingLlm()
    {
        var results = new[] { TestData.Result("doc-a", "alpha passage", 0.1f) };
        var manager = CreateManager(results, out var llm, "should not be used");

        var workspace = await manager.CreateWorkspaceAsync("Alpha", w =>
        {
            w.SimilarityThreshold = 0.9f;
            w.ChatMode = WorkspaceChatMode.Query;
        });
        await manager.AddExistingDocumentAsync(workspace.WorkspaceId, "doc-a");

        var events = new List<RagStreamEvent>();
        await foreach (var evt in manager.AskStreamWithSourcesAsync(workspace.WorkspaceId, "q"))
        {
            events.Add(evt);
        }

        Assert.Equal(RagStreamEventType.Sources, events[0].Type);
        Assert.Empty(events[0].Sources!);
        Assert.Equal(RagStreamEventType.Completed, events[^1].Type);
        Assert.Contains("do not contain information", events[^1].Answer);
        Assert.Null(llm.LastMessages);
    }

    /// <summary>
    /// Prior conversation turns are carried into the streamed prompt, and the current question is
    /// appended once by the chat template rather than being duplicated.
    /// </summary>
    [Fact]
    public async Task StreamCarriesConversationHistoryWithoutDuplicatingQuestion()
    {
        var results = new[] { TestData.Result("doc-a", "alpha passage", 0.9f) };
        var manager = CreateManager(results, out var llm, "ok");

        var workspace = await manager.CreateWorkspaceAsync("Alpha");
        await manager.AddExistingDocumentAsync(workspace.WorkspaceId, "doc-a");

        var history = new List<ChatMessage> { ChatMessage.User("earlier turn"), ChatMessage.Assistant("earlier reply") };
        await foreach (var _ in manager.AskStreamWithSourcesAsync(workspace.WorkspaceId, "current question", history)) { }

        var contents = llm.LastMessages!.Select(m => m.Content).ToList();
        Assert.Contains(contents, c => c == "earlier turn");
        Assert.Equal("current question", contents[^1]);
        Assert.Equal(1, contents.Count(c => c == "current question"));
    }

    private static async Task<IReadOnlyList<SearchResult>> FirstSourcesAsync(
        WorkspaceManager manager,
        string workspaceId,
        string question)
    {
        await foreach (var evt in manager.AskStreamWithSourcesAsync(workspaceId, question))
        {
            if (evt.Type == RagStreamEventType.Sources) return evt.Sources!;
        }

        throw new InvalidOperationException("The stream produced no Sources event.");
    }

    private WorkspaceManager CreateManager(
        IReadOnlyList<SearchResult> results,
        out FakeStreamingLlmProvider llm,
        params string[] tokens)
    {
        llm = new FakeStreamingLlmProvider(tokens);
        var client = new TechieRagClient(
            new FakeVectorStore(results),
            new FakeEmbeddingProvider(),
            Array.Empty<IDocumentProcessor>(),
            new TechieRagConfig(),
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
