namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Persistence for saved schedules (REQ-FN-028). Dapper over SQLite; EF Core is banned (ADR-005).
/// </summary>
public interface IScheduleRepository
{
    /// <summary>Lists every schedule, newest first.</summary>
    /// <returns>All schedules.</returns>
    Task<IReadOnlyList<Schedule>> ListAsync();

    /// <summary>Reads one schedule.</summary>
    /// <param name="scheduleId">The schedule key.</param>
    /// <returns>The schedule, or <see langword="null"/> when it does not exist.</returns>
    Task<Schedule?> GetAsync(long scheduleId);

    /// <summary>Inserts a schedule and returns its new key.</summary>
    /// <param name="schedule">The schedule to insert.</param>
    /// <returns>The assigned <see cref="Schedule.ScheduleId"/>.</returns>
    Task<long> CreateAsync(Schedule schedule);

    /// <summary>Updates an existing schedule in full.</summary>
    /// <param name="schedule">The schedule to write back.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task UpdateAsync(Schedule schedule);

    /// <summary>Deletes a schedule. Its run history is kept and detached (BRD-65).</summary>
    /// <param name="scheduleId">The schedule key.</param>
    /// <returns>A task that completes when the row is removed.</returns>
    Task DeleteAsync(long scheduleId);

    /// <summary>Enables or pauses a schedule without touching anything else.</summary>
    /// <param name="scheduleId">The schedule key.</param>
    /// <param name="isEnabled">Whether the schedule should fire.</param>
    /// <param name="nextRunUtc">The recomputed next occurrence, or <see langword="null"/> when paused.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task SetEnabledAsync(long scheduleId, bool isEnabled, DateTime? nextRunUtc);

    /// <summary>Lists enabled schedules whose next occurrence is at or before an instant.</summary>
    /// <param name="asOfUtc">The instant to test against, in UTC.</param>
    /// <returns>Due schedules, oldest due first.</returns>
    Task<IReadOnlyList<Schedule>> ListDueAsync(DateTime asOfUtc);

    /// <summary>Records that a schedule ran, and when it is next due.</summary>
    /// <param name="scheduleId">The schedule key.</param>
    /// <param name="lastRunUtc">When the run started, in UTC.</param>
    /// <param name="nextRunUtc">The next occurrence, in UTC.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task RecordRunAsync(long scheduleId, DateTime lastRunUtc, DateTime? nextRunUtc);
}
