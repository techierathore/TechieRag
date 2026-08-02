using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Scheduling;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// Run classification, progress and per-item results (REQ-FN-020 / BRD-65) — the shape a connector
/// run reports through.
/// </summary>
public sealed class JobRunnerTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>A run with no failures is recorded as succeeded.</summary>
    [Fact]
    public async Task ACleanRunSucceeds()
    {
        var harness = new Harness();
        harness.Handler.Behaviour = (context, _) =>
        {
            context.Progress.RecordItem(RunItemStatus.Processed, "1", "one");
            context.Progress.RecordItem(RunItemStatus.Processed, "2", "two");
            return Task.FromResult(JobRunResult.Completed);
        };

        var run = await harness.RunAsync();

        Assert.Equal(RunOutcome.Succeeded, run.Outcome);
        Assert.Equal(2, run.ItemsProcessed);
        Assert.Equal(0, run.ItemsFailed);
    }

    /// <summary>
    /// A run where some items failed is Partial, never Succeeded — a handler cannot report "412 of
    /// 500 ingested" as a success.
    /// </summary>
    [Fact]
    public async Task SomeItemsFailingMakesTheRunPartial()
    {
        var harness = new Harness();
        harness.Handler.Behaviour = (context, _) =>
        {
            context.Progress.RecordItem(RunItemStatus.Processed, "1", "one");
            context.Progress.RecordItem(RunItemStatus.Failed, "2", "two", "the source returned 403");
            return Task.FromResult(JobRunResult.Completed);
        };

        var run = await harness.RunAsync();

        Assert.Equal(RunOutcome.Partial, run.Outcome);
        Assert.Equal(1, run.ItemsFailed);
    }

    /// <summary>Every per-item failure is persisted with its reason, so a retry can name it.</summary>
    [Fact]
    public async Task PerItemFailureReasonsArePersisted()
    {
        var harness = new Harness();
        harness.Handler.Behaviour = (context, _) =>
        {
            context.Progress.RecordItem(RunItemStatus.Failed, "msg-9", "Renewal notice", "attachment too large");
            return Task.FromResult(JobRunResult.Completed);
        };

        await harness.RunAsync();

        var item = Assert.Single(harness.Runs.Items);
        Assert.Equal("msg-9", item.ItemId);
        Assert.Equal("Renewal notice", item.ItemName);
        Assert.Equal("attachment too large", item.Reason);
        Assert.Equal(RunItemStatus.Failed, item.Status);
    }

    /// <summary>
    /// A handler that throws produces a failed run carrying the message, not an escaping exception —
    /// this runs on a background timer inside a desktop process.
    /// </summary>
    [Fact]
    public async Task AThrowingHandlerBecomesAFailedRun()
    {
        var harness = new Harness();
        harness.Handler.Behaviour = (_, _) => throw new InvalidOperationException("the mailbox refused the login");

        var run = await harness.RunAsync();

        Assert.Equal(RunOutcome.Failed, run.Outcome);
        Assert.Equal("the mailbox refused the login", run.FailureReason);
    }

    /// <summary>A handler that reports a failure reason fails the run even with no failed items.</summary>
    [Fact]
    public async Task AReportedFailureReasonFailsTheRun()
    {
        var harness = new Harness();
        harness.Handler.Behaviour = (_, _) =>
            Task.FromResult(JobRunResult.Failed("the connector is not configured"));

        var run = await harness.RunAsync();

        Assert.Equal(RunOutcome.Failed, run.Outcome);
        Assert.Equal("the connector is not configured", run.FailureReason);
    }

    /// <summary>A schedule whose handler is not installed names the missing kind rather than failing blankly.</summary>
    [Fact]
    public async Task AMissingHandlerNamesTheKind()
    {
        var harness = new Harness();

        var run = await harness.Runner.RunOnceAsync("Orphan", "ConnectorSync", null, null, CancellationToken.None);

        Assert.Equal(RunOutcome.Failed, run.Outcome);
        Assert.Contains("ConnectorSync", run.FailureReason);
    }

    /// <summary>Cancellation is recorded as cancelled, not as a failure.</summary>
    [Fact]
    public async Task CancellationIsRecordedAsCancelled()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        harness.Handler.Behaviour = (_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(JobRunResult.Completed);
        };

        var run = await harness.Runner.RunOnceAsync("Long job", "Test", null, null, cancellation.Token);

        Assert.Equal(RunOutcome.Cancelled, run.Outcome);
    }

    /// <summary>Progress snapshots reach the caller while the run is still going (REQ-FN-020).</summary>
    [Fact]
    public async Task ProgressIsVisibleWhileTheRunIsGoing()
    {
        var harness = new Harness();
        var snapshots = new List<JobProgressSnapshot>();
        harness.Handler.Behaviour = (context, _) =>
        {
            context.Progress.Report(0, 3, "starting");
            context.Progress.RecordItem(RunItemStatus.Processed, "1", "one");
            context.Progress.RecordItem(RunItemStatus.Processed, "2", "two");
            return Task.FromResult(JobRunResult.Completed);
        };

        await harness.Runner.RunOnceAsync("Job", "Test", null, snapshots.Add, CancellationToken.None);

        Assert.Equal(3, snapshots.Count);
        Assert.Equal("starting", snapshots[0].Message);
        Assert.Equal(3, snapshots[^1].Total);
        Assert.Equal(2, snapshots[^1].Processed);
    }

    /// <summary>Percent complete is null while the total is unknown rather than an invented number.</summary>
    [Fact]
    public void PercentIsNullWithoutATotal()
    {
        var withoutTotal = new JobProgressSnapshot(1, null, "Job", "Test", Now, 5, 0, 0, null, null);
        var withTotal = new JobProgressSnapshot(1, null, "Job", "Test", Now, 5, 0, 0, 10, null);

        Assert.Null(withoutTotal.PercentComplete);
        Assert.Equal(50d, withTotal.PercentComplete);
    }

    /// <summary>
    /// Successful items stop being retained past the cap while every failure is kept, and the run
    /// says the list was sampled instead of truncating in silence.
    /// </summary>
    [Fact]
    public async Task SuccessesAreCappedButFailuresAreNot()
    {
        var harness = new Harness();
        harness.Handler.Behaviour = (context, _) =>
        {
            for (var index = 0; index < JobProgressCollector.SuccessItemCap + 50; index++)
            {
                context.Progress.RecordItem(RunItemStatus.Processed, index.ToString(), $"item {index}");
            }

            for (var index = 0; index < 10; index++)
            {
                context.Progress.RecordItem(RunItemStatus.Failed, $"f{index}", $"failure {index}", "broken");
            }

            return Task.FromResult(JobRunResult.Completed);
        };

        var run = await harness.RunAsync();

        Assert.Equal(JobProgressCollector.SuccessItemCap + 10, harness.Runs.Items.Count);
        Assert.Equal(10, harness.Runs.Items.Count(item => item.Status == RunItemStatus.Failed));
        Assert.Contains("capped", run.Detail);
    }

    /// <summary>A hand-started background job records a run with no schedule behind it (REQ-FN-020).</summary>
    [Fact]
    public async Task AHandStartedJobRecordsARunWithNoSchedule()
    {
        var harness = new Harness();

        var run = await harness.Runner.RunOnceAsync("Sync now", "Test", "{}", null, CancellationToken.None);

        Assert.Null(run.ScheduleId);
        Assert.Equal(RunTrigger.Background, run.TriggerKind);
    }

    private sealed class Harness
    {
        public Harness()
        {
            Runs = new FakeScheduleRunRepository();
            Handler = new FakeJobHandler();
            Runner = new JobRunner(Runs, [Handler], new TestClock(Now), NullLogger<JobRunner>.Instance);
        }

        public FakeScheduleRunRepository Runs { get; }

        public FakeJobHandler Handler { get; }

        public JobRunner Runner { get; }

        public Task<ScheduleRun> RunAsync() =>
            Runner.RunOnceAsync("Job", "Test", null, null, CancellationToken.None);
    }
}
