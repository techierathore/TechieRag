using TechieDesk.Services.Scheduling.Authoring;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// The application-facing surface of the Automations screen: list, create, pause, run and delete
/// schedules (REQ-FN-028, REQ-UI-046).
/// </summary>
public interface IScheduleService
{
    /// <summary>Lists every schedule.</summary>
    /// <returns>All schedules, newest first.</returns>
    Task<IReadOnlyList<Schedule>> ListAsync();

    /// <summary>Lists recent runs across every job, for the Run history tab.</summary>
    /// <param name="limit">Maximum rows.</param>
    /// <returns>Recent runs, newest first.</returns>
    Task<IReadOnlyList<ScheduleRun>> ListRecentRunsAsync(int limit = 50);

    /// <summary>Reads one run's per-item results.</summary>
    /// <param name="scheduleRunId">The run key.</param>
    /// <returns>The items, failures first.</returns>
    Task<IReadOnlyList<ScheduleRunItem>> ListRunItemsAsync(long scheduleRunId);

    /// <summary>
    /// Saves a confirmed draft as a schedule.
    /// </summary>
    /// <param name="draft">The reviewed draft.</param>
    /// <param name="confirmation">The lines the user was shown, echoed back.</param>
    /// <returns>The saved schedule.</returns>
    /// <exception cref="ScheduleNotConfirmedException">
    /// The confirmation does not match the draft — meaning the user was not shown what is about to be
    /// saved.
    /// </exception>
    Task<Schedule> CreateAsync(ScheduleDraft draft, ScheduleConfirmation confirmation);

    /// <summary>Enables or pauses a schedule.</summary>
    /// <param name="scheduleId">The schedule key.</param>
    /// <param name="isEnabled">Whether it should fire.</param>
    /// <returns>A task that completes when the change is saved.</returns>
    Task SetEnabledAsync(long scheduleId, bool isEnabled);

    /// <summary>Deletes a schedule, keeping its run history.</summary>
    /// <param name="scheduleId">The schedule key.</param>
    /// <returns>A task that completes when the schedule is removed.</returns>
    Task DeleteAsync(long scheduleId);

    /// <summary>Runs a schedule immediately.</summary>
    /// <param name="scheduleId">The schedule key.</param>
    /// <returns>The completed run, or <see langword="null"/> when it was already running or is unknown.</returns>
    Task<ScheduleRun?> RunNowAsync(long scheduleId);
}
