using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Data;
using TechieDesk.Services.Scheduling;
using TechieDesk.Tests.Support;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// REQ-UI-056 (BRD-91): a run-history sentence is STORED as codes and arguments and RENDERED in the
/// reader's language — and a row that has no codes still prints the words it was written with.
/// </summary>
/// <remarks>
/// <para>
/// <b>The test that matters most here is <see cref="ALegacyRowRendersItsStoredTextInEveryLanguage"/>.</b>
/// It inserts a row shaped exactly like the 22 an installed build had already written — text in
/// <c>Detail</c> / <c>Reason</c>, nothing in the new <c>…Json</c> columns — through the real
/// migration and the real repository, and asserts the renderer prints that text unchanged in both
/// shipped languages. Everything else in this file is the new path; that one is the promise that
/// switching the new path on did not blank out anybody's history. It is written against the SHIPPED
/// migration rather than hand-rolled DDL, for the reason
/// <see cref="SchedulingPersistenceTests"/> records: a repository proved against a schema no user
/// has is not proved at all.
/// </para>
/// <para>
/// <b>Both halves are asserted on every new row.</b> A coded row must carry the codes AND the English
/// audit copy, because the helper host logs the detail line, support reads these rows in a database
/// browser, and a build that no longer recognises a code falls back to the text. A test that only
/// checked the codes would let the audit copy quietly stop being written.
/// </para>
/// </remarks>
public sealed class JobMessageTests : IDisposable
{
    /// <summary>The exact detail text an installed build had already written, five times over.</summary>
    private const string LegacyDetail = "2 ingested of 2 listed";

    /// <summary>The exact per-item reason an installed build had already written.</summary>
    private const string LegacyReason = "Added to workspace 09ed1034-3377-447e-8fc4-8f3f9d9919bd.";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "techiedesk-jobmessage-tests", Guid.NewGuid().ToString("N"));

    private readonly string connectionString;

    /// <summary>Creates a temporary database and migrates it with the shipped scripts.</summary>
    public JobMessageTests()
    {
        Directory.CreateDirectory(directory);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "techiedesk.db")
        }.ToString();

        Assert.Equal(0, MigrationRunner.Run("Sqlite", connectionString));
    }

    /// <summary>Deletes the temporary database.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A row written before REQ-UI-056 — text, no codes — renders its stored text verbatim, in
    /// English and in Hindi alike.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// The fallback is PERMANENT, not transitional. There is no code to look up for these rows and
    /// there never will be, so the only honest rendering is the sentence on disk. Asserting it in
    /// Hindi as well as English is the point: a renderer that quietly returned null, the key, or an
    /// empty string on a Hindi install would pass an English-only test.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public async Task ALegacyRowRendersItsStoredTextInEveryLanguage(string culture)
    {
        InsertLegacyRowsDirectly();

        var runs = Runs();
        var run = Assert.Single(await runs.ListRecentAsync(10));
        var item = Assert.Single(await runs.ListItemsAsync(run.ScheduleRunId));

        // The shape the migration guarantees for pre-existing data: text present, codes absent.
        Assert.Equal(LegacyDetail, run.Detail);
        Assert.Null(run.DetailJson);
        Assert.Null(run.FailureReasonJson);
        Assert.Equal(LegacyReason, item.Reason);
        Assert.Null(item.ReasonJson);

        using var resources = new ResourceHarness(culture);

        Assert.Equal(LegacyDetail, JobMessage.Render(run.Detail, run.DetailJson, resources.Localize));
        Assert.Equal(LegacyReason, JobMessage.Render(item.Reason, item.ReasonJson, resources.Localize));

        // A row that recorded nothing stays nothing, rather than becoming an empty string that a
        // screen would then render as a blank line under the item name.
        Assert.Null(JobMessage.Render(null, null, resources.Localize));
    }

    /// <summary>
    /// A run this build writes carries BOTH the codes and the English audit copy, and the codes are
    /// what a Hindi reader sees.
    /// </summary>
    /// <remarks>
    /// This is the same sentence as the legacy row above — "2 ingested of 2 listed" — which is what
    /// makes the pair readable as one story: the old row can only be English, the new one cannot be
    /// stuck in any language.
    /// </remarks>
    [Fact]
    public async Task ANewRowCarriesCodesAndStillCarriesTheEnglishAuditCopy()
    {
        var runs = Runs();
        var detail = JobMessage.Of("ConnectorRunDetailIngestedOfListed", 2, 2);
        var reason = JobMessage.Of(
            "ConnectorItemAddedToWorkspace", "09ed1034-3377-447e-8fc4-8f3f9d9919bd");

        var run = new ScheduleRun
        {
            JobName = "Handbook",
            JobKind = "Connector",
            TriggerKind = RunTrigger.Manual,
            StartedUtc = DateTime.UtcNow,
            Outcome = RunOutcome.Succeeded,
            Detail = detail.ToInvariantString(),
            DetailJson = detail.ToStorage()
        };

        await runs.StartAsync(run);
        await runs.AddItemsAsync(run.ScheduleRunId, [
            new ScheduleRunItem
            {
                ScheduleRunId = run.ScheduleRunId,
                ItemId = "readme",
                ItemName = "README.md",
                Status = RunItemStatus.Processed,
                Reason = reason.ToInvariantString(),
                ReasonJson = reason.ToStorage(),
                RecordedUtc = DateTime.UtcNow
            }
        ]);

        var stored = Assert.Single(await runs.ListRecentAsync(10));
        var item = Assert.Single(await runs.ListItemsAsync(stored.ScheduleRunId));

        Assert.Equal(LegacyDetail, stored.Detail);
        Assert.NotNull(stored.DetailJson);
        Assert.Equal(LegacyReason, item.Reason);
        Assert.NotNull(item.ReasonJson);

        using var english = new ResourceHarness("en");
        Assert.Equal(LegacyDetail, JobMessage.Render(stored.Detail, stored.DetailJson, english.Localize));

        using var hindi = new ResourceHarness("hi");
        var rendered = JobMessage.Render(stored.Detail, stored.DetailJson, hindi.Localize);
        var itemRendered = JobMessage.Render(item.Reason, item.ReasonJson, hindi.Localize);

        Assert.NotNull(rendered);
        Assert.NotEqual(LegacyDetail, rendered);
        Assert.Contains(rendered!, character => character is >= 'ऀ' and <= 'ॿ');

        // The numbers and the workspace id are DATA and survive the language change untouched — the
        // whole reason the stored unit is a code PLUS arguments and not a bare key.
        Assert.Contains("2", rendered!, StringComparison.Ordinal);
        Assert.Contains("09ed1034-3377-447e-8fc4-8f3f9d9919bd", itemRendered!, StringComparison.Ordinal);
    }

    /// <summary>A message made only of text this app did not author stores no codes at all.</summary>
    /// <remarks>
    /// A library exception's words cannot be translated, so writing <c>[{"text":"…"}]</c> beside the
    /// identical text column would double the storage to say nothing new — and it would hide the fact
    /// that the row is genuinely untranslatable behind a column that looks coded.
    /// </remarks>
    [Fact]
    public void AVerbatimMessageStoresNoCodesAndRendersItselfUnchanged()
    {
        var message = JobMessage.Text("404 from api.github.com");

        Assert.Null(message.ToStorage());
        Assert.Equal("404 from api.github.com", message.ToInvariantString());

        using var hindi = new ResourceHarness("hi");
        Assert.Equal("404 from api.github.com", message.Resolve(hindi.Localize));
    }

    /// <summary>Segments survive a round trip through storage, in order, with their arguments.</summary>
    [Fact]
    public void ComposedSegmentsRoundTripThroughStorage()
    {
        var message = JobMessage
            .Of("JobRunDetailProcessed", 3)
            .Then("JobRunDetailFailed", 1)
            .Then("JobRunDetailSkipped", 2);

        var restored = JobMessage.FromStorage(message.ToStorage());

        Assert.NotNull(restored);
        Assert.Equal(3, restored.Segments.Count);
        Assert.Equal(
            ["JobRunDetailProcessed", "JobRunDetailFailed", "JobRunDetailSkipped"],
            restored.Segments.Select(segment => segment.Code));
        Assert.Equal("3 processed · 1 failed · 2 skipped", restored.ToInvariantString());
    }

    /// <summary>
    /// A stored value that cannot be parsed falls back to the text beside it rather than throwing.
    /// </summary>
    /// <remarks>
    /// The run-details dialog paints whatever the table holds. A column hand-edited, truncated, or
    /// written by a build this one has never met must degrade to the English audit copy, not take
    /// the dialog down on its first render.
    /// </remarks>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"code\":\"X\"}")]
    [InlineData("[]")]
    [InlineData("")]
    public void UnreadableStoredCodesFallBackToTheStoredText(string stored)
    {
        using var hindi = new ResourceHarness("hi");

        Assert.Null(JobMessage.FromStorage(stored));
        Assert.Equal(LegacyDetail, JobMessage.Render(LegacyDetail, stored, hindi.Localize));
    }

    /// <summary>
    /// The neutral resolver returns ENGLISH, not the code — the audit copy and the model's prompt
    /// both depend on it.
    /// </summary>
    /// <remarks>
    /// <c>ResourceManager</c> finds nothing if the resource base name and the embedded resource name
    /// ever stop agreeing, and its answer to that is the code name — which would silently write
    /// "JobRunDetailProcessed" into every run row while every other test stayed green. Asserting the
    /// value differs from the code is what catches it. Asserted under a Hindi UI culture, because
    /// "neutral" has to mean neutral and not "whatever the thread happens to be set to".
    /// </remarks>
    [Fact]
    public void TheNeutralResolverReturnsEnglishWhateverTheUiCultureIs()
    {
        using var hindi = new ResourceHarness("hi");

        Assert.Equal("3 processed", JobMessage.Neutral("JobRunDetailProcessed", 3));
        Assert.Equal("The run was cancelled.", JobMessage.Neutral("JobRunCancelled"));
        Assert.NotEqual("JobRunCancelled", JobMessage.Neutral("JobRunCancelled"));
    }

    /// <summary>Arguments are formatted invariantly at capture time, so a stored row cannot drift.</summary>
    /// <remarks>
    /// A count formatted at RENDER time would make the same row mean subtly different things to two
    /// readers. Latin digits in every culture is also the rule <see cref="CronDescriber"/> already
    /// applies, and a run detail sitting beside a schedule sentence must not disagree with it.
    /// </remarks>
    [Fact]
    public void ArgumentsAreCapturedAsInvariantText()
    {
        var message = JobMessage.Of("ConnectorRunDetailIngestedOfListed", 1_500, 2_000);
        var segment = Assert.Single(message.Segments);

        // No group separator, and no culture's digits but Latin — asserted on the ARGUMENTS rather
        // than on the JSON, whose own commas would make the check pass for the wrong reason.
        Assert.Equal(["1500", "2000"], segment.Arguments);
        Assert.All(segment.Arguments, value => Assert.All(value, character => Assert.True(character is >= '0' and <= '9')));
    }

    /// <summary>The new columns exist on both tables after the shipped migration runs.</summary>
    /// <remarks>
    /// Nullable, so a database full of pre-REQ-UI-056 rows migrates without touching one of them.
    /// </remarks>
    [Theory]
    [InlineData("ScheduleRun", "DetailJson")]
    [InlineData("ScheduleRun", "FailureReasonJson")]
    [InlineData("ScheduleRunItem", "ReasonJson")]
    public void TheMigrationAddsANullableCompanionColumn(string table, string column)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT "notnull" FROM pragma_table_info('{table}') WHERE "name" = '{column}';""";

        var notNull = command.ExecuteScalar();
        Assert.NotNull(notNull);
        Assert.Equal(0L, Convert.ToInt64(notNull, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Builds the repository against this test's migrated database.</summary>
    /// <returns>The run-history repository.</returns>
    private ScheduleRunRepository Runs() => new(new AppDbConnectionFactory(Options.Create(new AppDbOptions
    {
        Provider = "Sqlite",
        ConnectionString = connectionString
    })));

    /// <summary>
    /// Writes a run and an item exactly as a pre-REQ-UI-056 build did — no companion columns named.
    /// </summary>
    /// <remarks>
    /// Deliberately raw SQL rather than the repository. The repository now always writes both halves,
    /// so it CANNOT produce the row this test is about; only an INSERT that predates the columns can,
    /// and that is precisely the row on the owner's installed database.
    /// </remarks>
    private void InsertLegacyRowsDirectly()
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var run = connection.CreateCommand();
        run.CommandText = """
            INSERT INTO "ScheduleRun" (
                "JobName", "JobKind", "TriggerKind", "StartedUtc", "CompletedUtc", "Outcome",
                "ItemsProcessed", "ItemsFailed", "ItemsSkipped", "Detail")
            VALUES ('Spoon-Knife (smoke)', 'Connector', 'Manual', '2026-07-27 09:00:00',
                    '2026-07-27 09:00:04', 'Succeeded', 2, 0, 0, $detail);
            """;
        run.Parameters.AddWithValue("$detail", LegacyDetail);
        run.ExecuteNonQuery();

        using var item = connection.CreateCommand();
        item.CommandText = """
            INSERT INTO "ScheduleRunItem" (
                "ScheduleRunId", "ItemId", "ItemName", "Status", "Reason", "RecordedUtc")
            VALUES (1, 'readme', 'README.md', 'Processed', $reason, '2026-07-27 09:00:03');
            """;
        item.Parameters.AddWithValue("$reason", LegacyReason);
        item.ExecuteNonQuery();
    }
}
