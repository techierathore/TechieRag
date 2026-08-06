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

    /// <summary>Counts every event matching the filter, ignoring its paging window.</summary>
    /// <param name="filter">Filter values; <c>Offset</c> and <c>Limit</c> are deliberately not applied.</param>
    /// <returns>The total number of matching events.</returns>
    /// <remarks>
    /// REQ-UI-026: the pagination footer states how many events the current filter matched, which
    /// is a different number from the page length returned by <see cref="QueryAsync"/>.
    /// </remarks>
    Task<int> CountAsync(EventLogFilter filter);

    /// <summary>Reads a single event by primary key.</summary>
    /// <param name="eventLogId">The event identifier.</param>
    /// <returns>The event, or null when no such row exists.</returns>
    Task<EventLog?> GetAsync(long eventLogId);

    /// <summary>Lists every event sharing a correlation id, oldest first.</summary>
    /// <param name="correlationId">The correlation id to group on.</param>
    /// <returns>
    /// The correlated events in the order they happened, or an empty list when the id is blank or
    /// unknown. Oldest first because a correlated group reads as a sequence of steps.
    /// </returns>
    Task<IReadOnlyList<EventLog>> QueryByCorrelationAsync(string? correlationId);

    /// <summary>Lists the distinct categories present in the log.</summary>
    /// <returns>Category names in alphabetical order.</returns>
    /// <remarks>
    /// The category filter offers what the log actually contains. Offering a fixed list would show
    /// the operator categories that can never match anything on their install.
    /// </remarks>
    Task<IReadOnlyList<string>> ListCategoriesAsync();
}
