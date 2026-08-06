namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Executes one job, records it, and classifies how it ended (REQ-FN-028, REQ-FN-020).
/// </summary>
public interface IJobRunner
{
    /// <summary>Lists the registered job kinds, for the authoring dialog's action list (REQ-UI-046).</summary>
    /// <returns>Every handler registered in this process.</returns>
    IReadOnlyList<IScheduledJobHandler> AvailableHandlers { get; }

    /// <summary>Finds a handler by kind.</summary>
    /// <param name="jobKind">The handler key, compared case-insensitively.</param>
    /// <returns>The handler, or <see langword="null"/> when no handler answers to that kind.</returns>
    IScheduledJobHandler? FindHandler(string? jobKind);

    /// <summary>Runs a saved schedule now.</summary>
    /// <param name="schedule">The schedule to run.</param>
    /// <param name="trigger">Why it is running.</param>
    /// <param name="onProgress">Invoked with each progress snapshot. May be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The completed run record.</returns>
    Task<ScheduleRun> RunScheduleAsync(
        Schedule schedule,
        RunTrigger trigger,
        Action<JobProgressSnapshot>? onProgress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a job that has no schedule behind it — a connector sync started by hand (REQ-FN-020).
    /// </summary>
    /// <param name="jobName">The display name for the run history.</param>
    /// <param name="jobKind">The handler key.</param>
    /// <param name="payload">The handler-specific payload.</param>
    /// <param name="onProgress">Invoked with each progress snapshot. May be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The completed run record.</returns>
    Task<ScheduleRun> RunOnceAsync(
        string jobName,
        string jobKind,
        string? payload,
        Action<JobProgressSnapshot>? onProgress,
        CancellationToken cancellationToken);
}
