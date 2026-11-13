using Microsoft.Data.Sqlite;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Services;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// REQ-RAG-162 (Sevak TR-RAG-059): token usage by day and by month, kept across a restart.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> <c>ITokenTracker</c> kept an in-memory session only: after a restart "today" and
/// "this month" started again at zero, and a session that ran past midnight counted yesterday as today.</para>
/// <para>Days are counted in a fixed UTC+05:30 zone built for the test, so the result does not depend on the
/// machine's own zone, and a record at 20:00 UTC lands on the next local day.</para>
/// </remarks>
public sealed class TokenUsageByDayTests : IDisposable
{
    private static readonly TimeZoneInfo India = TimeZoneInfo.CreateCustomTimeZone("Test+0530", TimeSpan.FromMinutes(330), "Test+0530", "Test+0530");
    private readonly string folder = Path.Combine(Path.GetTempPath(), "trusage-" + Guid.NewGuid().ToString("N"));
    private readonly string connectionString;

    /// <summary>Initializes a new instance of the <see cref="TokenUsageByDayTests"/> class.</summary>
    public TokenUsageByDayTests()
    {
        Directory.CreateDirectory(folder);
        connectionString = "Data Source=" + Path.Combine(folder, "usage.db") + ";Pooling=False";
    }

    /// <summary>
    /// Usage recorded before a restart is in today's and this month's totals after it, with days and months
    /// counted in the host's zone.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-162 RestartKeepsTodayAndThisMonth")]
    public void RestartKeepsTodayAndThisMonth()
    {
        var before = new TokenUsageTracker(null, new SqliteTokenUsageLog(connectionString));
        before.RecordUsage(Usage(new DateTime(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc), 100, 0.10m)); // 13 Nov 01:30 local
        before.RecordUsage(Usage(new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc), 200, 0.20m)); // 12 Nov
        before.RecordUsage(Usage(new DateTime(2026, 10, 31, 20, 0, 0, DateTimeKind.Utc), 400, 0.40m)); // 1 Nov 01:30 local
        before.RecordUsage(Usage(new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc), 800, 0.80m)); // 31 Oct

        var after = new TokenUsageTracker(null, new SqliteTokenUsageLog(connectionString));
        Assert.Equal(0, after.GetSessionUsage().OperationCount);

        var today = new DateOnly(2026, 11, 13);
        var day = after.GetUsageByDay(today, today, India)[today];
        Assert.Equal(1, day.OperationCount);
        Assert.Equal(0.10m, day.TotalEstimatedCostUsd);

        var months = after.GetUsageByMonth(new DateOnly(2026, 10, 1), today, India);
        Assert.Equal(2, months.Count);
        Assert.Equal(3, months[new DateOnly(2026, 11, 1)].OperationCount);
        Assert.Equal(0.70m, months[new DateOnly(2026, 11, 1)].TotalEstimatedCostUsd);
        Assert.Equal(1, months[new DateOnly(2026, 10, 1)].OperationCount);
    }

    /// <summary>Every day of the asked range is present, the ones without usage at zero.</summary>
    [Fact(DisplayName = "REQ-RAG-162 EveryDayOfRangeIsListed")]
    public void EveryDayOfRangeIsListed()
    {
        var tracker = new TokenUsageTracker(null, new SqliteTokenUsageLog(connectionString));
        tracker.RecordUsage(Usage(new DateTime(2026, 11, 10, 6, 0, 0, DateTimeKind.Utc), 50, 0.05m));

        var days = tracker.GetUsageByDay(new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 12), India);

        Assert.Equal([new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12)], days.Keys.Order());
        Assert.Equal(1, days[new DateOnly(2026, 11, 10)].OperationCount);
        Assert.Equal(0, days[new DateOnly(2026, 11, 11)].OperationCount);
    }

    /// <summary>A UTC range is half-open: a record exactly at its end belongs to the next range.</summary>
    [Fact(DisplayName = "REQ-RAG-162 RangeIsHalfOpen")]
    public void RangeIsHalfOpen()
    {
        var tracker = new TokenUsageTracker(null, new SqliteTokenUsageLog(connectionString));
        var at = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
        tracker.RecordUsage(Usage(at, 10, 0.01m));

        Assert.Equal(0, tracker.GetUsageBetween(at.AddDays(-1), at).OperationCount);
        Assert.Equal(1, tracker.GetUsageBetween(at, at.AddDays(1)).OperationCount);
    }

    /// <summary>
    /// <c>WithPersistence</c> gives the built tracker a usage log in the same database, so a second client
    /// built on that database (a restart) sees the first one's usage.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-162 PersistenceKeepsUsageByDefault")]
    public void PersistenceKeepsUsageByDefault()
    {
        var first = Build(persist: true).GetTokenTracker();
        first.RecordUsage(Usage(DateTime.UtcNow, 300, 0.30m));

        var second = Build(persist: true).GetTokenTracker();
        var summary = second.GetUsageBetween(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        Assert.Equal(1, summary.OperationCount);
        Assert.Equal(300, summary.TotalInputTokens);
    }

    /// <summary>With <c>PersistUsage</c> off, usage stays in the session and no table is created.</summary>
    [Fact(DisplayName = "REQ-RAG-162 PersistUsageOffKeepsSessionOnly")]
    public void PersistUsageOffKeepsSessionOnly()
    {
        var tracker = (TokenUsageTracker)Build(persist: false).GetTokenTracker();
        tracker.RecordUsage(Usage(DateTime.UtcNow, 300, 0.30m));

        Assert.Null(tracker.UsageLog);
        Assert.Equal(1, tracker.GetUsageBetween(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1)).OperationCount);
        Assert.False(TableExists("TrTokenUsage"));
    }

    /// <summary>A log that refuses a write raises <c>OnUsageLogFailed</c> and the model call is not failed.</summary>
    [Fact(DisplayName = "REQ-RAG-162 LogFailureIsReportedNotThrown")]
    public void LogFailureIsReportedNotThrown()
    {
        var tracker = new TokenUsageTracker(null, new RefusingLog());
        Exception? reported = null;
        tracker.OnUsageLogFailed += (_, ex) => reported = ex;

        tracker.RecordUsage(Usage(DateTime.UtcNow, 10, 0.01m));

        Assert.IsType<InvalidOperationException>(reported);
        Assert.Equal(1, tracker.GetSessionUsage().OperationCount);
    }

    /// <summary>A custom tracker that keeps no time says so instead of returning a wrong zero.</summary>
    [Fact(DisplayName = "REQ-RAG-162 CustomTrackerSaysNotSupported")]
    public void CustomTrackerSaysNotSupported()
    {
        ITokenTracker tracker = new SessionOnlyTracker();
        Assert.Throws<NotSupportedException>(() => tracker.GetUsageBetween(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // A file still held on Windows is left in the temp folder.
        }
    }

    private static TokenUsage Usage(DateTime at, int inputTokens, decimal cost) => new()
    {
        Timestamp = at,
        InputTokens = inputTokens,
        OutputTokens = 10,
        EstimatedCostUsd = cost,
        ModelName = "test-model",
        ProviderName = "test"
    };

    private ITechieRag Build(bool persist)
    {
        var builder = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider())
            .WithPersistence(StoreProvider.Sqlite, connectionString);
        if (!persist)
        {
            builder.WithUsageTracking(c => c.PersistUsage = false);
        }

        return builder.Build();
    }

    private bool TableExists(string table)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private sealed class RefusingLog : ITokenUsageLog
    {
        public void Append(TokenUsage usage) => throw new InvalidOperationException("disk full");

        public IReadOnlyList<TokenUsage> Read(DateTime fromUtc, DateTime toUtc) => [];
    }

    private sealed class SessionOnlyTracker : ITokenTracker
    {
        public event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert { add { } remove { } }

        public event EventHandler<TokenUsage>? OnUsageRecorded { add { } remove { } }

        public void RecordUsage(TokenUsage usage)
        {
        }

        public TokenUsageSummary GetSessionUsage() => new();

        public IReadOnlyDictionary<string, TokenUsageSummary> GetUsageByModel() => new Dictionary<string, TokenUsageSummary>();

        public decimal GetEstimatedCost() => 0;

        public void SetBudget(UsageBudget budget)
        {
        }

        public BudgetStatus? GetBudgetStatus() => null;

        public void ThrowIfBudgetExceeded(string? callerTag = null)
        {
        }

        public void Reset()
        {
        }
    }
}
