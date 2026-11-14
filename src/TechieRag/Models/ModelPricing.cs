namespace TechieRag.Models;

/// <summary>
/// Per-model token pricing used for cost estimation.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Externalizes the pricing table so it can be bound from the
/// <c>TechieRag:UsageTracking:Pricing</c> configuration section or set fluently via
/// TechieRagBuilder.WithModelPricing, instead of being hardcoded in the library.</para>
/// <para><b>Matching:</b> TokenUsageTracker matches pricing keys case-insensitively and by
/// substring, so a key of "gpt-4o" also matches "gpt-4o-2024-11-20".</para>
/// </remarks>
public class ModelPricing
{
    /// <summary>Gets or sets the input (prompt) token price per one million tokens in USD.</summary>
    public decimal InputPerMillionUsd { get; set; }

    /// <summary>Gets or sets the output (completion) token price per one million tokens in USD.</summary>
    public decimal OutputPerMillionUsd { get; set; }

    /// <summary>
    /// Gets or sets the prompt length, in tokens, above which the long-prompt prices apply; 0 means the model
    /// has one price whatever the prompt length (REQ-RAG-164).
    /// </summary>
    /// <remarks>
    /// The prompt length is every input token of the request: <see cref="TokenUsage.InputTokens"/> plus cache
    /// reads and cache writes, as Anthropic counts it for Claude Haiku 5.5 (above 100,000 tokens).
    /// </remarks>
    public int LongPromptThresholdTokens { get; set; }

    /// <summary>Gets or sets the input price per one million tokens for a prompt above <see cref="LongPromptThresholdTokens"/>.</summary>
    public decimal LongPromptInputPerMillionUsd { get; set; }

    /// <summary>Gets or sets the output price per one million tokens for a prompt above <see cref="LongPromptThresholdTokens"/>.</summary>
    public decimal LongPromptOutputPerMillionUsd { get; set; }
}
