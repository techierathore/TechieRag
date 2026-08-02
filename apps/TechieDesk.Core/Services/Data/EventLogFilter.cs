namespace TechieDesk.Services.Data;

/// <summary>
/// Optional filters for querying <see cref="EventLog"/> rows; null members are ignored.
/// </summary>
public sealed class EventLogFilter
{
    /// <summary>Restrict to a single category.</summary>
    public string? Category { get; set; }

    /// <summary>Restrict to a single actor.</summary>
    public string? Actor { get; set; }

    /// <summary>Only events at or after this UTC instant.</summary>
    public DateTime? From { get; set; }

    /// <summary>Only events at or before this UTC instant.</summary>
    public DateTime? To { get; set; }

    /// <summary>
    /// Case-insensitive substring matched against the event name, actor, source and detail payload.
    /// </summary>
    /// <remarks>
    /// REQ-UI-026: the search box on the event-log screen. Whitespace-only text is treated as no
    /// filter at all rather than as a search for a space, which would match every row.
    /// </remarks>
    public string? SearchText { get; set; }

    /// <summary>Number of matching rows to skip before the page begins. Defaults to 0.</summary>
    public int Offset { get; set; }

    /// <summary>Maximum number of rows returned (newest first). Defaults to 200.</summary>
    public int Limit { get; set; } = 200;
}
