namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Everything a handler is given for one run (REQ-FN-028, REQ-FN-020).
/// </summary>
/// <param name="ScheduleRunId">The open run row, so a handler can correlate its own logging.</param>
/// <param name="ScheduleId">The schedule behind the run, or <see langword="null"/> for a hand-started background job.</param>
/// <param name="JobName">The job's display name.</param>
/// <param name="JobKind">The handler key.</param>
/// <param name="Payload">The handler-specific payload, as stored. Opaque to the scheduler.</param>
/// <param name="Trigger">What caused this run.</param>
/// <param name="Progress">Where to report progress and per-item results.</param>
public sealed record JobRunContext(
    long ScheduleRunId,
    long? ScheduleId,
    string JobName,
    string JobKind,
    string? Payload,
    RunTrigger Trigger,
    IJobProgressReporter Progress);
