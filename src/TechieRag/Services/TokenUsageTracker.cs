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
    private readonly ConcurrentDictionary<string, ModelPricing> modelPricing = new(StringComparer.OrdinalIgnoreCase);
    private readonly object budgetLock = new();
    private readonly ITokenUsageLog? usageLog;
    private UsageBudget? currentBudget;

    /// <inheritdoc/>
    public event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert;

    /// <inheritdoc/>
    public event EventHandler<TokenUsage>? OnUsageRecorded;

    /// <summary>
    /// Raised when the usage log refuses a record (REQ-RAG-162). The record is still counted in this
    /// session; it is missing from later range queries.
    /// </summary>
    public event EventHandler<Exception>? OnUsageLogFailed;

    /// <summary>
    /// Raised for each recorded usage whose model has no known price (REQ-RAG-164, Sevak TR-RAG-064). The
    /// usage carries <see cref="TokenUsage.IsPriceKnown"/> false and is left out of every cost total; add the
    /// price with <see cref="SetModelPricing(string, decimal, decimal)"/> or <see cref="UsageTrackingConfig.Pricing"/>.
    /// </summary>
    public event EventHandler<TokenUsage>? OnPriceNotKnown;

    /// <summary>
    /// Gets the durable log every recorded usage is written to, or null when usage lives only in this session.
    /// </summary>
    public ITokenUsageLog? UsageLog => usageLog;

    /// <summary>
    /// Creates a new token usage tracker with optional initial configuration.
    /// </summary>
    /// <param name="config">Optional usage tracking configuration. Any entries in
    /// <see cref="UsageTrackingConfig.Pricing"/> override the built-in default pricing,
    /// so the cost table can be maintained entirely in configuration.</param>
    public TokenUsageTracker(UsageTrackingConfig? config = null)
        : this(config, null)
    {
    }

    /// <summary>
    /// Creates a tracker that also writes every usage to a durable log, so usage by day and by month
    /// survives a restart (REQ-RAG-162, Sevak TR-RAG-059).
    /// </summary>
    /// <param name="config">Optional usage tracking configuration.</param>
    /// <param name="usageLog">The durable log; null keeps usage in this session only.</param>
    public TokenUsageTracker(UsageTrackingConfig? config, ITokenUsageLog? usageLog)
    {
        this.usageLog = usageLog;
        InitializeDefaultPricing();

        if (config is not null)
        {
            foreach (var entry in config.Pricing)
            {
                modelPricing[entry.Key] = entry.Value;
            }
        }

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

        // REQ-RAG-164: a cost the host supplied stands; otherwise the price table decides, and a model it
        // has no price for is marked "price not known" rather than priced at $0.
        if (usage.EstimatedCostUsd == 0)
        {
            var pricing = FindPricing(usage.ModelName);
            usage.IsPriceKnown = pricing is not null || usage.TotalTokens == 0;
            usage.EstimatedCostUsd = pricing is null ? 0 : CalculateCost(pricing, usage);
        }

        usageRecords.Add(usage);
        AppendToLog(usage);
        OnUsageRecorded?.Invoke(this, usage);
        if (!usage.IsPriceKnown)
        {
            OnPriceNotKnown?.Invoke(this, usage);
        }

        CheckBudget();
    }

    /// <inheritdoc/>
    public TokenUsageSummary GetSessionUsage() => Summarize(usageRecords.ToArray());

    /// <inheritdoc/>
    /// <remarks>
    /// With a <see cref="UsageLog"/> the range is read from the log, so usage recorded before a restart
    /// counts; without one only this session's usage is in range.
    /// </remarks>
    public TokenUsageSummary GetUsageBetween(DateTime fromUtc, DateTime toUtc) =>
        Summarize(ReadRange(Persistence.RelationalTokenUsageLog.ToUtc(fromUtc), Persistence.RelationalTokenUsageLog.ToUtc(toUtc)));

    /// <inheritdoc/>
    public IReadOnlyDictionary<DateOnly, TokenUsageSummary> GetUsageByDay(DateOnly firstDay, DateOnly lastDay, TimeZoneInfo? timeZone = null)
    {
        if (lastDay < firstDay)
        {
            throw new ArgumentException("The last day is before the first day.", nameof(lastDay));
        }

        var zone = timeZone ?? TimeZoneInfo.Local;
        var byDay = ReadRange(StartOfDayUtc(firstDay, zone), StartOfDayUtc(lastDay.AddDays(1), zone))
            .GroupBy(r => LocalDay(r.Timestamp, zone))
            .ToDictionary(g => g.Key, g => g.ToArray());

        var result = new Dictionary<DateOnly, TokenUsageSummary>();
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            result[day] = Summarize(byDay.TryGetValue(day, out var records) ? records : []);
        }

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<DateOnly, TokenUsageSummary> GetUsageByMonth(DateOnly firstDay, DateOnly lastDay, TimeZoneInfo? timeZone = null)
    {
        if (lastDay < firstDay)
        {
            throw new ArgumentException("The last day is before the first day.", nameof(lastDay));
        }

        var zone = timeZone ?? TimeZoneInfo.Local;
        var firstMonth = new DateOnly(firstDay.Year, firstDay.Month, 1);
        var afterLastMonth = new DateOnly(lastDay.Year, lastDay.Month, 1).AddMonths(1);
        var byMonth = ReadRange(StartOfDayUtc(firstMonth, zone), StartOfDayUtc(afterLastMonth, zone))
            .GroupBy(r =>
            {
                var day = LocalDay(r.Timestamp, zone);
                return new DateOnly(day.Year, day.Month, 1);
            })
            .ToDictionary(g => g.Key, g => g.ToArray());

        var result = new Dictionary<DateOnly, TokenUsageSummary>();
        for (var month = firstMonth; month < afterLastMonth; month = month.AddMonths(1))
        {
            result[month] = Summarize(byMonth.TryGetValue(month, out var records) ? records : []);
        }

        return result;
    }

    /// <summary>Reads the usage in a UTC range, from the log when there is one.</summary>
    /// <param name="fromUtc">The start, inclusive.</param>
    /// <param name="toUtc">The end, exclusive.</param>
    /// <returns>The records.</returns>
    private IReadOnlyList<TokenUsage> ReadRange(DateTime fromUtc, DateTime toUtc)
    {
        if (usageLog is not null)
        {
            return usageLog.Read(fromUtc, toUtc);
        }

        return usageRecords
            .Where(r =>
            {
                var at = Persistence.RelationalTokenUsageLog.ToUtc(r.Timestamp);
                return at >= fromUtc && at < toUtc;
            })
            .ToArray();
    }

    /// <summary>Writes one usage to the log, reporting a failure without failing the model call.</summary>
    /// <param name="usage">The usage.</param>
    private void AppendToLog(TokenUsage usage)
    {
        if (usageLog is null)
        {
            return;
        }

        try
        {
            usageLog.Append(usage);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException or IOException or TimeoutException)
        {
            OnUsageLogFailed?.Invoke(this, ex);
        }
    }

    /// <summary>The UTC instant a calendar day starts in a time zone.</summary>
    /// <param name="day">The day.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The UTC time of its first moment.</returns>
    internal static DateTime StartOfDayUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            // A clock change skipped midnight; the day starts at the first time that exists.
            local = local.AddMinutes(30);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>The calendar day a UTC time falls on in a time zone.</summary>
    /// <param name="timestamp">The time; an unspecified kind is taken as UTC.</param>
    /// <param name="zone">The time zone.</param>
    /// <returns>The day.</returns>
    internal static DateOnly LocalDay(DateTime timestamp, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Persistence.RelationalTokenUsageLog.ToUtc(timestamp), zone));

    /// <summary>Sums a set of usage records.</summary>
    /// <param name="records">The records.</param>
    /// <returns>The summary; zero totals when there are none.</returns>
    private static TokenUsageSummary Summarize(IReadOnlyCollection<TokenUsage> records)
    {
        if (records.Count == 0)
        {
            return new TokenUsageSummary();
        }

        var unpriced = records.Where(r => !r.IsPriceKnown).ToArray();
        return new TokenUsageSummary
        {
            TotalInputTokens = records.Sum(r => (long)r.InputTokens),
            TotalOutputTokens = records.Sum(r => (long)r.OutputTokens),
            TotalReasoningTokens = records.Sum(r => (long)r.ReasoningTokens),
            TotalEstimatedCostUsd = records.Sum(r => r.EstimatedCostUsd),
            OperationCount = records.Count,
            UnpricedOperationCount = unpriced.Length,
            UnpricedTokens = unpriced.Sum(r => (long)r.TotalTokens),
            UnpricedModels = unpriced.Select(r => r.ModelName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            FirstOperationAt = records.Min(r => r.Timestamp),
            LastOperationAt = records.Max(r => r.Timestamp)
        };
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, TokenUsageSummary> GetUsageByModel()
    {
        var records = usageRecords.ToArray();
        var grouped = records.GroupBy(r => r.ModelName);
        return SummarizeByKey(grouped);
    }

    /// <summary>Gets token usage breakdown by route.</summary>
    public IReadOnlyDictionary<string, TokenUsageSummary> GetUsageByRoute()
    {
        var records = usageRecords.ToArray();
        var grouped = records.GroupBy(r => r.Route ?? string.Empty);
        return SummarizeByKey(grouped);
    }

    /// <summary>Gets token usage breakdown by caller tag.</summary>
    public IReadOnlyDictionary<string, TokenUsageSummary> GetUsageByCaller()
    {
        var records = usageRecords.ToArray();
        var grouped = records.GroupBy(r => r.CallerTag ?? string.Empty);
        return SummarizeByKey(grouped);
    }

    private static IReadOnlyDictionary<string, TokenUsageSummary> SummarizeByKey(IEnumerable<IGrouping<string, TokenUsage>> grouped)
    {
        var result = new Dictionary<string, TokenUsageSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in grouped)
        {
            result[group.Key] = Summarize(group.ToArray());
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
    public void ThrowIfBudgetExceeded(string? callerTag = null)
    {
        lock (budgetLock)
        {
            if (currentBudget is null) return;
            if (!currentBudget.BlockOnExceeded) return;

            var status = new BudgetStatus
            {
                Budget = currentBudget,
                CurrentUsage = GetSessionUsage()
            };

            if (!status.IsExceeded) return;

            var scope = string.IsNullOrWhiteSpace(callerTag) ? string.Empty : $" for caller '{callerTag}'";
            throw new TechieRagBudgetExceededException(
                $"The session token budget has been exceeded{scope}. " +
                $"Tokens: {status.CurrentUsage.TotalTokens}/{status.Budget.MaxTotalTokens}, " +
                $"Cost: ${status.CurrentUsage.TotalEstimatedCostUsd:F6}/${status.Budget.MaxCostUsd:F6}.")
            {
                Budget = currentBudget,
                CurrentUsage = status.CurrentUsage,
                CallerTag = callerTag
            };
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
        modelPricing[modelName] = new ModelPricing { InputPerMillionUsd = inputPricePerMillion, OutputPerMillionUsd = outputPricePerMillion };
    }

    /// <summary>
    /// Gets the price the tracker uses for a model id, so a host can tell "priced" from "price not known"
    /// before or after a call (REQ-RAG-164).
    /// </summary>
    /// <param name="modelName">The model id as the provider reports it, for example <c>claude-sonnet-5-5</c>,
    /// <c>claude-opus-4-5-20251101</c> or <c>us.anthropic.claude-sonnet-4-5-20250929-v1:0</c>.</param>
    /// <param name="pricing">The price, or null.</param>
    /// <returns><see langword="true"/> when the model has a price.</returns>
    public bool TryGetPricing(string modelName, out ModelPricing? pricing)
    {
        pricing = FindPricing(modelName);
        return pricing is not null;
    }

    /// <summary>Prices one usage, using the long-prompt price when its prompt is above the model's threshold.</summary>
    /// <param name="pricing">The model's price.</param>
    /// <param name="usage">The usage.</param>
    /// <returns>The cost in USD.</returns>
    internal static decimal CalculateCost(ModelPricing pricing, TokenUsage usage)
    {
        var promptTokens = (long)usage.InputTokens + usage.CacheReadTokens + usage.CacheWriteTokens;
        var longPrompt = pricing.LongPromptThresholdTokens > 0 && promptTokens > pricing.LongPromptThresholdTokens;
        var inputPrice = longPrompt ? pricing.LongPromptInputPerMillionUsd : pricing.InputPerMillionUsd;
        var outputPrice = longPrompt ? pricing.LongPromptOutputPerMillionUsd : pricing.OutputPerMillionUsd;
        return ((decimal)usage.InputTokens / 1_000_000m * inputPrice) + ((decimal)usage.OutputTokens / 1_000_000m * outputPrice);
    }

    /// <summary>
    /// Finds a model's price: an exact key, else the longest key the id contains where the id does not go on
    /// to name another version (REQ-RAG-164).
    /// </summary>
    /// <param name="modelName">The model id.</param>
    /// <returns>The price, or null when no key fits.</returns>
    /// <remarks>
    /// <c>claude-opus-4-5</c> prices <c>claude-opus-4-5-20251101</c>, <c>claude-opus-4-5@20251101</c> and
    /// <c>us.anthropic.claude-opus-4-5-20251101-v1:0</c>, but <c>claude-opus-5</c> never prices
    /// <c>claude-opus-5-7</c>: the text after the key continues the version number, so that is another
    /// model, and its price is not known until it is added. A date (four digits or more after a hyphen) is
    /// not a version.
    /// </remarks>
    private ModelPricing? FindPricing(string modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return null;

        if (modelPricing.TryGetValue(modelName, out var exact))
            return exact;

        ModelPricing? best = null;
        var bestLength = -1;
        foreach (var kvp in modelPricing)
        {
            var at = modelName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase);
            if (at < 0 || kvp.Key.Length <= bestLength)
                continue;

            if (ContinuesVersion(modelName.AsSpan(at + kvp.Key.Length)))
                continue;

            best = kvp.Value;
            bestLength = kvp.Key.Length;
        }

        return best;
    }

    /// <summary>Whether the text after a matched key goes on to name another version of the model.</summary>
    /// <param name="rest">The text after the key.</param>
    /// <returns><see langword="true"/> for <c>-7</c>, <c>-12</c>, <c>.1</c> or a digit; false for a date, a revision such as <c>-002</c>, a suffix or nothing.</returns>
    internal static bool ContinuesVersion(ReadOnlySpan<char> rest)
    {
        if (rest.IsEmpty) return false;
        if (char.IsDigit(rest[0])) return true;
        if (rest[0] == '.' && rest.Length > 1 && char.IsDigit(rest[1])) return true;
        if (rest[0] != '-') return false;

        var digits = 0;
        while (digits + 1 < rest.Length && char.IsDigit(rest[digits + 1]))
        {
            digits++;
        }

        return digits is >= 1 and <= 2;
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

    /// <summary>
    /// Seeds the built-in fallback pricing table. All values can be overridden (and new
    /// models added) via <see cref="UsageTrackingConfig.Pricing"/> or <see cref="SetModelPricing"/>.
    /// </summary>
    /// <remarks>
    /// Claude prices are Anthropic's list prices per million input and output tokens, read on 2026-11-13
    /// from https://platform.claude.com/docs/en/about-claude/pricing (REQ-RAG-164, Sevak TR-RAG-064). Cache
    /// reads and writes are not priced here. Re-read the page when Anthropic adds a model; a model missing
    /// from this table is reported as "price not known", never as $0.
    /// </remarks>
    private void InitializeDefaultPricing()
    {
        SetModelPricing("gpt-4o", 2.50m, 10.00m);
        SetModelPricing("gpt-4o-mini", 0.15m, 0.60m);
        SetModelPricing("gpt-4-turbo", 10.00m, 30.00m);
        SetModelPricing("gemini-2.0-flash", 0.075m, 0.30m);
        SetModelPricing("gemini-1.5-pro", 1.25m, 5.00m);

        SetModelPricing("claude-fable-5-2", 8m, 40m);
        SetModelPricing("claude-mythos-5-2", 8m, 40m);
        SetModelPricing("claude-fable-5-1", 10m, 50m);
        SetModelPricing("claude-mythos-5-1", 10m, 50m);
        SetModelPricing("claude-fable-5", 10m, 50m);
        SetModelPricing("claude-mythos-5", 10m, 50m);
        SetModelPricing("claude-opus-5-5", 4m, 20m);
        SetModelPricing("claude-opus-5", 5m, 25m);
        SetModelPricing("claude-opus-4-8", 5m, 25m);
        SetModelPricing("claude-opus-4-7", 5m, 25m);
        SetModelPricing("claude-opus-4-6", 5m, 25m);
        SetModelPricing("claude-opus-4-5", 5m, 25m);
        SetModelPricing("claude-opus-4-1", 15m, 75m);
        SetModelPricing("claude-opus-4", 15m, 75m);
        SetModelPricing("claude-sonnet-5-5", 2m, 10m);
        SetModelPricing("claude-sonnet-5", 2m, 10m);
        SetModelPricing("claude-sonnet-4-6", 3m, 15m);
        SetModelPricing("claude-sonnet-4-5", 3m, 15m);
        SetModelPricing("claude-sonnet-4", 3m, 15m);
        modelPricing["claude-haiku-5-5"] = new ModelPricing
        {
            InputPerMillionUsd = 0.10m,
            OutputPerMillionUsd = 0.50m,
            LongPromptThresholdTokens = 100_000,
            LongPromptInputPerMillionUsd = 0.50m,
            LongPromptOutputPerMillionUsd = 2.50m
        };
        SetModelPricing("claude-haiku-4-5", 1m, 5m);
    }
}
