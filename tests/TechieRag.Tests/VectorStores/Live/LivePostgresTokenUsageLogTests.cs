using Npgsql;
using TechieRag.Models;
using TechieRag.Persistence;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.VectorStores.Live;

/// <summary>
/// REQ-RAG-162 (Sevak TR-RAG-059) against a real PostgreSQL: usage written by one tracker is read back by a
/// second one on the same database, as after a restart.
/// </summary>
[Trait("Category", LivePostgresFactAttribute.CategoryName)]
public sealed class LivePostgresTokenUsageLogTests
{
    /// <summary>
    /// A second tracker on the same PostgreSQL database reads the first one's usage by day and by month, with
    /// the cost exact to the cent fraction.
    /// </summary>
    [LivePostgresFact(DisplayName = "REQ-RAG-162 PostgresLogKeepsUsageAcrossRestart")]
    public async Task PostgresLogKeepsUsageAcrossRestart()
    {
        var schema = "trusage" + Guid.NewGuid().ToString("N")[..12];
        var connectionString = new NpgsqlConnectionStringBuilder(LivePostgresFactAttribute.ConnectionString!)
        {
            SearchPath = schema,
            Pooling = false
        }.ConnectionString;
        await RunAsync(connectionString, $"CREATE SCHEMA {schema}");

        try
        {
            var before = new TokenUsageTracker(null, new PostgresTokenUsageLog(connectionString));
            before.RecordUsage(new TokenUsage
            {
                Timestamp = new DateTime(2026, 11, 13, 4, 0, 0, DateTimeKind.Utc),
                InputTokens = 1200,
                OutputTokens = 300,
                EstimatedCostUsd = 0.012345m,
                ModelName = "claude-sonnet-5-5",
                ProviderName = "Anthropic",
                CallerTag = "assistant"
            });
            before.RecordUsage(new TokenUsage
            {
                Timestamp = new DateTime(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc),
                InputTokens = 10,
                OutputTokens = 5,
                EstimatedCostUsd = 0.5m,
                ModelName = "m",
                ProviderName = "p"
            });

            var after = new TokenUsageTracker(null, new PostgresTokenUsageLog(connectionString));
            var today = new DateOnly(2026, 11, 13);

            var day = after.GetUsageByDay(today, today, TimeZoneInfo.Utc)[today];
            Assert.Equal(1, day.OperationCount);
            Assert.Equal(1200, day.TotalInputTokens);
            Assert.Equal(0.012345m, day.TotalEstimatedCostUsd);

            var month = after.GetUsageByMonth(today, today, TimeZoneInfo.Utc)[new DateOnly(2026, 11, 1)];
            Assert.Equal(2, month.OperationCount);
            Assert.Equal(0.512345m, month.TotalEstimatedCostUsd);
        }
        finally
        {
            await RunAsync(connectionString, $"DROP SCHEMA IF EXISTS {schema} CASCADE");
        }
    }

    private static async Task RunAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
