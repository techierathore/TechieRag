namespace TechieRag.Models;

/// <summary>Token usage metrics from a single LLM operation.</summary>
public class TokenUsage
{
    /// <summary>Gets or sets the number of input/prompt tokens.</summary>
    public int InputTokens { get; set; }

    /// <summary>Gets or sets the number of output/completion tokens.</summary>
    /// <remarks>This includes reasoning tokens when the provider folds them into completion tokens.
    /// See <see cref="ReasoningTokens"/> for the separately reported reasoning portion.</remarks>
    public int OutputTokens { get; set; }

    /// <summary>Gets the total tokens (input + output).</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>Gets or sets prompt tokens served from the provider's cache (REQ-RAG-043 / BRD-124).</summary>
    /// <remarks>Zero when the provider reported nothing, which is not the same as a cache miss — Ollama
    /// and LM Studio have no cache to report on at all. Billed at a discount where the provider caches.</remarks>
    public int CacheReadTokens { get; set; }

    /// <summary>Gets or sets prompt tokens written into the provider's cache (REQ-RAG-043 / BRD-124).</summary>
    /// <remarks>Anthropic bills a cache write at a premium over an ordinary input token, so this is
    /// tracked separately rather than folded into <see cref="InputTokens"/>: a caller comparing the cost
    /// of caching against not caching needs the two apart.</remarks>
    public int CacheWriteTokens { get; set; }

    /// <summary>Gets or sets the number of reasoning tokens the provider reported separately.</summary>
    /// <remarks>Some providers (e.g. Venice/OpenAI-style <c>completion_tokens_details.reasoning_tokens</c>)
    /// bill reasoning separately from visible completion tokens. This figure is included in
    /// <see cref="OutputTokens"/>, not added on top of it, so callers can compute visible completion
    /// tokens as <c>OutputTokens - ReasoningTokens</c>.</remarks>
    public int ReasoningTokens { get; set; }

    /// <summary>Gets or sets whether any token count was estimated because the provider sent none.</summary>
    /// <remarks>When <see langword="true"/>, <see cref="InputTokens"/> and/or <see cref="OutputTokens"/>
    /// were derived from the provider's text rather than its own usage report. The reasoning figure is
    /// never estimated: if the provider did not report it, it is left at zero.</remarks>
    public bool IsEstimated { get; set; }

    /// <summary>Gets or sets the route the call was made on, for example <c>venice/qwen-3-6-plus</c>.</summary>
    /// <remarks>Set from the provider's route when available; null when the provider was created directly.</remarks>
    public string? Route { get; set; }

    /// <summary>Gets or sets a caller-supplied tag that names the agent or component that spent the tokens.</summary>
    /// <remarks>Set via <see cref="LlmCompletionOptions.CallerTag"/> so a host can break down usage by
    /// its own agent name.</remarks>
    public string? CallerTag { get; set; }

    /// <summary>Gets or sets the estimated cost in USD.</summary>
    public decimal EstimatedCostUsd { get; set; }

    /// <summary>Gets or sets the model name.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Gets or sets the provider name.</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Gets or sets the timestamp of this usage record.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>Aggregated token usage summary across multiple operations.</summary>
public class TokenUsageSummary
{
    /// <summary>Gets or sets the total input tokens.</summary>
    public long TotalInputTokens { get; set; }

    /// <summary>Gets or sets the total output tokens.</summary>
    public long TotalOutputTokens { get; set; }

    /// <summary>Gets or sets the total reasoning tokens.</summary>
    /// <remarks>Included in <see cref="TotalOutputTokens"/>, not in addition to it.</remarks>
    public long TotalReasoningTokens { get; set; }

    /// <summary>Gets the total tokens.</summary>
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    /// <summary>Gets the total visible completion tokens (output minus reasoning).</summary>
    public long TotalCompletionTokens => TotalOutputTokens - TotalReasoningTokens;

    /// <summary>Gets or sets the total estimated cost in USD.</summary>
    public decimal TotalEstimatedCostUsd { get; set; }

    /// <summary>Gets or sets the number of operations.</summary>
    public int OperationCount { get; set; }

    /// <summary>Gets or sets the timestamp of the first operation.</summary>
    public DateTime? FirstOperationAt { get; set; }

    /// <summary>Gets or sets the timestamp of the last operation.</summary>
    public DateTime? LastOperationAt { get; set; }
}

/// <summary>Configuration for a usage budget with alert thresholds.</summary>
public class UsageBudget
{
    /// <summary>Gets or sets the maximum total tokens allowed (0 = unlimited).</summary>
    public long MaxTotalTokens { get; set; }

    /// <summary>Gets or sets the maximum cost in USD allowed (0 = unlimited).</summary>
    public decimal MaxCostUsd { get; set; }

    /// <summary>Gets or sets the alert threshold percentage (0.0-1.0).</summary>
    public float AlertThreshold { get; set; } = 0.8f;

    /// <summary>Gets or sets whether to block requests when budget is exceeded.</summary>
    public bool BlockOnExceeded { get; set; }
}

/// <summary>Current status of a usage budget.</summary>
public class BudgetStatus
{
    /// <summary>Gets the configured budget.</summary>
    public required UsageBudget Budget { get; init; }

    /// <summary>Gets the current usage summary.</summary>
    public required TokenUsageSummary CurrentUsage { get; init; }

    /// <summary>Gets the token budget utilization (0.0-1.0).</summary>
    public float TokenUtilization => Budget.MaxTotalTokens > 0
        ? (float)CurrentUsage.TotalTokens / Budget.MaxTotalTokens
        : 0;

    /// <summary>Gets the cost budget utilization (0.0-1.0).</summary>
    public float CostUtilization => Budget.MaxCostUsd > 0
        ? (float)(CurrentUsage.TotalEstimatedCostUsd / Budget.MaxCostUsd)
        : 0;

    /// <summary>Gets whether the budget has been exceeded.</summary>
    public bool IsExceeded =>
        (Budget.MaxTotalTokens > 0 && CurrentUsage.TotalTokens >= Budget.MaxTotalTokens) ||
        (Budget.MaxCostUsd > 0 && CurrentUsage.TotalEstimatedCostUsd >= Budget.MaxCostUsd);

    /// <summary>Gets whether the alert threshold has been reached.</summary>
    public bool IsAlertTriggered => TokenUtilization >= Budget.AlertThreshold || CostUtilization >= Budget.AlertThreshold;
}

/// <summary>Event arguments when budget alert is triggered.</summary>
public class BudgetAlertEventArgs : EventArgs
{
    /// <summary>Gets the current budget status.</summary>
    public required BudgetStatus Status { get; init; }

    /// <summary>Gets whether this is an exceeded alert.</summary>
    public bool IsExceeded { get; init; }
}
