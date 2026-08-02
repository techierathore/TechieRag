namespace TechieDesk.Services.Scheduling.Authoring;

/// <summary>
/// The user's confirmation of an interpreted draft: the exact lines they were shown, echoed back
/// (BRD-140 / REQ-UI-046, ADR-010).
/// </summary>
/// <param name="ReviewedScheduleText">The plain-language schedule sentence the confirm panel displayed.</param>
/// <param name="ReviewedActionSummary">The action summary the confirm panel displayed.</param>
/// <remarks>
/// <b>Why an echo rather than a boolean.</b> ADR-010 requires that nothing saves without an explicit
/// confirm showing the full understood result. A <c>bool isConfirmed</c> would be satisfied by a
/// caller that never rendered the panel — the check would pass while the guarantee failed. Echoing
/// the displayed text makes the guarantee structural: a caller that did not show the resolved
/// schedule cannot produce the value needed to save it, and a caller that showed a <i>stale</i> one
/// (because the cron was edited in Advanced afterwards) fails the comparison and is refused.
/// </remarks>
public sealed record ScheduleConfirmation(string ReviewedScheduleText, string ReviewedActionSummary);

/// <summary>
/// Thrown when a schedule is saved without a confirmation matching what the user was shown.
/// </summary>
public sealed class ScheduleNotConfirmedException : InvalidOperationException
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">What did not match.</param>
    public ScheduleNotConfirmedException(string message)
        : base(message)
    {
    }
}
