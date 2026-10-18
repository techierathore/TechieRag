using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for tracking token usage and costs across LLM operations.
/// </summary>
public interface ITokenTracker
{
    /// <summary>Records token usage from an LLM operation.</summary>
    void RecordUsage(TokenUsage usage);

    /// <summary>Gets the cumulative token usage for the current session.</summary>
    TokenUsageSummary GetSessionUsage();

    /// <summary>Gets token usage breakdown by model.</summary>
    IReadOnlyDictionary<string, TokenUsageSummary> GetUsageByModel();

    /// <summary>Gets the estimated cost for the current session.</summary>
    decimal GetEstimatedCost();

    /// <summary>Sets a usage budget with alert threshold.</summary>
    void SetBudget(UsageBudget budget);

    /// <summary>Gets the current budget status.</summary>
    BudgetStatus? GetBudgetStatus();

    /// <summary>Throws when the configured budget is set to block and has already been exceeded.</summary>
    /// <param name="callerTag">Optional caller tag to include in the exception message.</param>
    /// <exception cref="Models.TechieRagBudgetExceededException">The budget is exceeded and blocking is enabled.</exception>
    /// <remarks>Callers should invoke this before issuing an LLM call to prevent spending after the budget is gone.</remarks>
    void ThrowIfBudgetExceeded(string? callerTag = null);

    /// <summary>Resets all tracked usage.</summary>
    void Reset();

    /// <summary>Event raised when usage exceeds the configured budget alert threshold.</summary>
    event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert;

    /// <summary>Event raised after each token usage is recorded.</summary>
    event EventHandler<TokenUsage>? OnUsageRecorded;
}
