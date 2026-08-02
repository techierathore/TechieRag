namespace TechieDesk.Services.Scheduling;

/// <summary>
/// The scheduler: one class, hosted either by the app window or by the background helper
/// (REQ-FN-042 / ADR-009).
/// </summary>
/// <remarks>
/// <b>The helper is a hosting choice, not a second implementation.</b> BRD-139 and ADR-009 both say
/// so, and this interface is where that is enforced: whatever process is alive constructs this same
/// service against the same data directory. There is no separate daemon code path to keep in step.
/// </remarks>
public interface ISchedulerService
{
    /// <summary>Gets a value indicating whether the polling loop is running in this process.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Repairs schedule state after a gap, then starts the polling loop.
    /// </summary>
    /// <param name="cancellationToken">Stops the loop.</param>
    /// <returns>A task that completes when the loop stops.</returns>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Recomputes next-run times and closes runs abandoned by a process that died mid-run.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>A task that completes when the pass finishes.</returns>
    Task PrimeAsync(CancellationToken cancellationToken);

    /// <summary>Runs one poll: finds due schedules and executes them.</summary>
    /// <param name="cancellationToken">Cancels the poll.</param>
    /// <returns>The runs started by this poll.</returns>
    Task<IReadOnlyList<ScheduleRun>> PollAsync(CancellationToken cancellationToken);
}
