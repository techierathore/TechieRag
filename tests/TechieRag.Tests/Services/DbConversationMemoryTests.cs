using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// Tests for <see cref="DbConversationMemory"/> (REQ-RAG-027): the persistent
/// <c>IConversationMemory</c> implementation backed by a SQLite conversation store. Verifies
/// lazy thread creation, history round-trip across a fresh memory instance (restart survival),
/// and clear semantics.
/// </summary>
public class DbConversationMemoryTests : IDisposable
{
    private readonly string dbPath = Path.Combine(Path.GetTempPath(), $"trmem-{Guid.NewGuid():N}.db");
    private readonly SqliteConversationStore store;

    /// <summary>Creates a store over a unique temp database file.</summary>
    public DbConversationMemoryTests() => store = new SqliteConversationStore($"Data Source={dbPath}");

    /// <summary>Added messages are retrievable in order through the memory abstraction.</summary>
    [Fact]
    public async Task AddsAndRetrievesHistory()
    {
        var memory = new DbConversationMemory(store, userId: "u1");
        await memory.AddMessageAsync(ChatMessage.User("hello"));
        await memory.AddMessageAsync(ChatMessage.Assistant("hi there"));

        var history = await memory.GetHistoryAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal("hello", history[0].Content);
        Assert.Equal("hi there", history[1].Content);
    }

    /// <summary>
    /// Messages saved back to back come back in the order they were saved: the store never gives
    /// two messages the same time, so the random message id never decides the order. Guards the
    /// fix for the CI failure of 2026-10-30 (run 40914516648); the tie it fixed needs a busy machine
    /// or a clock step, so this test alone does not reproduce the old failure every time.
    /// </summary>
    [Fact]
    public async Task RapidMessagesKeepTheirOrder()
    {
        var memory = new DbConversationMemory(store, userId: "u1");
        for (var i = 0; i < 50; i++)
        {
            await memory.AddMessageAsync(ChatMessage.User($"m{i}"));
        }

        var history = await memory.GetHistoryAsync();

        Assert.Equal(Enumerable.Range(0, 50).Select(i => $"m{i}"), history.Select(m => m.Content));
    }

    /// <summary>
    /// History persists across memory instances: a new memory bound to the same thread id
    /// reads the messages written by the first (survives an application restart).
    /// </summary>
    [Fact]
    public async Task HistorySurvivesNewMemoryInstance()
    {
        var first = new DbConversationMemory(store, userId: "u1");
        await first.AddMessageAsync(ChatMessage.User("remember me"));
        var threadId = first.ConversationId;

        var second = new DbConversationMemory(store, userId: "u1", threadId: threadId);
        var history = await second.GetHistoryAsync();

        Assert.Single(history);
        Assert.Equal("remember me", history[0].Content);
    }

    /// <summary>Clearing removes the underlying thread and its history.</summary>
    [Fact]
    public async Task ClearRemovesHistory()
    {
        var memory = new DbConversationMemory(store, userId: "u1");
        await memory.AddMessageAsync(ChatMessage.User("temp"));

        await memory.ClearAsync();

        var history = await memory.GetHistoryAsync();
        Assert.Empty(history);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(dbPath)) File.Delete(dbPath);
    }
}
