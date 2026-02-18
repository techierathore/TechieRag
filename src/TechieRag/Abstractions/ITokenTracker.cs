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

    /// <summary>Resets all tracked usage.</summary>
    void Reset();

    /// <summary>Event raised when usage exceeds the configured budget alert threshold.</summary>
    event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert;

    /// <summary>Event raised after each token usage is recorded.</summary>
    event EventHandler<TokenUsage>? OnUsageRecorded;
}
