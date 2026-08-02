using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Data;
using TechieDesk.Services.Scheduling;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// The scheduling tables and their repositories, against a real SQLite file migrated by the real
/// DbUp script (0005-Scheduling.sql).
/// </summary>
/// <remarks>
/// <para>These run the actual migration rather than hand-rolling the DDL. A test that creates its own
/// tables proves the repository's SQL against a schema no user will ever have; running the shipped
/// script proves the pair together, which is the only version of this test worth having.</para>
/// <para>They also assert the second-run behaviour: DbUp journals what it applied, so re-running must
/// apply zero scripts. That is the property that makes migration-at-launch (ADR-007) safe.</para>
/// </remarks>
public sealed class SchedulingPersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "techiedesk-scheduling-tests", Guid.NewGuid().ToString("N"));

    private readonly string connectionString;

    /// <summary>Creates a temporary database and migrates it.</summary>
    public SchedulingPersistenceTests()
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

    /// <summary>Re-running the migrator applies nothing, because DbUp journals what it applied.</summary>
    [Fact]
    public void ASecondMigrationRunAppliesNothing()
    {
        Assert.Equal(0, MigrationRunner.Run("Sqlite", connectionString));

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "SchemaVersions" WHERE "ScriptName" LIKE '%0005-Scheduling%';""";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    /// <summary>A schedule round-trips through the repository unchanged.</summary>
    [Fact]
    public async Task AScheduleRoundTrips()
    {
        var repository = Schedules();
        var schedule = NewSchedule();

        var id = await repository.CreateAsync(schedule);
        var read = await repository.GetAsync(id);

        Assert.NotNull(read);
        Assert.Equal("Sync legal mailbox", read.Name);
        Assert.Equal("0 7 * * 1-5", read.CronExpression);
        Assert.Equal("Every weekday at 07:00", read.ScheduleText);
        Assert.Equal("every weekday at 7, sync the mailbox", read.SourceInstruction);
        Assert.True(read.CatchUpMissedRuns);
        Assert.True(read.IsEnabled);
    }

    /// <summary>Only enabled schedules whose next run has passed come back as due.</summary>
    [Fact]
    public async Task OnlyEnabledSchedulesThatArePastDueComeBack()
    {
        var repository = Schedules();
        var now = new DateTime(2026, 7, 27, 9, 0, 0, DateTimeKind.Utc);

        var due = NewSchedule();
        due.NextRunUtc = now.AddMinutes(-1);
        await repository.CreateAsync(due);

        var future = NewSchedule("Later");
        future.NextRunUtc = now.AddHours(1);
        await repository.CreateAsync(future);

        var paused = NewSchedule("Paused");
        paused.NextRunUtc = now.AddMinutes(-1);
        paused.IsEnabled = false;
        await repository.CreateAsync(paused);

        var result = await repository.ListDueAsync(now);

        var single = Assert.Single(result);
        Assert.Equal("Sync legal mailbox", single.Name);
    }

    /// <summary>Two schedules cannot share a name.</summary>
    [Fact]
    public async Task ScheduleNamesAreUnique()
    {
        var repository = Schedules();
        await repository.CreateAsync(NewSchedule());

        await Assert.ThrowsAsync<SqliteException>(() => repository.CreateAsync(NewSchedule()));
    }

    /// <summary>A run and its per-item failures round-trip, with failures listed first.</summary>
    [Fact]
    public async Task ARunAndItsItemFailuresRoundTrip()
    {
        var runs = Runs();
        var run = new ScheduleRun
        {
            JobName = "Sync legal mailbox",
            JobKind = "Test",
            TriggerKind = RunTrigger.CatchUp,
            StartedUtc = new DateTime(2026, 7, 27, 9, 0, 0, DateTimeKind.Utc),
            Outcome = RunOutcome.Running
        };
        var id = await runs.StartAsync(run);

        await runs.AddItemsAsync(id,
        [
            new ScheduleRunItem { ItemId = "1", ItemName = "ok", Status = RunItemStatus.Processed },
            new ScheduleRunItem
            {
                ItemId = "2", ItemName = "broken", Status = RunItemStatus.Failed, Reason = "403 from the source"
            }
        ]);

        run.Outcome = RunOutcome.Partial;
        run.ItemsProcessed = 1;
        run.ItemsFailed = 1;
        run.CompletedUtc = run.StartedUtc.AddSeconds(22);
        run.Detail = "1 processed · 1 failed";
        await runs.CompleteAsync(run);

        var stored = Assert.Single(await runs.ListRecentAsync(10));
        Assert.Equal(RunOutcome.Partial, stored.Outcome);
        Assert.Equal(RunTrigger.CatchUp, stored.TriggerKind);
        Assert.Equal(TimeSpan.FromSeconds(22), stored.Duration);

        var items = await runs.ListItemsAsync(id);
        Assert.Equal(RunItemStatus.Failed, items[0].Status);
        Assert.Equal("403 from the source", items[0].Reason);
    }

    /// <summary>
    /// Deleting a schedule keeps its history and detaches it, so what an automation did survives the
    /// automation.
    /// </summary>
    [Fact]
    public async Task DeletingAScheduleKeepsItsHistory()
    {
        var schedules = Schedules();
        var runs = Runs();
        var id = await schedules.CreateAsync(NewSchedule());
        await runs.StartAsync(new ScheduleRun
        {
            ScheduleId = id,
            JobName = "Sync legal mailbox",
            JobKind = "Test",
            StartedUtc = DateTime.UtcNow,
            Outcome = RunOutcome.Succeeded
        });

        await schedules.DeleteAsync(id);

        var stored = Assert.Single(await runs.ListRecentAsync(10));
        Assert.Null(stored.ScheduleId);
        Assert.Equal("Sync legal mailbox", stored.JobName);
    }

    /// <summary>A run abandoned by a dead process is closed as failed with its reason.</summary>
    [Fact]
    public async Task AnAbandonedRunIsClosedAsFailed()
    {
        var runs = Runs();
        await runs.StartAsync(new ScheduleRun
        {
            JobName = "Interrupted",
            JobKind = "Test",
            StartedUtc = DateTime.UtcNow.AddHours(-1),
            Outcome = RunOutcome.Running
        });

        var closed = await runs.CloseAbandonedRunsAsync(
            JobMessage.Of("SchedulerRunAbandonedByProcess"), DateTime.UtcNow);

        Assert.Equal(1, closed);
        var stored = Assert.Single(await runs.ListRecentAsync(10));
        Assert.Equal(RunOutcome.Failed, stored.Outcome);

        // REQ-UI-056: both halves land — the English audit copy AND the codes that render it in the
        // reader's language.
        Assert.Equal("The application stopped while this run was in progress.", stored.FailureReason);
        Assert.NotNull(stored.FailureReasonJson);
    }

    /// <summary>The scheduler preferences round-trip through the instance-setting table.</summary>
    [Fact]
    public async Task SchedulerPreferencesRoundTripThroughTheSettingsTable()
    {
        var settings = new InstanceSettingRepository(Factory());
        var store = new SchedulerPreferencesStore(
            settings, Microsoft.Extensions.Logging.Abstractions.NullLogger<SchedulerPreferencesStore>.Instance);

        await store.SaveAsync(new SchedulerPreferences(
            BackgroundServiceEnabled: true, MainsPowerOnly: true, AllowedNetworks: ["Home"]));
        var read = await store.LoadAsync();

        Assert.True(read.BackgroundServiceEnabled);
        Assert.True(read.MainsPowerOnly);
        Assert.Equal(["Home"], read.AllowedNetworks);
    }

    private static Schedule NewSchedule(string name = "Sync legal mailbox") => new()
    {
        Name = name,
        JobKind = "Test",
        ActionSummary = "Email connector → Contracts",
        CronExpression = "0 7 * * 1-5",
        TimeZoneId = TimeZoneInfo.Utc.Id,
        ScheduleText = "Every weekday at 07:00",
        SourceInstruction = "every weekday at 7, sync the mailbox",
        CreatedUtc = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc),
        UpdatedUtc = new DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc)
    };

    private AppDbConnectionFactory Factory() => new(Options.Create(new AppDbOptions
    {
        Provider = "Sqlite",
        ConnectionString = connectionString
    }));

    private ScheduleRepository Schedules() => new(Factory());

    private ScheduleRunRepository Runs() => new(Factory());
}
