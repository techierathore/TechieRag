namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Persistence for run history and per-item results (BRD-93, BRD-65).
/// </summary>
public interface IScheduleRunRepository
{
    /// <summary>Opens a run row in the <see cref="RunOutcome.Running"/> state and returns its key.</summary>
    /// <param name="run">The run to open. Its key is assigned on return.</param>
    /// <returns>The assigned <see cref="ScheduleRun.ScheduleRunId"/>.</returns>
    Task<long> StartAsync(ScheduleRun run);

    /// <summary>Writes the final counts and outcome onto an open run.</summary>
    /// <param name="run">The run to close.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task CompleteAsync(ScheduleRun run);

    /// <summary>Appends per-item results to a run.</summary>
    /// <param name="scheduleRunId">The owning run.</param>
    /// <param name="items">The items to record.</param>
    /// <returns>A task that completes when the rows are written.</returns>
    Task AddItemsAsync(long scheduleRunId, IReadOnlyList<ScheduleRunItem> items);

    /// <summary>Lists the most recent runs across every job, newest first.</summary>
    /// <param name="limit">Maximum rows to return.</param>
    /// <returns>Recent runs.</returns>
    Task<IReadOnlyList<ScheduleRun>> ListRecentAsync(int limit);

    /// <summary>Lists the most recent runs of one schedule, newest first.</summary>
    /// <param name="scheduleId">The schedule key.</param>
    /// <param name="limit">Maximum rows to return.</param>
    /// <returns>Recent runs of that schedule.</returns>
    Task<IReadOnlyList<ScheduleRun>> ListForScheduleAsync(long scheduleId, int limit);

    /// <summary>Reads one run's per-item results.</summary>
    /// <param name="scheduleRunId">The run key.</param>
    /// <returns>Items recorded for that run, failures first.</returns>
    Task<IReadOnlyList<ScheduleRunItem>> ListItemsAsync(long scheduleRunId);

    /// <summary>
    /// Closes any run still marked <see cref="RunOutcome.Running"/>, which can only mean the process
    /// died mid-run.
    /// </summary>
    /// <param name="reason">The failure reason to record against those runs.</param>
    /// <param name="asOfUtc">The completion instant to stamp.</param>
    /// <returns>How many stale runs were closed.</returns>
    /// <remarks>
    /// Without this, a crash or a force-quit leaves a run that reads as in-flight forever, and the
    /// scheduler's "already running" guard would refuse to start that job again — a single crash
    /// would silently disable an automation for good.
    /// </remarks>
    Task<int> CloseAbandonedRunsAsync(JobMessage reason, DateTime asOfUtc);
}
