using System.Text.RegularExpressions;
using TechieDesk.Services.Scheduling;
using TechieRag.Models;
using TechieRag.Persistence;
using Xunit;

namespace TechieDesk.Tests.Chat;

/// <summary>
/// The chat transcript stores what the PRODUCT says as a code, not as a finished sentence
/// (REQ-UI-059 / BRD-91).
/// </summary>
/// <remarks>
/// <para><b>The defect, as it was actually found.</b> Driving <c>/workspace/{slug}</c> in Hindi, the
/// primary screen showed two English sentences amid complete Devanagari. The Hindi translation
/// existed and was correct — the string had been localized at WRITE time and frozen into
/// <c>TrMessage</c>. No geometry check could see it; it came from reading a screenshot.</para>
/// <para><b>Why it is worse than the row it was found under.</b> <c>REQ-UI-056</c> fixed the same
/// class for <c>ScheduleRun</c>, where the offending rows were 22 disposable fixtures. Here they are
/// REAL CHAT HISTORY, so the legacy branch is not a migration step to be removed later — deleting it
/// would blank out a user's conversations, which is the one outcome the policy forbids.</para>
/// </remarks>
public sealed class ChatTranscriptLocalizationTests : IDisposable
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(), $"trchat-{Guid.NewGuid():N}.db");

    /// <summary>A product-authored message round-trips as a CODE and renders per reader.</summary>
    /// <remarks>
    /// The two assertions that matter together: the same stored row renders differently for two
    /// readers. If the sentence had been frozen at write time this is impossible by construction.
    /// </remarks>
    [Fact]
    public async Task AProductAuthoredMessageRendersInEachReadersLanguage()
    {
        var store = await StoreAsync();
        var thread = await store.CreateThreadAsync("user-1");

        var message = JobMessage.Of("ChatNoProviderConfiguredMessage");
        await store.AddMessageAsync(
            thread.ThreadId,
            ChatMessage.Assistant(message.ToInvariantString()),
            contentJson: message.ToStorage());

        var stored = Assert.Single(await store.GetMessagesAsync(thread.ThreadId));

        Assert.Equal(
            "[ChatNoProviderConfiguredMessage]",
            JobMessage.Render(stored.Content, stored.ContentJson, Marker));

        Assert.Equal(
            "किसी भी भाषा में",
            JobMessage.Render(stored.Content, stored.ContentJson, (_, _) => "किसी भी भाषा में"));
    }

    /// <summary>
    /// A row written before codes existed prints its stored text verbatim — permanently.
    /// </summary>
    /// <remarks>
    /// This branch is the whole reason the fix is safe to ship against real history. It is not a
    /// transitional path and must never be deleted.
    /// </remarks>
    [Fact]
    public async Task ALegacyRowWithNoCodeRendersItsStoredTextVerbatim()
    {
        var store = await StoreAsync();
        var thread = await store.CreateThreadAsync("user-1");

        // Exactly what the old code wrote: a finished sentence, no companion JSON.
        await store.AddMessageAsync(
            thread.ThreadId,
            ChatMessage.Assistant("No LLM provider is configured. Configure one in LLM Settings to chat."));

        var stored = Assert.Single(await store.GetMessagesAsync(thread.ThreadId));

        Assert.Null(stored.ContentJson);
        Assert.Equal(
            "No LLM provider is configured. Configure one in LLM Settings to chat.",
            JobMessage.Render(stored.Content, stored.ContentJson, Marker));
    }

    /// <summary>
    /// The English stays in <c>Content</c>, because that is what the MODEL reads back as history.
    /// </summary>
    /// <remarks>
    /// Clause 3's invariant, at the storage layer: translating the column the model is replayed from
    /// would change what the model is told depending on who is looking at the screen.
    /// </remarks>
    [Fact]
    public async Task TheModelFacingColumnStaysInvariantEnglish()
    {
        var store = await StoreAsync();
        var thread = await store.CreateThreadAsync("user-1");

        var message = JobMessage.Of("ChatNoProviderConfiguredMessage");
        await store.AddMessageAsync(
            thread.ThreadId,
            ChatMessage.Assistant(message.ToInvariantString()),
            contentJson: message.ToStorage());

        var stored = Assert.Single(await store.GetMessagesAsync(thread.ThreadId));

        Assert.False(string.IsNullOrWhiteSpace(stored.Content));
        Assert.DoesNotContain('ऀ', stored.Content!);  // no Devanagari in the model's history
    }

    /// <summary>
    /// A database created before the column existed gains it on open, without losing its rows.
    /// </summary>
    /// <remarks>
    /// <c>CREATE TABLE IF NOT EXISTS</c> does nothing to an existing table, so without the additive
    /// <c>ALTER</c> every install that had ever run the app would silently drop each coded message.
    /// An existing install is the only case this requirement is really about.
    /// </remarks>
    [Fact]
    public async Task AnOlderDatabaseIsMigratedWithoutLosingHistory()
    {
        await using (var legacy = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            await legacy.OpenAsync();
            await using var command = legacy.CreateCommand();
            command.CommandText = """
                CREATE TABLE TrThread (ThreadId TEXT PRIMARY KEY, UserId TEXT NOT NULL, WorkspaceId TEXT,
                    Title TEXT NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
                CREATE TABLE TrMessage (MessageId TEXT PRIMARY KEY, ThreadId TEXT NOT NULL, Role TEXT NOT NULL,
                    Content TEXT, SourcesJson TEXT, CreatedAt TEXT NOT NULL);
                INSERT INTO TrThread VALUES ('t1','user-1',NULL,'Old chat','2026-01-01T00:00:00.0000000Z','2026-01-01T00:00:00.0000000Z');
                INSERT INTO TrMessage VALUES ('m1','t1','assistant','a sentence from before the upgrade',NULL,'2026-01-01T00:00:00.0000000Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = await StoreAsync();
        var stored = Assert.Single(await store.GetMessagesAsync("t1"));

        Assert.Equal("a sentence from before the upgrade", stored.Content);
        Assert.Null(stored.ContentJson);

        // And the migrated database now accepts a coded row.
        var message = JobMessage.Of("ChatNoProviderConfiguredMessage");
        await store.AddMessageAsync(
            "t1", ChatMessage.Assistant(message.ToInvariantString()), contentJson: message.ToStorage());

        Assert.Equal(2, (await store.GetMessagesAsync("t1")).Count);
    }

    /// <summary>
    /// No product-authored transcript write persists prose — asserted over the source itself.
    /// </summary>
    /// <remarks>
    /// <para><b>Clause 4, and the only assertion that stops this coming back.</b> The behavioural
    /// tests above prove the mechanism works; they cannot prove a future edit did not add a seventh
    /// <c>AddMessageAsync</c> that localizes at write time again. This reads
    /// <c>WorkspaceChat.razor</c> and fails when an assistant message is persisted from a
    /// <c>Localizer[...]</c> expression.</para>
    /// <para>The USER's own words are exempt — they are not ours to code — and so is the streamed
    /// model answer, which is the model's text and equally untranslatable.</para>
    /// </remarks>
    [Fact]
    public void NoProductAuthoredTranscriptWriteIsPersistedAsProse()
    {
        var source = File.ReadAllText(RepositoryFile(
            "apps/TechieDesk/Components/Pages/WorkspaceChat.razor"));

        // Any AddMessageAsync whose message argument is built straight from a Localizer lookup.
        var offenders = Regex.Matches(
            source,
            @"AddMessageAsync\([^;]*?ChatMessage\.\w+\(\s*Localizer\[",
            RegexOptions.Singleline);

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} transcript write(s) still localize at write time and freeze the sentence "
            + "into history. Persist a code + arguments (ChatTranscriptMessage) and render at display time.");
    }

    /// <summary>Opens a store over this test's own database file.</summary>
    /// <returns>The initialized store.</returns>
    private async Task<SqliteConversationStore> StoreAsync()
    {
        var store = new SqliteConversationStore($"Data Source={path}");
        await store.InitializeAsync();
        return store;
    }

    /// <summary>Resolves a repository-relative path from the test binary's location.</summary>
    /// <param name="relative">Path relative to the repository root.</param>
    /// <returns>The absolute path.</returns>
    private static string RepositoryFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative);
    }

    /// <summary>A localizer that reveals the key rather than any wording.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arguments">The values its holes take.</param>
    /// <returns>A marker naming the key.</returns>
    private static string Marker(string key, params object?[] arguments) =>
        arguments.Length == 0 ? $"[{key}]" : $"[{key}|{string.Join("|", arguments)}]";

    /// <inheritdoc />
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
