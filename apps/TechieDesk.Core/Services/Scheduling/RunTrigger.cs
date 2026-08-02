namespace TechieDesk.Services.Scheduling;

/// <summary>What caused a job run to start.</summary>
/// <remarks>
/// Recorded per run because "this ran at 09:14 rather than 07:00" has entirely different meanings for
/// a <see cref="CatchUp"/> and a <see cref="Manual"/> run, and the run history is the only place the
/// difference can be told.
/// </remarks>
public enum RunTrigger
{
    /// <summary>The schedule came due while the scheduler was running.</summary>
    Scheduled = 0,

    /// <summary>The user pressed Run now.</summary>
    Manual = 1,

    /// <summary>An occurrence missed while the app or machine was not running, coalesced into one run.</summary>
    CatchUp = 2,

    /// <summary>Started by another part of the app as a background job — a connector sync (REQ-FN-020).</summary>
    Background = 3
}
