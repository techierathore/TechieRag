using System.Collections.Concurrent;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Tracks token usage and manages usage budgets.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Central service for recording, aggregating, and monitoring
/// token consumption across all LLM operations. Supports cost estimation
/// and budget alerting.</para>
/// <para><b>Code Flow:</b> Automatically subscribed to ILlmProvider.OnCompletionCompleted events.
/// Applications can query GetSessionUsage() and GetEstimatedCost() for monitoring.</para>
/// </remarks>
public class TokenUsageTracker : ITokenTracker
{
    private readonly ConcurrentBag<TokenUsage> usageRecords = new();
    private readonly ConcurrentDictionary<string, (decimal InputPricePerMillion, decimal OutputPricePerMillion)> modelPricing = new(StringComparer.OrdinalIgnoreCase);
    private readonly object budgetLock = new();
    private UsageBudget? currentBudget;

    /// <inheritdoc/>
    public event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert;

    /// <inheritdoc/>
    public event EventHandler<TokenUsage>? OnUsageRecorded;

    /// <summary>
    /// Creates a new token usage tracker with optional initial configuration.
    /// </summary>
    /// <param name="config">Optional usage tracking configuration.</param>
    public TokenUsageTracker(UsageTrackingConfig? config = null)
    {
        InitializeDefaultPricing();

        if (config is not null && (config.MaxTotalTokens > 0 || config.MaxCostUsd > 0))
        {
            SetBudget(new UsageBudget
            {
                MaxTotalTokens = config.MaxTotalTokens,
                MaxCostUsd = config.MaxCostUsd,
                AlertThreshold = config.AlertThreshold,
                BlockOnExceeded = config.BlockOnExceeded
            });
        }
    }

    /// <inheritdoc/>
    public void RecordUsage(TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (usage.EstimatedCostUsd == 0)
        {
            usage.EstimatedCostUsd = CalculateCost(usage.ModelName, usage.InputTokens, usage.OutputTokens);
        }

        usageRecords.Add(usage);
        OnUsageRecorded?.Invoke(this, usage);

        CheckBudget();
    }

    /// <inheritdoc/>
    public TokenUsageSummary GetSessionUsage()
    {
        var records = usageRecords.ToArray();
        if (records.Length == 0)
        {
            return new TokenUsageSummary();
        }

        return new TokenUsageSummary
        {
            TotalInputTokens = records.Sum(r => (long)r.InputTokens),
            TotalOutputTokens = records.Sum(r => (long)r.OutputTokens),
            TotalEstimatedCostUsd = records.Sum(r => r.EstimatedCostUsd),
            OperationCount = records.Length,
            FirstOperationAt = records.Min(r => r.Timestamp),
            LastOperationAt = records.Max(r => r.Timestamp)
        };
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, TokenUsageSummary> GetUsageByModel()
    {
        var records = usageRecords.ToArray();
        var grouped = records.GroupBy(r => r.ModelName);
        var result = new Dictionary<string, TokenUsageSummary>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var groupRecords = group.ToArray();
            result[group.Key] = new TokenUsageSummary
            {
                TotalInputTokens = groupRecords.Sum(r => (long)r.InputTokens),
                TotalOutputTokens = groupRecords.Sum(r => (long)r.OutputTokens),
                TotalEstimatedCostUsd = groupRecords.Sum(r => r.EstimatedCostUsd),
                OperationCount = groupRecords.Length,
                FirstOperationAt = groupRecords.Min(r => r.Timestamp),
                LastOperationAt = groupRecords.Max(r => r.Timestamp)
            };
        }

        return result;
    }

    /// <inheritdoc/>
    public decimal GetEstimatedCost()
    {
        return usageRecords.Sum(r => r.EstimatedCostUsd);
    }

    /// <inheritdoc/>
    public void SetBudget(UsageBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        lock (budgetLock)
        {
            currentBudget = budget;
        }
    }

    /// <inheritdoc/>
    public BudgetStatus? GetBudgetStatus()
    {
        lock (budgetLock)
        {
            if (currentBudget is null) return null;

            return new BudgetStatus
            {
                Budget = currentBudget,
                CurrentUsage = GetSessionUsage()
            };
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        while (usageRecords.TryTake(out _)) { }
    }

    /// <summary>
    /// Sets custom pricing for a model.
    /// </summary>
    /// <param name="modelName">The model name (case-insensitive matching).</param>
    /// <param name="inputPricePerMillion">Input token price per 1M tokens in USD.</param>
    /// <param name="outputPricePerMillion">Output token price per 1M tokens in USD.</param>
    public void SetModelPricing(string modelName, decimal inputPricePerMillion, decimal outputPricePerMillion)
    {
        modelPricing[modelName] = (inputPricePerMillion, outputPricePerMillion);
    }

    private decimal CalculateCost(string modelName, int inputTokens, int outputTokens)
    {
        if (string.IsNullOrEmpty(modelName)) return 0;

        var pricing = FindPricing(modelName);
        if (pricing is null) return 0;

        var inputCost = (decimal)inputTokens / 1_000_000m * pricing.Value.InputPricePerMillion;
        var outputCost = (decimal)outputTokens / 1_000_000m * pricing.Value.OutputPricePerMillion;
        return inputCost + outputCost;
    }

    private (decimal InputPricePerMillion, decimal OutputPricePerMillion)? FindPricing(string modelName)
    {
        if (modelPricing.TryGetValue(modelName, out var exact))
            return exact;

        foreach (var kvp in modelPricing)
        {
            if (modelName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return null;
    }

    private void CheckBudget()
    {
        lock (budgetLock)
        {
            if (currentBudget is null) return;

            var status = new BudgetStatus
            {
                Budget = currentBudget,
                CurrentUsage = GetSessionUsage()
            };

            if (status.IsExceeded)
            {
                OnBudgetAlert?.Invoke(this, new BudgetAlertEventArgs { Status = status, IsExceeded = true });
            }
            else if (status.IsAlertTriggered)
            {
                OnBudgetAlert?.Invoke(this, new BudgetAlertEventArgs { Status = status, IsExceeded = false });
            }
        }
    }

    private void InitializeDefaultPricing()
    {
        modelPricing["gpt-4o"] = (2.50m, 10.00m);
        modelPricing["gpt-4o-mini"] = (0.15m, 0.60m);
        modelPricing["gpt-4-turbo"] = (10.00m, 30.00m);
        modelPricing["claude-opus-4-6"] = (15.00m, 75.00m);
        modelPricing["claude-sonnet-4-5"] = (3.00m, 15.00m);
        modelPricing["claude-haiku-4-5"] = (0.80m, 4.00m);
        modelPricing["gemini-2.0-flash"] = (0.075m, 0.30m);
        modelPricing["gemini-1.5-pro"] = (1.25m, 5.00m);
    }
}
