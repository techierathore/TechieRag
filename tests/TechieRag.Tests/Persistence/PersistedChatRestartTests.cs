using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Persistence;

/// <summary>
/// REQ-RAG-089 / BRD-136: a developer who configures <c>WithPersistence(StoreProvider.Sqlite, ...)</c>
/// and chats keeps the thread across an application restart.
/// </summary>
/// <remarks>
/// The "restart" is a second instance built from the same configuration over the same database file:
/// nothing is shared in memory between the two instances, so whatever the second one reads came from
/// SQLite. The LLM is a fake; the conversation store and the vector store are real SQLite files.
/// </remarks>
public sealed class PersistedChatRestartTests : IDisposable
{
    private readonly string workFolder = Path.Combine(Path.GetTempPath(), $"trrestart-{Guid.NewGuid():N}");

    /// <summary>Creates the temporary folder holding both databases.</summary>
    public PersistedChatRestartTests() => Directory.CreateDirectory(workFolder);

    /// <summary>
    /// A chat turn through <c>ChatWithRagAsync</c> on an instance built with SQLite persistence is
    /// written to a thread; a fresh instance built with the same settings lists that thread for the
    /// same user and reloads the user question and the assistant answer in order.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-089 ChatThreadSurvivesARestart")]
    public async Task ChatThreadSurvivesARestart()
    {
        var first = Build();
        await first.InitializeAsync();
        await first.ChatWithRagAsync("When does the contract renew?");

        var restarted = Build();
        await restarted.InitializeAsync();
        var store = restarted.GetConversationStore();

        Assert.NotNull(store);
        var thread = Assert.Single(await store!.ListThreadsAsync("owner"));
        var messages = await store.GetMessagesAsync(thread.ThreadId);
        Assert.Equal(["user", "assistant"], messages.Select(m => m.Role));
        Assert.Equal("When does the contract renew?", messages[0].Content);
        Assert.Equal("Every March.", messages[1].Content);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(workFolder)) Directory.Delete(workFolder, recursive: true);
    }

    private ITechieRag Build() => new TechieRagBuilder()
        .UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider())
        .UseSqliteVec(Path.Combine(workFolder, "vectors.db"))
        .UseCustomLlmProvider(() => new FakeStreamingLlmProvider("Every ", "March."))
        .WithConversationMemory()
        .WithPersistence(StoreProvider.Sqlite, $"Data Source={Path.Combine(workFolder, "conversations.db")}", "owner")
        .Build();
}
