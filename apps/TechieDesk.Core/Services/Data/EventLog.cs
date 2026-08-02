namespace TechieDesk.Services.Data;

/// <summary>
/// Append-only audit/event row for the admin console (BRD-104 P1 schema).
/// </summary>
public sealed class EventLog
{
    /// <summary>Primary key.</summary>
    public long EventLogId { get; set; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Event category (e.g. Auth, Workspace, Ingestion).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>User or system actor that produced the event.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Short event name (e.g. WorkspaceCreated).</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>Optional structured detail payload.</summary>
    public string? Detail { get; set; }

    /// <summary>Optional origin of the event (component or subsystem).</summary>
    public string? Source { get; set; }

    /// <summary>
    /// Optional identifier shared by every event belonging to the same operation.
    /// </summary>
    /// <remarks>
    /// REQ-UI-026: this is what turns a single audit line into a debuggable job — the event-log
    /// Details view lists the siblings that carry the same value. Null means the event was written
    /// on its own, which the UI states rather than presenting an empty group.
    /// </remarks>
    public string? CorrelationId { get; set; }
}
