namespace TechieDesk.Services.Scheduling;

/// <summary>Status of a single item inside a run.</summary>
public enum RunItemStatus
{
    /// <summary>The item was handled.</summary>
    Processed = 0,

    /// <summary>The item could not be handled; <see cref="ScheduleRunItem.Reason"/> says why.</summary>
    Failed = 1,

    /// <summary>The item was deliberately not handled — unchanged since the last run, or ineligible.</summary>
    Skipped = 2
}

/// <summary>
/// One item a run touched, with the reason when it did not succeed (BRD-65).
/// </summary>
/// <remarks>
/// <b>Failures are always recorded; successes are sampled.</b> A crawl of 20,000 pages must not write
/// 20,000 rows per run, but every failure must be nameable or the run history cannot answer "which
/// ones, and why" — the question BRD-65 exists to answer. The cap is applied by
/// <see cref="JobRunner"/>, and a run that hit it says so in its detail line rather than truncating
/// silently.
/// </remarks>
public sealed class ScheduleRunItem
{
    /// <summary>Gets or sets the surrogate key. Zero until the row is inserted.</summary>
    public long ScheduleRunItemId { get; set; }

    /// <summary>Gets or sets the owning run.</summary>
    public long ScheduleRunId { get; set; }

    /// <summary>Gets or sets the source's identifier for the item, so a retry can name it.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-facing item name — a path, a subject line, a page title.</summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>Gets or sets what happened to the item.</summary>
    public RunItemStatus Status { get; set; }

    /// <summary>Gets or sets why, in operator terms. Never contains a credential.</summary>
    public string? Reason { get; set; }

    /// <summary>Gets or sets when the item was recorded, in UTC.</summary>
    public DateTime RecordedUtc { get; set; }
}
