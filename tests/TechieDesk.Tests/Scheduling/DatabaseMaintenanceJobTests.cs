using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Scheduling;
using TechieDesk.Services.Scheduling.Jobs;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// The one built-in job handler, which is also the worked example of the connector seam
/// (REQ-FN-020): list items, report each result, let the runner classify the outcome.
/// </summary>
public sealed class DatabaseMaintenanceJobTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "techiedesk-maintenance-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temporary data directory.</summary>
    public DatabaseMaintenanceJobTests() => Directory.CreateDirectory(directory);

    /// <summary>Removes the temporary data directory.</summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Every database in the data directory is compacted and reported as its own item.</summary>
    [Fact]
    public async Task EveryDatabaseIsReportedAsItsOwnItem()
    {
        CreateDatabase("one.db");
        CreateDatabase("two.db");
        var harness = new Harness(directory);

        var run = await harness.RunAsync();

        Assert.Equal(RunOutcome.Succeeded, run.Outcome);
        Assert.Equal(2, run.ItemsProcessed);
        Assert.Equal(2, harness.Runs.Items.Count);
        Assert.Contains(harness.Runs.Items, item => item.ItemName == "one.db");
    }

    /// <summary>
    /// A file that is not a database fails as one item and the run continues — the BRD-65 shape,
    /// where one bad item does not discard the rest of the work.
    /// </summary>
    [Fact]
    public async Task ABrokenFileFailsAsOneItemAndTheRunContinues()
    {
        CreateDatabase("good.db");
        await File.WriteAllTextAsync(Path.Combine(directory, "broken.db"), "this is not a database");
        var harness = new Harness(directory);

        var run = await harness.RunAsync();

        Assert.Equal(RunOutcome.Partial, run.Outcome);
        Assert.Equal(1, run.ItemsProcessed);
        Assert.Equal(1, run.ItemsFailed);
        var failure = Assert.Single(harness.Runs.Items.Where(item => item.Status == RunItemStatus.Failed));
        Assert.Equal("broken.db", failure.ItemName);
        Assert.False(string.IsNullOrWhiteSpace(failure.Reason));
    }

    /// <summary>A missing data directory is a run failure that names the directory.</summary>
    [Fact]
    public async Task AMissingDataDirectoryFailsTheRunAndNamesIt()
    {
        var missing = Path.Combine(directory, "gone");
        var harness = new Harness(missing);

        var run = await harness.RunAsync();

        Assert.Equal(RunOutcome.Failed, run.Outcome);
        Assert.Contains(missing, run.FailureReason);
    }

    /// <summary>The action reads as plain language, never as JSON or cron (BRD-140).</summary>
    [Fact]
    public void TheActionDescribesItselfInPlainLanguage()
    {
        var handler = new DatabaseMaintenanceJobHandler(
            directory, NullLogger<DatabaseMaintenanceJobHandler>.Instance);

        Assert.Equal(
            "Compact the local databases",
            handler.DescribeAction("""{"anything":true}""").ToInvariantString());
        Assert.Null(handler.ValidatePayload(null));
    }

    private void CreateDatabase(string name)
    {
        var path = Path.Combine(directory, name);
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """CREATE TABLE "Sample" ("SampleId" INTEGER PRIMARY KEY);""";
        command.ExecuteNonQuery();
    }

    private sealed class Harness
    {
        private readonly JobRunner runner;

        public Harness(string dataDirectory)
        {
            Runs = new FakeScheduleRunRepository();
            var handler = new DatabaseMaintenanceJobHandler(
                dataDirectory, NullLogger<DatabaseMaintenanceJobHandler>.Instance);
            runner = new JobRunner(
                Runs,
                [handler],
                new TestClock(new DateTime(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc)),
                NullLogger<JobRunner>.Instance);
        }

        public FakeScheduleRunRepository Runs { get; }

        public Task<ScheduleRun> RunAsync() => runner.RunOnceAsync(
            "Nightly compaction", DatabaseMaintenanceJobHandler.Kind, null, null, CancellationToken.None);
    }
}
