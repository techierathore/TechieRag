namespace TechieDesk.Services.Scheduling;

/// <summary>
/// A point-in-time view of a running job, for the visible progress REQ-FN-020 requires.
/// </summary>
/// <param name="ScheduleRunId">The run this describes.</param>
/// <param name="ScheduleId">The schedule behind it, or <see langword="null"/> for a hand-started job.</param>
/// <param name="JobName">The job's display name.</param>
/// <param name="JobKind">The handler key.</param>
/// <param name="StartedUtc">When the run started.</param>
/// <param name="Processed">Items completed so far.</param>
/// <param name="Failed">Items that failed so far.</param>
/// <param name="Skipped">Items skipped so far.</param>
/// <param name="Total">Total items expected, or <see langword="null"/> when not yet known.</param>
/// <param name="Message">
/// What is happening right now, as codes and arguments. Resolved by whichever screen paints it, so a
/// live progress line reads in the reader's language rather than the handler's (REQ-UI-056).
/// </param>
public sealed record JobProgressSnapshot(
    long ScheduleRunId,
    long? ScheduleId,
    string JobName,
    string JobKind,
    DateTime StartedUtc,
    int Processed,
    int Failed,
    int Skipped,
    int? Total,
    JobMessage? Message)
{
    /// <summary>
    /// Gets completion as a percentage, or <see langword="null"/> when the total is unknown.
    /// </summary>
    /// <remarks>
    /// Null rather than a guess. A progress bar that invents a percentage from an unknown total is a
    /// bar that jumps backwards, and a user learns within one run to stop believing it.
    /// </remarks>
    public double? PercentComplete => Total is > 0
        ? Math.Min(100d, (Processed + Failed + Skipped) * 100d / Total.Value)
        : null;
}
