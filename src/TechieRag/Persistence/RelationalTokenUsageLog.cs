using System.Data;
using System.Data.Common;
using System.Globalization;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Persistence;

/// <summary>
/// The <c>TrTokenUsage</c> table shared by the SQLite and PostgreSQL usage logs (REQ-RAG-162, Sevak TR-RAG-059).
/// </summary>
/// <remarks>
/// <para>Times are stored as fixed-width UTC text (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>), so a range is a
/// plain text comparison on both databases, and the cost as invariant text, so no decimal is rounded
/// through a floating-point column.</para>
/// <para>The table is created on first use.</para>
/// </remarks>
public abstract class RelationalTokenUsageLog : ITokenUsageLog
{
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private readonly object initLock = new();
    private bool initialized;

    /// <summary>Opens a connection to the database.</summary>
    /// <returns>An open connection the caller disposes.</returns>
    protected abstract DbConnection OpenConnection();

    /// <summary>Runs <paramref name="createSchema"/> while other processes are kept from creating the same table.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="createSchema">The schema creation.</param>
    protected virtual void WithSchemaLock(DbConnection connection, Action createSchema) => createSchema();

    /// <inheritdoc/>
    public void Append(TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        EnsureSchema();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TrTokenUsage (UsageId, RecordedAt, ModelName, ProviderName, Route, CallerTag,
                InputTokens, OutputTokens, ReasoningTokens, CacheReadTokens, CacheWriteTokens, IsEstimated, CostUsd, PriceKnown)
            VALUES (@UsageId, @RecordedAt, @ModelName, @ProviderName, @Route, @CallerTag,
                @InputTokens, @OutputTokens, @ReasoningTokens, @CacheReadTokens, @CacheWriteTokens, @IsEstimated, @CostUsd, @PriceKnown)
            """;
        Add(command, "UsageId", Guid.NewGuid().ToString("N"), DbType.String);
        Add(command, "RecordedAt", FormatTime(usage.Timestamp), DbType.String);
        Add(command, "ModelName", usage.ModelName ?? string.Empty, DbType.String);
        Add(command, "ProviderName", usage.ProviderName ?? string.Empty, DbType.String);
        Add(command, "Route", usage.Route, DbType.String);
        Add(command, "CallerTag", usage.CallerTag, DbType.String);
        Add(command, "InputTokens", usage.InputTokens, DbType.Int32);
        Add(command, "OutputTokens", usage.OutputTokens, DbType.Int32);
        Add(command, "ReasoningTokens", usage.ReasoningTokens, DbType.Int32);
        Add(command, "CacheReadTokens", usage.CacheReadTokens, DbType.Int32);
        Add(command, "CacheWriteTokens", usage.CacheWriteTokens, DbType.Int32);
        Add(command, "IsEstimated", usage.IsEstimated ? 1 : 0, DbType.Int32);
        Add(command, "CostUsd", usage.EstimatedCostUsd.ToString(CultureInfo.InvariantCulture), DbType.String);
        Add(command, "PriceKnown", usage.IsPriceKnown ? 1 : 0, DbType.Int32);
        command.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public IReadOnlyList<TokenUsage> Read(DateTime fromUtc, DateTime toUtc)
    {
        EnsureSchema();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT RecordedAt, ModelName, ProviderName, Route, CallerTag, InputTokens, OutputTokens,
                   ReasoningTokens, CacheReadTokens, CacheWriteTokens, IsEstimated, CostUsd, PriceKnown
            FROM TrTokenUsage
            WHERE RecordedAt >= @FromUtc AND RecordedAt < @ToUtc
            ORDER BY RecordedAt
            """;
        Add(command, "FromUtc", FormatTime(fromUtc), DbType.String);
        Add(command, "ToUtc", FormatTime(toUtc), DbType.String);

        var rows = new List<TokenUsage>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new TokenUsage
            {
                Timestamp = DateTime.ParseExact(reader.GetString(0), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                ModelName = reader.GetString(1),
                ProviderName = reader.GetString(2),
                Route = reader.IsDBNull(3) ? null : reader.GetString(3),
                CallerTag = reader.IsDBNull(4) ? null : reader.GetString(4),
                InputTokens = Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture),
                OutputTokens = Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture),
                ReasoningTokens = Convert.ToInt32(reader.GetValue(7), CultureInfo.InvariantCulture),
                CacheReadTokens = Convert.ToInt32(reader.GetValue(8), CultureInfo.InvariantCulture),
                CacheWriteTokens = Convert.ToInt32(reader.GetValue(9), CultureInfo.InvariantCulture),
                IsEstimated = Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture) != 0,
                EstimatedCostUsd = decimal.Parse(reader.GetString(11), NumberStyles.Number, CultureInfo.InvariantCulture),
                IsPriceKnown = Convert.ToInt32(reader.GetValue(12), CultureInfo.InvariantCulture) != 0
            });
        }

        return rows;
    }

    /// <summary>Formats a time as the fixed-width UTC text the table stores.</summary>
    /// <param name="time">The time; an unspecified kind is taken as UTC.</param>
    /// <returns>The text.</returns>
    internal static string FormatTime(DateTime time) => ToUtc(time).ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>Converts a time to UTC, taking an unspecified kind as UTC already.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The UTC time.</returns>
    internal static DateTime ToUtc(DateTime time) => time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Utc)
    };

    private void EnsureSchema()
    {
        if (initialized)
        {
            return;
        }

        lock (initLock)
        {
            if (initialized)
            {
                return;
            }

            using var connection = OpenConnection();
            WithSchemaLock(connection, () =>
            {
                Execute(connection, """
                    CREATE TABLE IF NOT EXISTS TrTokenUsage (
                        UsageId TEXT PRIMARY KEY,
                        RecordedAt TEXT NOT NULL,
                        ModelName TEXT NOT NULL,
                        ProviderName TEXT NOT NULL,
                        Route TEXT,
                        CallerTag TEXT,
                        InputTokens INTEGER NOT NULL,
                        OutputTokens INTEGER NOT NULL,
                        ReasoningTokens INTEGER NOT NULL,
                        CacheReadTokens INTEGER NOT NULL,
                        CacheWriteTokens INTEGER NOT NULL,
                        IsEstimated INTEGER NOT NULL,
                        CostUsd TEXT NOT NULL,
                        PriceKnown INTEGER NOT NULL DEFAULT 1
                    )
                    """);
                Execute(connection, "CREATE INDEX IF NOT EXISTS IxTrTokenUsageRecordedAt ON TrTokenUsage(RecordedAt)");
            });
            initialized = true;
        }
    }

    /// <summary>Runs one statement.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="sql">The statement.</param>
    protected static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Add(DbCommand command, string name, object? value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
