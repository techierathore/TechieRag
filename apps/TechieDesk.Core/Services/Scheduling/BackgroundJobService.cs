using System.Collections.Concurrent;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Default <see cref="IBackgroundJobService"/>: an in-process registry of running jobs
/// (REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <para><b>No queue, no worker pool, no job store.</b> BRD-139 rules those out and nothing here
/// needs them: a desktop install runs a handful of jobs, and the durable record of what happened is
/// the run-history table, not a queue. What this class owns is only the <i>live</i> view — the
/// progress the user is watching and the token that stops it.</para>
/// <para><b>One run per schedule at a time.</b> A half-hourly sync that takes 40 minutes must not
/// stack up; the second attempt is refused rather than queued, and the refusal is visible as a
/// skipped tick rather than as two connectors fighting over the same store.</para>
/// <para><b>One run per hand-started job at a time, too.</b> That guard used to exist only for
/// schedules: <see cref="IsRunning(long)"/> matches on <c>ScheduleId</c>, which is null for every run
/// started from a screen, so two clicks of "Sync now" produced two concurrent walks of one source —
/// duplicate ingests, doubled rate-limit consumption, and two runs each saving sync state the other
/// could not see. <see cref="running"/> answers "which runs exist"; <see cref="claims"/> answers
/// "which jobs are already in flight", and it has to be a separate map because the claim is taken
/// BEFORE the run row exists and therefore before there is an id to key on.</para>
/// </remarks>
public sealed class BackgroundJobService : IBackgroundJobService, IDisposable
{
    private readonly ConcurrentDictionary<long, RunningJob> running = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<long>> claims =
        new(StringComparer.Ordinal);

    private readonly IJobRunner runner;
    private readonly ILogger<BackgroundJobService> logger;

    /// <summary>Initializes the service.</summary>
    /// <param name="runner">The job runner.</param>
    /// <param name="logger">Logger.</param>
    public BackgroundJobService(IJobRunner runner, ILogger<BackgroundJobService> logger)
    {
        this.runner = runner;
        this.logger = logger;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public IReadOnlyList<JobProgressSnapshot> ActiveJobs =>
        running.Values.Select(job => job.Latest).OrderBy(job => job.StartedUtc).ToList();

    /// <inheritdoc />
    public async Task<long> StartAsync(
        string jobName, string jobKind, string? payload, string? runKey = null)
    {
        var key = string.IsNullOrWhiteSpace(runKey) ? $"{jobKind}:{jobName}" : runKey;
        var started = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Claimed BEFORE anything is started, so two callers racing on the same key cannot both get
        // past this line. The loser awaits the winner's run id instead of opening a second run:
        // returning the in-flight id is what makes a double-click idempotent rather than an error the
        // user has to interpret.
        var claim = claims.GetOrAdd(key, started);
        if (!ReferenceEquals(claim, started))
        {
            logger.LogInformation(
                "{JobName} ({JobKind}) is already running; the second request joined the run in "
                + "flight instead of starting another",
                jobName,
                jobKind);
            return await claim.Task.ConfigureAwait(false);
        }

        var cancellation = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            try
            {
                var run = await runner.RunOnceAsync(
                    jobName,
                    jobKind,
                    payload,
                    snapshot => Track(snapshot, cancellation, started),
                    cancellation.Token).ConfigureAwait(false);

                // A handler that never reported progress still produced a run row, and the caller is
                // waiting on its id.
                started.TrySetResult(run.ScheduleRunId);
                Release(run.ScheduleRunId);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background job {JobName} ({JobKind}) failed to start", jobName, jobKind);
                started.TrySetException(exception);
            }
            finally
            {
                // Released only when the run is over, and only if this claim is still the current
                // one — the pair overload cannot remove a claim a later start has already taken.
                claims.TryRemove(new KeyValuePair<string, TaskCompletionSource<long>>(key, started));
                cancellation.Dispose();
                Changed?.Invoke();
            }
        });

        return await started.Task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ScheduleRun?> RunScheduleAsync(Schedule schedule, RunTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (IsRunning(schedule.ScheduleId))
        {
            logger.LogInformation(
                "Skipping {JobName}: the previous run has not finished", schedule.Name);
            return null;
        }

        using var cancellation = new CancellationTokenSource();
        try
        {
            var run = await runner.RunScheduleAsync(
                schedule,
                trigger,
                snapshot => Track(snapshot, cancellation, null),
                cancellation.Token).ConfigureAwait(false);
            Release(run.ScheduleRunId);
            return run;
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    /// <inheritdoc />
    public bool IsRunning(long scheduleId) =>
        running.Values.Any(job => job.Latest.ScheduleId == scheduleId);

    /// <inheritdoc />
    public bool Cancel(long scheduleRunId)
    {
        if (!running.TryGetValue(scheduleRunId, out var job))
        {
            return false;
        }

        job.Cancellation.Cancel();
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var job in running.Values)
        {
            job.Cancellation.Cancel();
        }

        running.Clear();
    }

    private void Track(
        JobProgressSnapshot snapshot,
        CancellationTokenSource cancellation,
        TaskCompletionSource<long>? started)
    {
        running[snapshot.ScheduleRunId] = new RunningJob(snapshot, cancellation);
        started?.TrySetResult(snapshot.ScheduleRunId);
        Changed?.Invoke();
    }

    private void Release(long scheduleRunId) => running.TryRemove(scheduleRunId, out _);

    private sealed record RunningJob(JobProgressSnapshot Latest, CancellationTokenSource Cancellation);
}
