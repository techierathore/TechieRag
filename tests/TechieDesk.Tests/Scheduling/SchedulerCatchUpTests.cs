using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Scheduling;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// What the scheduler does about time it was not running for (REQ-FN-042 / BRD-139) — the behaviour
/// that decides whether closing the app loses a run.
/// </summary>
public sealed class SchedulerCatchUpTests
{
    private static readonly DateTime Monday0700 = new(2026, 7, 27, 7, 0, 0, DateTimeKind.Utc);

    /// <summary>An occurrence that came due while the scheduler was running is a normal tick.</summary>
    [Fact]
    public async Task DueOccurrenceRunsAsAScheduledTick()
    {
        var harness = new Harness(Monday0700);
        harness.AddSchedule("Daily sync", "0 7 * * *", nextRunUtc: Monday0700);

        var runs = await harness.Scheduler.PollAsync(CancellationToken.None);

        Assert.Single(runs);
        Assert.Equal(RunTrigger.Scheduled, runs[0].TriggerKind);
        Assert.Equal(1, harness.Handler.RunCount);
    }

    /// <summary>
    /// An occurrence missed while the app was closed runs once at the next start, flagged as a
    /// catch-up rather than as a normal tick.
    /// </summary>
    [Fact]
    public async Task MissedOccurrenceIsCaughtUpOnce()
    {
        // The schedule was due at 07:00; nothing was running until 09:14.
        var harness = new Harness(Monday0700.AddHours(2).AddMinutes(14));
        harness.AddSchedule("Daily sync", "0 7 * * *", nextRunUtc: Monday0700);

        var runs = await harness.Scheduler.PollAsync(CancellationToken.None);

        Assert.Single(runs);
        Assert.Equal(RunTrigger.CatchUp, runs[0].TriggerKind);
    }

    /// <summary>
    /// A week of missed half-hourly occurrences produces ONE run, not one per occurrence — the
    /// coalescing rule.
    /// </summary>
    [Fact]
    public async Task AWeekOfMissedOccurrencesProducesOneRun()
    {
        var start = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var harness = new Harness(start.AddDays(7));
        harness.AddSchedule("Mailbox sync", "*/30 * * * *", nextRunUtc: start);

        var runs = await harness.Scheduler.PollAsync(CancellationToken.None);

        Assert.Single(runs);
        Assert.Equal(1, harness.Handler.RunCount);
    }

    /// <summary>
    /// With catch-up off, a missed occurrence is skipped and the skip is recorded with its reason
    /// rather than only logged.
    /// </summary>
    [Fact]
    public async Task CatchUpOffSkipsAndRecordsWhy()
    {
        var harness = new Harness(Monday0700.AddHours(3));
        harness.AddSchedule("Daily sync", "0 7 * * *", nextRunUtc: Monday0700, catchUp: false);

        var runs = await harness.Scheduler.PollAsync(CancellationToken.None);

        Assert.Empty(runs);
        Assert.Equal(0, harness.Handler.RunCount);
        var recorded = Assert.Single(harness.Runs.Runs);
        Assert.Equal(RunOutcome.Skipped, recorded.Outcome);
        Assert.Contains("Catch-up is off", recorded.Detail);
    }

    /// <summary>A run that was skipped still advances the schedule, so it does not fire on every poll.</summary>
    [Fact]
    public async Task ASkippedOccurrenceStillAdvancesTheSchedule()
    {
        var harness = new Harness(Monday0700.AddHours(3));
        harness.AddSchedule("Daily sync", "0 7 * * *", nextRunUtc: Monday0700, catchUp: false);

        await harness.Scheduler.PollAsync(CancellationToken.None);
        var afterFirst = harness.Schedules.Items[0].NextRunUtc;
        await harness.Scheduler.PollAsync(CancellationToken.None);

        Assert.Equal(Monday0700.AddDays(1), afterFirst);
        Assert.Single(harness.Runs.Runs);
    }

    /// <summary>A paused schedule never fires, however long it has been due.</summary>
    [Fact]
    public async Task APausedScheduleNeverFires()
    {
        var harness = new Harness(Monday0700.AddDays(30));
        harness.AddSchedule("Daily sync", "0 7 * * *", nextRunUtc: Monday0700, isEnabled: false);

        var runs = await harness.Scheduler.PollAsync(CancellationToken.None);

        Assert.Empty(runs);
        Assert.Empty(harness.Runs.Runs);
    }

    /// <summary>
    /// The next run is advanced before the job starts, so a long run is not re-detected as due while
    /// it is still going.
    /// </summary>
    [Fact]
    public async Task NextRunAdvancesBeforeTheJobStarts()
    {
        var harness = new Harness(Monday0700);
        harness.AddSchedule("Daily sync", "0 7 * * *", nextRunUtc: Monday0700);
        var writesSeenDuringRun = new List<DateTime?>();
        harness.Handler.Behaviour = (_, _) =>
        {
            // The WRITE, not the in-memory object: a scheduler that only moved the field it already
            // held would leave a crash pinned to a past instant that fires on every poll forever.
            writesSeenDuringRun.AddRange(harness.Schedules.RecordedRuns.Select(write => write.NextRunUtc));
            return Task.FromResult(JobRunResult.Completed);
        };

        await harness.Scheduler.PollAsync(CancellationToken.None);

        Assert.Equal([Monday0700.AddDays(1)], writesSeenDuringRun);
    }

    /// <summary>
    /// A disabled schedule classifies as not due however far past its next run is — pausing means
    /// paused, not queued.
    /// </summary>
    [Fact]
    public void ADisabledScheduleClassifiesAsNotDue()
    {
        var schedule = new Schedule
        {
            Name = "Paused",
            CronExpression = "0 7 * * *",
            TimeZoneId = TimeZoneInfo.Utc.Id,
            IsEnabled = false,
            NextRunUtc = Monday0700
        };

        Assert.Equal(DueKind.NotDue, ScheduleCalculator.Classify(schedule, Monday0700.AddDays(30)));

        schedule.IsEnabled = true;
        Assert.Equal(DueKind.Missed, ScheduleCalculator.Classify(schedule, Monday0700.AddDays(30)));
    }

    /// <summary>
    /// Priming closes a run left in flight by a process that died, so the history does not claim it
    /// is still going.
    /// </summary>
    [Fact]
    public async Task PrimingClosesRunsAbandonedByADeadProcess()
    {
        var harness = new Harness(Monday0700);
        await harness.Runs.StartAsync(new ScheduleRun
        {
            JobName = "Interrupted",
            JobKind = "Test",
            StartedUtc = Monday0700.AddDays(-1),
            Outcome = RunOutcome.Running
        });

        await harness.Scheduler.PrimeAsync(CancellationToken.None);

        var run = Assert.Single(harness.Runs.Runs);
        Assert.Equal(RunOutcome.Failed, run.Outcome);
        Assert.Contains("stopped while this run was in progress", run.FailureReason);
    }

    /// <summary>
    /// Priming gives a schedule that has never been primed a next-run time, computed from its last
    /// run so an occurrence passed while the app was closed still reads as missed.
    /// </summary>
    [Fact]
    public async Task PrimingSchedulesFromTheLastRunNotFromNow()
    {
        var harness = new Harness(Monday0700.AddDays(3));
        var schedule = harness.AddSchedule("Daily sync", "0 7 * * *", nextRunUtc: null);
        schedule.LastRunUtc = Monday0700;

        await harness.Scheduler.PrimeAsync(CancellationToken.None);

        // The next occurrence after the last run is the following morning, which is already past —
        // exactly what makes the next poll a catch-up rather than a fresh start.
        Assert.Equal(Monday0700.AddDays(1), harness.Schedules.Items[0].NextRunUtc);
    }

    /// <summary>A run condition that is not met skips the run and records the reason.</summary>
    [Fact]
    public async Task ARunConditionThatIsNotMetSkipsAndRecordsWhy()
    {
        var harness = new Harness(Monday0700, new FakeRunEnvironmentProbe(PowerState.Battery, null));
        harness.Preferences.Current = SchedulerPreferences.Default with { MainsPowerOnly = true };
        harness.AddSchedule("Daily sync", "0 7 * * *", nextRunUtc: Monday0700);

        var runs = await harness.Scheduler.PollAsync(CancellationToken.None);

        Assert.Empty(runs);
        var recorded = Assert.Single(harness.Runs.Runs);
        Assert.Equal(RunOutcome.Skipped, recorded.Outcome);
        Assert.Contains("battery", recorded.Detail);
    }

    private sealed class Harness
    {
        public Harness(DateTime nowUtc, IRunEnvironmentProbe? probe = null)
        {
            Clock = new TestClock(nowUtc);
            Handler = new FakeJobHandler();
            Runs = new FakeScheduleRunRepository();
            Schedules = new FakeScheduleRepository();
            Preferences = new FakeSchedulerPreferencesStore();

            var runner = new JobRunner(
                Runs, [Handler], Clock, NullLogger<JobRunner>.Instance);
            var jobs = new BackgroundJobService(runner, NullLogger<BackgroundJobService>.Instance);
            Scheduler = new SchedulerService(
                Schedules,
                Runs,
                jobs,
                new RunConditionEvaluator(
                    probe ?? new FakeRunEnvironmentProbe(PowerState.Mains, "Home"), SchedulingText.Localize),
                Preferences,
                Clock,
                Options.Create(new SchedulerOptions()),
                NullLogger<SchedulerService>.Instance,
                SchedulingText.Localize);
        }

        public TestClock Clock { get; }

        public FakeJobHandler Handler { get; }

        public FakeScheduleRepository Schedules { get; }

        public FakeScheduleRunRepository Runs { get; }

        public FakeSchedulerPreferencesStore Preferences { get; }

        public SchedulerService Scheduler { get; }

        public Schedule AddSchedule(
            string name,
            string cron,
            DateTime? nextRunUtc,
            bool catchUp = true,
            bool isEnabled = true)
        {
            var schedule = new Schedule
            {
                Name = name,
                JobKind = "Test",
                ActionSummary = "Test action",
                CronExpression = cron,
                TimeZoneId = TimeZoneInfo.Utc.Id,
                ScheduleText = "Test schedule",
                IsEnabled = isEnabled,
                CatchUpMissedRuns = catchUp,
                NextRunUtc = nextRunUtc,
                CreatedUtc = Clock.GetUtcNow().UtcDateTime,
                UpdatedUtc = Clock.GetUtcNow().UtcDateTime
            };
            Schedules.CreateAsync(schedule).GetAwaiter().GetResult();
            return schedule;
        }
    }
}
