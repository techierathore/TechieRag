namespace TechieDesk.Services.Scheduling;

/// <summary>
/// What a handler observed. The <see cref="JobRunner"/> — not the handler — turns this into a
/// <see cref="RunOutcome"/> (BRD-65).
/// </summary>
/// <param name="Detail">A one-line summary for the run history ("14 new · 0 failed"), or null to have one composed.</param>
/// <param name="FailureReason">Set only when the run itself could not proceed. Never contains a credential.</param>
public sealed record JobRunResult(string? Detail = null, string? FailureReason = null)
{
    /// <summary>Gets a result for a run that completed with nothing to report.</summary>
    public static JobRunResult Completed { get; } = new();

    /// <summary>Creates a result for a run that could not proceed at all.</summary>
    /// <param name="reason">Why, in terms an operator can act on.</param>
    /// <returns>The failed result.</returns>
    public static JobRunResult Failed(string reason) => new(null, reason);
}
