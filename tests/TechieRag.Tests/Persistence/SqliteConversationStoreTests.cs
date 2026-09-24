using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Persistence;

/// <summary>
/// End-to-end tests for the DB-backed thread-aware conversation store (REQ-RAG-027) using a
/// real temporary SQLite database. Verifies thread lifecycle, per-user/workspace scoping,
/// message persistence with citations (sources round-trip), and token-aware history trimming.
/// </summary>
public class SqliteConversationStoreTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"trconv-{Guid.NewGuid():N}.db");
    private readonly SqliteConversationStore store;

    /// <summary>Creates a store over a unique temp database file.</summary>
    public SqliteConversationStoreTests() => store = new SqliteConversationStore($"Data Source={dbPath}");

    /// <summary>Creating a thread persists it and it can be read back by id.</summary>
    [Fact]
    public async Task CreatesAndRetrievesThread()
    {
        var thread = await store.CreateThreadAsync("user1", workspaceId: "ws1", title: "My Chat");

        var loaded = await store.GetThreadAsync(thread.ThreadId);
        Assert.NotNull(loaded);
        Assert.Equal("user1", loaded!.UserId);
        Assert.Equal("ws1", loaded.WorkspaceId);
        Assert.Equal("My Chat", loaded.Title);
    }

    /// <summary>Thread listing is scoped by user and optionally by workspace.</summary>
    [Fact]
    public async Task ListsThreadsScopedByUserAndWorkspace()
    {
        await store.CreateThreadAsync("userA", "wsX");
        await store.CreateThreadAsync("userA", "wsY");
        await store.CreateThreadAsync("userB", "wsX");

        var allForA = await store.ListThreadsAsync("userA");
        var wsXForA = await store.ListThreadsAsync("userA", "wsX");

        Assert.Equal(2, allForA.Count);
        Assert.Single(wsXForA);
        Assert.Equal("wsX", wsXForA[0].WorkspaceId);
    }

    /// <summary>Messages persist with their citations and round-trip back with sources intact.</summary>
    [Fact]
    public async Task PersistsMessagesWithCitations()
    {
        var thread = await store.CreateThreadAsync("user1");
        var sources = new List<SearchResult>
        {
            TestData.Result("doc-42", "cited passage", 0.87f)
        };

        await store.AddMessageAsync(thread.ThreadId, ChatMessage.User("What does doc 42 say?"));
        await store.AddMessageAsync(thread.ThreadId, ChatMessage.Assistant("It says hello."), sources);

        var messages = await store.GetMessagesAsync(thread.ThreadId);

        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Null(messages[0].Sources);

        Assert.Equal("assistant", messages[1].Role);
        Assert.NotNull(messages[1].Sources);
        Assert.Single(messages[1].Sources!);
        Assert.Equal("doc-42", messages[1].Sources![0].Chunk.DocumentId);
        Assert.Equal("cited passage", messages[1].Sources![0].Chunk.Text);
        Assert.Equal(0.87f, messages[1].Sources![0].Score);
    }

    /// <summary>Deleting a thread removes it and all its messages.</summary>
    [Fact]
    public async Task DeletesThreadAndMessages()
    {
        var thread = await store.CreateThreadAsync("user1");
        await store.AddMessageAsync(thread.ThreadId, ChatMessage.User("hi"));

        await store.DeleteThreadAsync(thread.ThreadId);

        Assert.Null(await store.GetThreadAsync(thread.ThreadId));
        Assert.Empty(await store.GetMessagesAsync(thread.ThreadId));
    }

    /// <summary>Trimmed history keeps the system message plus the most recent turns within budget.</summary>
    [Fact(DisplayName = "REQ-RAG-091 TrimsHistoryToTokenBudget")]
    public async Task TrimsHistoryToTokenBudget()
    {
        var thread = await store.CreateThreadAsync("user1");
        await store.AddMessageAsync(thread.ThreadId, ChatMessage.System("SYS"));
        await store.AddMessageAsync(thread.ThreadId, ChatMessage.User("oldest"));
        await store.AddMessageAsync(thread.ThreadId, ChatMessage.Assistant("middle"));
        await store.AddMessageAsync(thread.ThreadId, ChatMessage.User("newest"));

        // Each word counts as 1 token; budget of 3 => system + 2 most recent non-system.
        var trimmed = await store.GetTrimmedHistoryAsync(thread.ThreadId, maxTokens: 3, tokenCounter: _ => 1);

        Assert.Equal("system", trimmed[0].Role);
        Assert.Equal("newest", trimmed[^1].Content);
        Assert.DoesNotContain(trimmed, m => m.Content == "oldest");
    }

    /// <summary>
    /// REQ-RAG-090 / BRD-137: an assistant message stored with sources and its structured content
    /// (<c>ContentJson</c>) reloads from a second store over the same database with its role, text,
    /// structured content and every source field intact.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-090 MessageReloadsWithRoleContentAndSources")]
    public async Task MessageReloadsWithRoleContentAndSources()
    {
        const string structured = """{"code":"answer.cited","args":["doc-7"]}""";
        var thread = await store.CreateThreadAsync("user1", workspaceId: "ws1");
        var sources = new List<SearchResult>
        {
            TestData.Result("doc-7", "the cited clause", 0.91f),
            TestData.Result("doc-8", "a second clause", 0.52f)
        };
        await store.AddMessageAsync(thread.ThreadId, ChatMessage.User("Which clause applies?"));
        await store.AddMessageAsync(thread.ThreadId, ChatMessage.Assistant("Clause seven."), sources, contentJson: structured);

        var reopened = new SqliteConversationStore($"Data Source={dbPath}");
        var messages = await reopened.GetMessagesAsync(thread.ThreadId);

        Assert.Equal(["user", "assistant"], messages.Select(m => m.Role));
        Assert.Equal("Which clause applies?", messages[0].Content);
        var answer = messages[1];
        Assert.Equal("Clause seven.", answer.Content);
        Assert.Equal(structured, answer.ContentJson);
        Assert.Equal(["doc-7", "doc-8"], answer.Sources!.Select(s => s.Chunk.DocumentId));
        Assert.Equal(["the cited clause", "a second clause"], answer.Sources!.Select(s => s.Chunk.Text));
        Assert.Equal([0.91f, 0.52f], answer.Sources!.Select(s => s.Score));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }
}
