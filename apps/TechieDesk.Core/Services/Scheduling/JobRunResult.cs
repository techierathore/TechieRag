namespace TechieDesk.Services.Scheduling;

/// <summary>
/// What a handler observed. The <see cref="JobRunner"/> — not the handler — turns this into a
/// <see cref="RunOutcome"/> (BRD-65).
/// </summary>
/// <param name="Detail">
/// A one-line summary for the run history ("14 new · 0 failed"), or null to have one composed.
/// </param>
/// <param name="FailureReason">Set only when the run itself could not proceed. Never contains a credential.</param>
/// <remarks>
/// <b>Both are <see cref="JobMessage"/> and not <see cref="string"/> (REQ-UI-056 / BRD-91).</b> Both
/// end up PERSISTED on <see cref="ScheduleRun"/> and read back months later, possibly by a reader
/// who has since switched language, so a handler that returned a finished English sentence would be
/// writing English into the database forever. The type change is deliberately source-BREAKING rather
/// than an added overload: <c>Failed("Something went wrong.")</c> would otherwise keep compiling and
/// keep persisting English, which is precisely the defect this replaced.
/// </remarks>
public sealed record JobRunResult(JobMessage? Detail = null, JobMessage? FailureReason = null)
{
    /// <summary>Gets a result for a run that completed with nothing to report.</summary>
    public static JobRunResult Completed { get; } = new();

    /// <summary>Creates a result for a run that could not proceed at all.</summary>
    /// <param name="reason">Why, in terms an operator can act on.</param>
    /// <returns>The failed result.</returns>
    public static JobRunResult Failed(JobMessage reason) => new(null, reason);

    /// <summary>Creates a result for a run that could not proceed at all, from a resource code.</summary>
    /// <param name="code">A key present in <c>AppStrings.resx</c>.</param>
    /// <param name="arguments">The values the key's holes take.</param>
    /// <returns>The failed result.</returns>
    public static JobRunResult FailedWith(string code, params object?[] arguments) =>
        new(null, JobMessage.Of(code, arguments));
}
