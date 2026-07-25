namespace TechieDesk.Services.Data;

/// <summary>
/// Append and query access to the audit <see cref="EventLog"/> (Dapper-only, BRD-102).
/// </summary>
public interface IEventLogRepository
{
    /// <summary>Appends an event and returns its new primary key.</summary>
    /// <param name="eventLog">The event to append; <c>OccurredAt</c> defaults to now (UTC) when unset.</param>
    /// <returns>The generated <c>EventLogId</c>.</returns>
    Task<long> AppendAsync(EventLog eventLog);

    /// <summary>Queries events with optional filters, newest first.</summary>
    /// <param name="filter">Filter values; null members are ignored.</param>
    /// <returns>Matching events ordered by <c>OccurredAt</c> descending.</returns>
    Task<IReadOnlyList<EventLog>> QueryAsync(EventLogFilter filter);
}
