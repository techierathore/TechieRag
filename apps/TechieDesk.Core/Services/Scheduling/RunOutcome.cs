namespace TechieDesk.Services.Scheduling;

/// <summary>
/// How a job run ended (BRD-65's run-history shape).
/// </summary>
/// <remarks>
/// <see cref="Partial"/> exists because "some items failed" is neither success nor failure, and
/// collapsing it into either is how per-item data loss becomes invisible. A run that ingested 412 of
/// 500 is not a success.
/// </remarks>
public enum RunOutcome
{
    /// <summary>The run is in flight.</summary>
    Running = 0,

    /// <summary>Every item was processed.</summary>
    Succeeded = 1,

    /// <summary>The run completed, but at least one item failed. Reasons are on the run's items.</summary>
    Partial = 2,

    /// <summary>The run itself failed; the failure reason says why.</summary>
    Failed = 3,

    /// <summary>The run did not happen — a run condition was not met, or it was already running.</summary>
    Skipped = 4,

    /// <summary>The run was cancelled by the user.</summary>
    Cancelled = 5
}
