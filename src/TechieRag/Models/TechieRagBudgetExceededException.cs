namespace TechieRag.Models;

/// <summary>Thrown when a call would exceed the session's token or cost budget.</summary>
/// <remarks>
/// The budget is checked before the next LLM call is issued, so no provider request is sent once the
/// budget is spent. The exception carries the budget and the current usage so the host can report what
/// was spent and what the limit was.
/// </remarks>
public class TechieRagBudgetExceededException : InvalidOperationException
{
    /// <summary>Creates a new budget-exceeded exception.</summary>
    /// <param name="message">The error message.</param>
    public TechieRagBudgetExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Gets or sets the budget that was exceeded.</summary>
    public UsageBudget? Budget { get; init; }

    /// <summary>Gets or sets the usage at the time the call was refused.</summary>
    public TokenUsageSummary? CurrentUsage { get; init; }

    /// <summary>Gets or sets the caller tag of the call that was refused, if any.</summary>
    public string? CallerTag { get; init; }
}
