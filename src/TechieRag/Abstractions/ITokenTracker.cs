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

    /// <summary>
    /// Gets the usage recorded from <paramref name="fromUtc"/> (inclusive) to <paramref name="toUtc"/> (exclusive)
    /// (REQ-RAG-162, Sevak TR-RAG-059).
    /// </summary>
    /// <param name="fromUtc">The start, in UTC.</param>
    /// <param name="toUtc">The end, in UTC.</param>
    /// <returns>The summary of that range; with a usage log it includes usage from before a restart.</returns>
    /// <exception cref="NotSupportedException">This tracker does not keep usage by time.</exception>
    TokenUsageSummary GetUsageBetween(DateTime fromUtc, DateTime toUtc) =>
        throw new NotSupportedException($"{GetType().Name} does not keep usage by time; use TokenUsageTracker.");

    /// <summary>
    /// Gets the usage of each calendar day from <paramref name="firstDay"/> to <paramref name="lastDay"/>, both
    /// included, with days counted in <paramref name="timeZone"/> (REQ-RAG-162).
    /// </summary>
    /// <param name="firstDay">The first day.</param>
    /// <param name="lastDay">The last day.</param>
    /// <param name="timeZone">The time zone a day is counted in; null for this machine's.</param>
    /// <returns>One entry per day, days without usage included with zero totals.</returns>
    /// <exception cref="NotSupportedException">This tracker does not keep usage by time.</exception>
    IReadOnlyDictionary<DateOnly, TokenUsageSummary> GetUsageByDay(DateOnly firstDay, DateOnly lastDay, TimeZoneInfo? timeZone = null) =>
        throw new NotSupportedException($"{GetType().Name} does not keep usage by time; use TokenUsageTracker.");

    /// <summary>
    /// Gets the usage of each calendar month from the month of <paramref name="firstDay"/> to the month of
    /// <paramref name="lastDay"/>, with days counted in <paramref name="timeZone"/> (REQ-RAG-162).
    /// </summary>
    /// <param name="firstDay">A day in the first month.</param>
    /// <param name="lastDay">A day in the last month.</param>
    /// <param name="timeZone">The time zone a day is counted in; null for this machine's.</param>
    /// <returns>One entry per month, keyed by its first day, months without usage included.</returns>
    /// <exception cref="NotSupportedException">This tracker does not keep usage by time.</exception>
    IReadOnlyDictionary<DateOnly, TokenUsageSummary> GetUsageByMonth(DateOnly firstDay, DateOnly lastDay, TimeZoneInfo? timeZone = null) =>
        throw new NotSupportedException($"{GetType().Name} does not keep usage by time; use TokenUsageTracker.");

    /// <summary>Event raised when usage exceeds the configured budget alert threshold.</summary>
    event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert;

    /// <summary>Event raised after each token usage is recorded.</summary>
    event EventHandler<TokenUsage>? OnUsageRecorded;
}
