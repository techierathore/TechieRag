namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Where a running handler reports what it is doing (REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <para>Two separate channels, on purpose. <see cref="Report"/> drives the visible progress bar and
/// is allowed to be lossy — the UI only ever renders the latest value. <see cref="RecordItem"/> is
/// the per-item audit trail and is not lossy for failures: BRD-65 requires per-item results and
/// per-item failure reasons, and a progress bar cannot carry either.</para>
/// <para>Implementations must be safe to call from the handler's own thread while the UI reads the
/// snapshot from another.</para>
/// </remarks>
public interface IJobProgressReporter
{
    /// <summary>Reports how far along the run is.</summary>
    /// <param name="processed">Items completed so far.</param>
    /// <param name="total">Total items expected, or <see langword="null"/> when the handler cannot know yet.</param>
    /// <param name="message">What is happening right now, as codes and arguments.</param>
    void Report(int processed, int? total, JobMessage? message);

    /// <summary>Records the result of one item.</summary>
    /// <param name="status">Whether the item was processed, failed, or skipped.</param>
    /// <param name="itemId">The source's identifier for the item, so a retry can name it.</param>
    /// <param name="itemName">The human-facing name — a path, a subject line, a page title.</param>
    /// <param name="reason">Why, for a failure or a skip. Must never contain a credential.</param>
    /// <remarks>
    /// The reason is PERSISTED (<see cref="ScheduleRunItem.Reason"/>), which is why it is a
    /// <see cref="JobMessage"/> rather than a sentence: it is read back long after the run, by a
    /// reader who may have changed language since (REQ-UI-056).
    /// </remarks>
    void RecordItem(RunItemStatus status, string itemId, string itemName, JobMessage? reason = null);
}
