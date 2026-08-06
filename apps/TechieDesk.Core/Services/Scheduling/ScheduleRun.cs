namespace TechieDesk.Services.Scheduling;

/// <summary>
/// One execution of a job — scheduled, manual, caught up, or started as a background job
/// (BRD-93 run history; BRD-65 result shape).
/// </summary>
public sealed class ScheduleRun
{
    /// <summary>Gets or sets the surrogate key. Zero until the row is inserted.</summary>
    public long ScheduleRunId { get; set; }

    /// <summary>
    /// Gets or sets the schedule this run came from, or <see langword="null"/> for a background job
    /// started by hand (REQ-FN-020) or for a run whose schedule has since been deleted.
    /// </summary>
    public long? ScheduleId { get; set; }

    /// <summary>Gets or sets the job name as displayed, captured at run time so history survives a rename.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>Gets or sets the handler key that ran.</summary>
    public string JobKind { get; set; } = string.Empty;

    /// <summary>Gets or sets what caused this run.</summary>
    public RunTrigger TriggerKind { get; set; }

    /// <summary>Gets or sets when the run started, in UTC.</summary>
    public DateTime StartedUtc { get; set; }

    /// <summary>Gets or sets when the run finished, in UTC. Null while it is in flight.</summary>
    public DateTime? CompletedUtc { get; set; }

    /// <summary>Gets or sets how the run ended.</summary>
    public RunOutcome Outcome { get; set; }

    /// <summary>Gets or sets how many items the run processed successfully.</summary>
    public int ItemsProcessed { get; set; }

    /// <summary>Gets or sets how many items failed. Each has a reason on <see cref="ScheduleRunItem"/>.</summary>
    public int ItemsFailed { get; set; }

    /// <summary>Gets or sets how many items were skipped as unchanged or ineligible.</summary>
    public int ItemsSkipped { get; set; }

    /// <summary>Gets or sets the run's one-line summary ("14 new · 0 failed"), in English.</summary>
    /// <remarks>
    /// Read only through <see cref="JobMessage.Render"/>, paired with <see cref="DetailJson"/>. On a
    /// row this build wrote it is an audit copy of the coded form; on a row written before
    /// REQ-UI-056 it is the only form there is, and printing it verbatim is the whole point.
    /// </remarks>
    public string? Detail { get; set; }

    /// <summary>
    /// Gets or sets <see cref="Detail"/> as resource codes and their arguments, or
    /// <see langword="null"/> for a legacy row (REQ-UI-056).
    /// </summary>
    public string? DetailJson { get; set; }

    /// <summary>Gets or sets why the run itself failed, in operator terms. Never contains a credential.</summary>
    /// <remarks>Rendered through <see cref="JobMessage.Render"/> with <see cref="FailureReasonJson"/>.</remarks>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Gets or sets <see cref="FailureReason"/> as resource codes and their arguments, or
    /// <see langword="null"/> when the reason was not ours to phrase — a library exception, an OS
    /// error — as well as for a legacy row (REQ-UI-056).
    /// </summary>
    public string? FailureReasonJson { get; set; }

    /// <summary>Gets the run duration, or <see langword="null"/> while the run is in flight.</summary>
    public TimeSpan? Duration => CompletedUtc is { } completed ? completed - StartedUtc : null;
}
