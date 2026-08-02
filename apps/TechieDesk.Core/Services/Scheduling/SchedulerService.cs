using System.Globalization;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Finds due schedules and runs them — the one scheduler implementation, hosted either by the app
/// window or by the background helper (REQ-FN-042, REQ-FN-028; BRD-139, BRD-93; ADR-009).
/// </summary>
/// <remarks>
/// <para><b>What survives the app closing, precisely.</b> The schedules, their next-run times and
/// their history are rows in the app database, so they survive anything. Whether a run <i>happens</i>
/// while the window is closed depends entirely on whether some process is hosting this class. With
/// the background helper installed, the helper hosts it and runs fire with the window closed. Without
/// it, nothing fires while the app is shut and the missed occurrence is caught up at next launch —
/// which is a real behaviour, not a fallback dressed up as one, and the UI says so in those words.</para>
/// <para><b>Missed occurrences are coalesced, never replayed.</b> One catch-up run stands in for
/// however many ticks were missed, and the run's detail line says how many. Replaying 336 half-hourly
/// syncs after a week away would hammer every remote source the connectors touch for no additional
/// information — the state to sync to is the current one.</para>
/// <para><b>The clock is injected.</b> Every decision here is a function of <see cref="TimeProvider"/>,
/// which is what makes DST transitions and week-long absences testable without waiting for one.</para>
/// </remarks>
public sealed class SchedulerService : ISchedulerService
{
    private readonly IScheduleRepository scheduleRepository;
    private readonly IScheduleRunRepository runRepository;
    private readonly IBackgroundJobService backgroundJobs;
    private readonly RunConditionEvaluator runConditions;
    private readonly ISchedulerPreferencesStore preferencesStore;
    private readonly TimeProvider timeProvider;
    private readonly SchedulerOptions options;
    private readonly ILogger<SchedulerService> logger;
    private readonly LocalizeText localize;

    /// <summary>Initializes the scheduler.</summary>
    /// <param name="scheduleRepository">Schedule persistence.</param>
    /// <param name="runRepository">Run-history persistence.</param>
    /// <param name="backgroundJobs">Runs jobs and owns the in-flight guard.</param>
    /// <param name="runConditions">Tests BRD-139 run conditions.</param>
    /// <param name="preferencesStore">Reads the configured run conditions.</param>
    /// <param name="timeProvider">Clock.</param>
    /// <param name="options">Poll configuration.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    public SchedulerService(
        IScheduleRepository scheduleRepository,
        IScheduleRunRepository runRepository,
        IBackgroundJobService backgroundJobs,
        RunConditionEvaluator runConditions,
        ISchedulerPreferencesStore preferencesStore,
        TimeProvider timeProvider,
        IOptions<SchedulerOptions> options,
        ILogger<SchedulerService> logger,
        LocalizeText localize)
    {
        this.scheduleRepository = scheduleRepository;
        this.runRepository = runRepository;
        this.backgroundJobs = backgroundJobs;
        this.runConditions = runConditions;
        this.preferencesStore = preferencesStore;
        this.timeProvider = timeProvider;
        this.options = options.Value;
        this.logger = logger;
        this.localize = localize;
    }

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await PrimeAsync(cancellationToken).ConfigureAwait(false);

        IsRunning = true;
        logger.LogInformation(
            "Scheduler started; polling every {Seconds}s", options.PollInterval.TotalSeconds);

        using var timer = new PeriodicTimer(options.PollInterval, timeProvider);
        try
        {
            // Poll immediately rather than waiting out the first interval: a catch-up run the user is
            // sitting there expecting must not be 30 seconds late on top of being late already.
            await PollAsync(cancellationToken).ConfigureAwait(false);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await PollAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Scheduler stopped");
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <inheritdoc />
    public async Task PrimeAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // A run still marked Running belongs to a process that no longer exists. Left alone it would
        // read as in flight forever; the in-flight guard is in memory, so it does not block anything,
        // but the history would lie about it.
        var abandoned = await runRepository.CloseAbandonedRunsAsync(
            "The application stopped while this run was in progress.", nowUtc).ConfigureAwait(false);
        if (abandoned > 0)
        {
            logger.LogWarning("Closed {Count} run(s) abandoned by a previous process", abandoned);
        }

        foreach (var schedule in await scheduleRepository.ListAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!schedule.IsEnabled)
            {
                continue;
            }

            if (schedule.NextRunUtc is not null)
            {
                continue;
            }

            // A schedule with no next run has never been primed, or its expression was edited. Base
            // the first occurrence on the last run when there is one, so an app that was closed over
            // an occurrence still sees it as missed rather than starting the clock fresh.
            var basis = schedule.LastRunUtc ?? nowUtc;
            var next = ScheduleCalculator.NextRunUtc(schedule, basis);
            if (next is null)
            {
                logger.LogWarning(
                    "Schedule {Name} has an expression that never fires again and was left unscheduled",
                    schedule.Name);
                continue;
            }

            schedule.NextRunUtc = next;
            schedule.UpdatedUtc = nowUtc;
            await scheduleRepository.UpdateAsync(schedule).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduleRun>> PollAsync(CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var due = await scheduleRepository.ListDueAsync(nowUtc).ConfigureAwait(false);
        if (due.Count == 0)
        {
            return [];
        }

        var preferences = await preferencesStore.LoadAsync().ConfigureAwait(false);
        var verdict = runConditions.Evaluate(preferences.ToRunConditions());
        var started = new List<ScheduleRun>();

        foreach (var schedule in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var kind = ScheduleCalculator.Classify(schedule, nowUtc);
            if (kind == DueKind.NotDue)
            {
                continue;
            }

            var run = await RunDueScheduleAsync(schedule, kind, nowUtc, verdict).ConfigureAwait(false);
            if (run is not null)
            {
                started.Add(run);
            }
        }

        return started;
    }

    private async Task<ScheduleRun?> RunDueScheduleAsync(
        Schedule schedule, DueKind kind, DateTime nowUtc, RunConditionVerdict verdict)
    {
        var dueUtc = schedule.NextRunUtc ?? nowUtc;

        // Advance the next-run time BEFORE running. A long run must not be re-detected as due on the
        // next poll, and a crash mid-run must not leave the schedule pinned to a past instant that
        // fires again on every single poll forever.
        var next = ScheduleCalculator.NextRunUtc(schedule, nowUtc);
        await scheduleRepository.RecordRunAsync(schedule.ScheduleId, nowUtc, next).ConfigureAwait(false);
        schedule.NextRunUtc = next;

        if (kind == DueKind.Missed && !schedule.CatchUpMissedRuns)
        {
            await RecordSkippedAsync(
                schedule,
                nowUtc,
                localize(
                    "SchedulerSkipMissedNoCatchUp",
                    dueUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))
                .ConfigureAwait(false);
            return null;
        }

        if (!verdict.IsAllowed)
        {
            await RecordSkippedAsync(schedule, nowUtc, verdict.Reason).ConfigureAwait(false);
            return null;
        }

        var trigger = kind == DueKind.Missed ? RunTrigger.CatchUp : RunTrigger.Scheduled;
        if (kind == DueKind.Missed)
        {
            var missed = ScheduleCalculator.CountOccurrences(schedule, dueUtc - TimeSpan.FromSeconds(1), nowUtc);
            logger.LogInformation(
                "Catching up {Name}: {Missed} occurrence(s) passed since {Due:u}; running once",
                schedule.Name, missed, dueUtc);
        }

        return await backgroundJobs.RunScheduleAsync(schedule, trigger).ConfigureAwait(false);
    }

    private async Task RecordSkippedAsync(Schedule schedule, DateTime nowUtc, string? reason)
    {
        // A skip is written to the history rather than only logged. "Why did this not run last
        // night" has to be answerable from the same table that answers "what did it do".
        var run = new ScheduleRun
        {
            ScheduleId = schedule.ScheduleId,
            JobName = schedule.Name,
            JobKind = schedule.JobKind,
            TriggerKind = RunTrigger.Scheduled,
            StartedUtc = nowUtc,
            CompletedUtc = nowUtc,
            Outcome = RunOutcome.Skipped,
            Detail = reason
        };
        await runRepository.StartAsync(run).ConfigureAwait(false);
        await runRepository.CompleteAsync(run).ConfigureAwait(false);
        logger.LogInformation("Skipped {Name}: {Reason}", schedule.Name, reason);
    }
}
