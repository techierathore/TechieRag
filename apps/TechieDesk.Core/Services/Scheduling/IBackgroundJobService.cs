namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Starts jobs in the background and exposes their live progress (REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <b>This is the API the connector screen calls.</b> A connector sync started by hand and one
/// started by a schedule are the same thing running through the same runner, writing the same run
/// history and the same per-item failure reasons; only <see cref="RunTrigger"/> differs. Anything
/// that wants a connector run to be visible while it happens starts it here.
/// </remarks>
public interface IBackgroundJobService
{
    /// <summary>Raised whenever a job starts, reports progress, or finishes.</summary>
    /// <remarks>
    /// Raised on the reporting handler's thread. A Blazor consumer must marshal onto its own
    /// dispatcher before touching component state.
    /// </remarks>
    event Action? Changed;

    /// <summary>Gets a snapshot of every job currently running.</summary>
    IReadOnlyList<JobProgressSnapshot> ActiveJobs { get; }

    /// <summary>Starts a job that has no schedule behind it, and returns immediately.</summary>
    /// <param name="jobName">The display name for the run history.</param>
    /// <param name="jobKind">The handler key.</param>
    /// <param name="payload">The handler-specific payload.</param>
    /// <param name="runKey">
    /// What "the same job" means for the in-flight guard, or <see langword="null"/> to key on the
    /// kind and the name.
    /// </param>
    /// <returns>
    /// The opened run's key, once the run row exists — or the key of the run ALREADY in flight under
    /// the same <paramref name="runKey"/>.
    /// </returns>
    /// <remarks>
    /// <para><b>Hand-started runs are guarded too, which they were not.</b>
    /// <see cref="IsRunning(long)"/> keys on the schedule, and a hand-started run has no schedule, so
    /// double-clicking "Sync now" used to start two concurrent walks of the same source — two
    /// listings, two sets of fetches against the same rate limit, and every changed item ingested
    /// twice because neither run could see the other's sync state. The second caller now attaches to
    /// the first run rather than starting a second one.</para>
    /// <para>The caller supplies the key because only the caller knows what identity means for its
    /// job: two connector runs are "the same" when they name the same saved connector, whatever
    /// workspace each was aimed at.</para>
    /// </remarks>
    Task<long> StartAsync(string jobName, string jobKind, string? payload, string? runKey = null);

    /// <summary>Starts a saved schedule immediately, and returns once it has finished.</summary>
    /// <param name="schedule">The schedule to run.</param>
    /// <param name="trigger">Why it is running.</param>
    /// <returns>The completed run, or <see langword="null"/> when the job was already running.</returns>
    Task<ScheduleRun?> RunScheduleAsync(Schedule schedule, RunTrigger trigger);

    /// <summary>Determines whether a schedule already has a run in flight.</summary>
    /// <param name="scheduleId">The schedule key.</param>
    /// <returns><see langword="true"/> when a run of that schedule is in flight.</returns>
    bool IsRunning(long scheduleId);

    /// <summary>Requests cancellation of a running job.</summary>
    /// <param name="scheduleRunId">The run key.</param>
    /// <returns><see langword="true"/> when a matching run was found and asked to stop.</returns>
    bool Cancel(long scheduleRunId);
}
