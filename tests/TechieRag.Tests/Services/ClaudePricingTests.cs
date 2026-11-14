using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// REQ-RAG-164 (Sevak TR-RAG-064): the current Claude models carry their list prices, and a model with no
/// price is reported as "price not known", never as a cost of $0.
/// </summary>
/// <remarks>
/// <para>Prices are Anthropic's list prices per million input and output tokens, read on 2026-11-13 from
/// https://platform.claude.com/docs/en/about-claude/pricing (docs.claude.com/en/docs/about-claude/pricing
/// redirects there).</para>
/// <para><b>The defect.</b> The built-in table knew six models, none of them current, and a model it did not
/// know cost $0: Sevak's header read "$0.00 today" after 73 answers on claude-sonnet-5-5, and its spend
/// warning could never fire.</para>
/// </remarks>
public sealed class ClaudePricingTests
{
    /// <summary>
    /// One million input and one million output tokens cost the page's input plus output price, for every
    /// model on the page, under its plain id, a dated id and a Bedrock-style id.
    /// </summary>
    /// <param name="model">The model id.</param>
    /// <param name="input">The page's input price per million tokens.</param>
    /// <param name="output">The page's output price per million tokens.</param>
    [Theory(DisplayName = "REQ-RAG-164 CurrentClaudeModelsCostTheirListPrice")]
    [InlineData("claude-fable-5-2", 8, 40)]
    [InlineData("claude-mythos-5-2", 8, 40)]
    [InlineData("claude-fable-5-1", 10, 50)]
    [InlineData("claude-mythos-5-1", 10, 50)]
    [InlineData("claude-fable-5", 10, 50)]
    [InlineData("claude-mythos-5", 10, 50)]
    [InlineData("claude-opus-5-5", 4, 20)]
    [InlineData("claude-opus-5", 5, 25)]
    [InlineData("claude-opus-4-8", 5, 25)]
    [InlineData("claude-opus-4-7", 5, 25)]
    [InlineData("claude-opus-4-6", 5, 25)]
    [InlineData("claude-opus-4-5", 5, 25)]
    [InlineData("claude-opus-4-5-20251101", 5, 25)]
    [InlineData("claude-opus-4-1", 15, 75)]
    [InlineData("claude-opus-4", 15, 75)]
    [InlineData("claude-opus-4-20250514", 15, 75)]
    [InlineData("claude-sonnet-5-5", 2, 10)]
    [InlineData("claude-sonnet-5", 2, 10)]
    [InlineData("claude-sonnet-4-6", 3, 15)]
    [InlineData("claude-sonnet-4-5", 3, 15)]
    [InlineData("us.anthropic.claude-sonnet-4-5-20250929-v1:0", 3, 15)]
    [InlineData("claude-sonnet-4", 3, 15)]
    [InlineData("claude-haiku-5-5", 0.50, 2.50)] // a 1M-token prompt is over 100,000: the page's long-prompt price
    [InlineData("claude-haiku-4-5", 1, 5)]
    [InlineData("claude-haiku-4-5@20251001", 1, 5)]
    public void CurrentClaudeModelsCostTheirListPrice(string model, double input, double output)
    {
        var tracker = new TokenUsageTracker();
        tracker.RecordUsage(new TokenUsage { ModelName = model, InputTokens = 1_000_000, OutputTokens = 1_000_000, ProviderName = "Anthropic" });

        Assert.Equal((decimal)input + (decimal)output, tracker.GetEstimatedCost());
    }

    /// <summary>Sevak's own repro: one million input and 100,000 output tokens on claude-sonnet-5-5 cost $3.00.</summary>
    [Fact(DisplayName = "REQ-RAG-164 SevakReproCostsThreeDollars")]
    public void SevakReproCostsThreeDollars()
    {
        var tracker = new TokenUsageTracker();
        var usage = new TokenUsage { InputTokens = 1_000_000, OutputTokens = 100_000, ModelName = "claude-sonnet-5-5", ProviderName = "Anthropic" };
        tracker.RecordUsage(usage);

        Assert.Equal(3.00m, usage.EstimatedCostUsd);
    }

    /// <summary>
    /// Claude Haiku 5.5 costs $0.10 / $0.50 for a prompt of up to 100,000 tokens and $0.50 / $2.50 above that,
    /// counting cache reads and writes in the prompt.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-164 HaikuFiveFiveLongPromptCostsMore")]
    public void HaikuFiveFiveLongPromptCostsMore()
    {
        var tracker = new TokenUsageTracker();
        var shortPrompt = new TokenUsage { ModelName = "claude-haiku-5-5", InputTokens = 100_000, OutputTokens = 1_000_000 };
        var longPrompt = new TokenUsage { ModelName = "claude-haiku-5-5", InputTokens = 60_000, CacheReadTokens = 40_001, OutputTokens = 1_000_000 };
        tracker.RecordUsage(shortPrompt);
        tracker.RecordUsage(longPrompt);

        Assert.Equal(0.01m + 0.50m, shortPrompt.EstimatedCostUsd);
        Assert.Equal(0.03m + 2.50m, longPrompt.EstimatedCostUsd);
    }

    /// <summary>
    /// A model the table has no price for comes back as "price not known": the usage says so, the summary
    /// counts it apart from the cost, the event fires, and the budget says its cost side is incomplete.
    /// A free call on a priced model stays a known $0.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-164 UnknownModelIsPriceNotKnownNeverZero")]
    public void UnknownModelIsPriceNotKnownNeverZero()
    {
        var tracker = new TokenUsageTracker();
        tracker.SetBudget(new UsageBudget { MaxCostUsd = 10m });
        TokenUsage? reported = null;
        tracker.OnPriceNotKnown += (_, usage) => reported = usage;

        var unknown = new TokenUsage { ModelName = "claude-opus-6", InputTokens = 1_000, OutputTokens = 500 };
        var priced = new TokenUsage { ModelName = "claude-sonnet-5-5", InputTokens = 1_000_000, OutputTokens = 0 };
        var nothingSpent = new TokenUsage { ModelName = "claude-sonnet-5-5", InputTokens = 0, OutputTokens = 0 };
        tracker.RecordUsage(unknown);
        tracker.RecordUsage(priced);
        tracker.RecordUsage(nothingSpent);

        Assert.False(unknown.IsPriceKnown);
        Assert.Same(unknown, reported);
        Assert.True(priced.IsPriceKnown);
        Assert.True(nothingSpent.IsPriceKnown);
        Assert.Equal(0m, nothingSpent.EstimatedCostUsd);

        var summary = tracker.GetSessionUsage();
        Assert.Equal(2.00m, summary.TotalEstimatedCostUsd);
        Assert.Equal(1, summary.UnpricedOperationCount);
        Assert.Equal(1_500, summary.UnpricedTokens);
        Assert.Equal(["claude-opus-6"], summary.UnpricedModels);
        Assert.False(summary.IsCostComplete);
        Assert.True(tracker.GetBudgetStatus()!.IsCostIncomplete);
        Assert.False(tracker.TryGetPricing("claude-opus-6", out _));
        Assert.True(tracker.TryGetPricing("claude-opus-5-5", out var opus));
        Assert.Equal(4m, opus!.InputPerMillionUsd);
    }

    /// <summary>
    /// A newer version of a priced family is not priced as the older one: claude-opus-5 does not price
    /// claude-opus-5-7, and claude-sonnet-4 does not price claude-sonnet-4-9. A date or a revision is the
    /// same model.
    /// </summary>
    /// <param name="model">The model id.</param>
    /// <param name="known">Whether it has a price.</param>
    [Theory(DisplayName = "REQ-RAG-164 NewerVersionIsNotPricedAsOlder")]
    [InlineData("claude-opus-5-7", false)]
    [InlineData("claude-sonnet-4-9", false)]
    [InlineData("claude-haiku-5-10", false)]
    [InlineData("claude-3-5-sonnet-20241022", false)]
    [InlineData("claude-opus-5-20260301", true)]
    [InlineData("gpt-4o-2024-11-20", true)]
    [InlineData("gemini-1.5-pro-002", true)]
    public void NewerVersionIsNotPricedAsOlder(string model, bool known)
    {
        var tracker = new TokenUsageTracker();
        var usage = new TokenUsage { ModelName = model, InputTokens = 10, OutputTokens = 10 };
        tracker.RecordUsage(usage);

        Assert.Equal(known, usage.IsPriceKnown);
    }

    /// <summary>The usage log keeps the "price not known" mark, so a range read after a restart still shows it.</summary>
    [Fact(DisplayName = "REQ-RAG-164 UsageLogKeepsPriceNotKnown")]
    public void UsageLogKeepsPriceNotKnown()
    {
        var file = Path.Combine(Path.GetTempPath(), "trprice-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = "Data Source=" + file + ";Pooling=False";
        try
        {
            new TokenUsageTracker(null, new TechieRag.Persistence.SqliteTokenUsageLog(connectionString))
                .RecordUsage(new TokenUsage { ModelName = "some-new-model", InputTokens = 5, OutputTokens = 5 });

            var after = new TokenUsageTracker(null, new TechieRag.Persistence.SqliteTokenUsageLog(connectionString));
            var summary = after.GetUsageBetween(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
            Assert.Equal(1, summary.UnpricedOperationCount);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
