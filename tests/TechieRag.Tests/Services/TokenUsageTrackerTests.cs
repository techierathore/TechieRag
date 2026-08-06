using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// Unit tests for <see cref="TokenUsageTracker"/> cost math and the externalized pricing
/// table (REQ-RAG-029): default pricing, substring model matching, config-driven pricing
/// overrides, and streamed usage aggregation.
/// </summary>
public class TokenUsageTrackerTests
{
    /// <summary>Cost is computed from the built-in per-million pricing for a known model.</summary>
    [Fact]
    public void CalculatesCostFromDefaultPricing()
    {
        var tracker = new TokenUsageTracker();
        tracker.RecordUsage(new TokenUsage
        {
            ModelName = "gpt-4o",
            InputTokens = 1_000_000,
            OutputTokens = 1_000_000
        });

        // gpt-4o default: 2.50 input + 10.00 output per million.
        Assert.Equal(12.50m, tracker.GetEstimatedCost());
    }

    /// <summary>A pricing key matches a longer, dated model id by substring.</summary>
    [Fact]
    public void MatchesModelPricingBySubstring()
    {
        var tracker = new TokenUsageTracker();
        tracker.RecordUsage(new TokenUsage
        {
            ModelName = "gpt-4o-2024-11-20",
            InputTokens = 2_000_000,
            OutputTokens = 0
        });

        Assert.Equal(5.00m, tracker.GetEstimatedCost());
    }

    /// <summary>Config-supplied pricing overrides the built-in table for the same model.</summary>
    [Fact]
    public void ConfigPricingOverridesDefault()
    {
        var config = new UsageTrackingConfig();
        config.Pricing["gpt-4o"] = new ModelPricing { InputPerMillionUsd = 1.00m, OutputPerMillionUsd = 2.00m };
        var tracker = new TokenUsageTracker(config);

        tracker.RecordUsage(new TokenUsage
        {
            ModelName = "gpt-4o",
            InputTokens = 1_000_000,
            OutputTokens = 1_000_000
        });

        Assert.Equal(3.00m, tracker.GetEstimatedCost());
    }

    /// <summary>Config can add pricing for a model absent from the built-in table.</summary>
    [Fact]
    public void ConfigPricingAddsNewModel()
    {
        var config = new UsageTrackingConfig();
        config.Pricing["my-custom-llm"] = new ModelPricing { InputPerMillionUsd = 4.00m, OutputPerMillionUsd = 8.00m };
        var tracker = new TokenUsageTracker(config);

        tracker.RecordUsage(new TokenUsage
        {
            ModelName = "my-custom-llm",
            InputTokens = 500_000,
            OutputTokens = 250_000
        });

        // 0.5*4 + 0.25*8 = 2 + 2 = 4.
        Assert.Equal(4.00m, tracker.GetEstimatedCost());
    }

    /// <summary>An unknown model yields zero cost rather than throwing.</summary>
    [Fact]
    public void UnknownModelCostsZero()
    {
        var tracker = new TokenUsageTracker();
        tracker.RecordUsage(new TokenUsage { ModelName = "totally-unknown", InputTokens = 1000, OutputTokens = 1000 });
        Assert.Equal(0m, tracker.GetEstimatedCost());
    }

    /// <summary>
    /// Streamed usage records (non-zero input/output) aggregate correctly across operations,
    /// backing the REQ-RAG-029 requirement that streamed responses report non-zero usage.
    /// </summary>
    [Fact]
    public void AggregatesStreamedUsageAcrossOperations()
    {
        var tracker = new TokenUsageTracker();
        tracker.RecordUsage(new TokenUsage { ModelName = "gpt-4o", InputTokens = 100, OutputTokens = 50 });
        tracker.RecordUsage(new TokenUsage { ModelName = "gpt-4o", InputTokens = 200, OutputTokens = 80 });

        var summary = tracker.GetSessionUsage();
        Assert.Equal(2, summary.OperationCount);
        Assert.Equal(300, summary.TotalInputTokens);
        Assert.Equal(130, summary.TotalOutputTokens);
        Assert.Equal(430, summary.TotalTokens);
        Assert.True(summary.TotalEstimatedCostUsd > 0m);
    }
}
