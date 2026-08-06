using System.Text;
using Dapper;

namespace TechieDesk.Services.Data;

/// <summary>
/// Dapper implementation of <see cref="IEventLogRepository"/> building fully
/// parameterized filter queries portable across SQLite and PostgreSQL (BRD-102).
/// </summary>
public sealed class EventLogRepository : IEventLogRepository
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes the repository.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public EventLogRepository(IAppDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<long> AppendAsync(EventLog eventLog)
    {
        if (eventLog.OccurredAt == default)
        {
            eventLog.OccurredAt = DateTime.UtcNow;
        }

        const string sql = """
            INSERT INTO "EventLog" ("OccurredAt", "Category", "Actor", "EventName", "Detail", "Source", "CorrelationId")
            VALUES (@OccurredAt, @Category, @Actor, @EventName, @Detail, @Source, @CorrelationId)
            RETURNING "EventLogId";
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(sql, eventLog).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventLog>> QueryAsync(EventLogFilter filter)
    {
        var sql = new StringBuilder("""SELECT * FROM "EventLog" WHERE 1 = 1""");
        var parameters = new DynamicParameters();
        AppendFilters(filter, sql, parameters);

        // Ordering by the key as well keeps the page boundary stable when two events share a
        // timestamp — without it the same row can appear on both pages, or on neither.
        sql.Append(""" ORDER BY "OccurredAt" DESC, "EventLogId" DESC LIMIT @limit OFFSET @offset;""");
        parameters.Add("limit", filter.Limit);
        parameters.Add("offset", filter.Offset < 0 ? 0 : filter.Offset);

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<EventLog>(
            sql.ToString(), parameters).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(EventLogFilter filter)
    {
        var sql = new StringBuilder("""SELECT COUNT(*) FROM "EventLog" WHERE 1 = 1""");
        var parameters = new DynamicParameters();
        AppendFilters(filter, sql, parameters);
        sql.Append(';');

        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            sql.ToString(), parameters).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<EventLog?> GetAsync(long eventLogId)
    {
        const string sql = """SELECT * FROM "EventLog" WHERE "EventLogId" = @eventLogId;""";
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<EventLog>(
            sql, new { eventLogId }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EventLog>> QueryByCorrelationAsync(string? correlationId)
    {
        // A blank correlation id is not a group. Querying for it would return every uncorrelated
        // event in the database as though they belonged together.
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return [];
        }

        const string sql = """
            SELECT * FROM "EventLog"
            WHERE "CorrelationId" = @correlationId
            ORDER BY "OccurredAt" ASC, "EventLogId" ASC;
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<EventLog>(
            sql, new { correlationId }).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListCategoriesAsync()
    {
        const string sql = """
            SELECT DISTINCT "Category" FROM "EventLog"
            ORDER BY "Category";
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<string>(sql).ConfigureAwait(false);
        return rows.ToList();
    }

    private static void AppendFilters(EventLogFilter filter, StringBuilder sql, DynamicParameters parameters)
    {
        if (!string.IsNullOrEmpty(filter.Category))
        {
            sql.Append(""" AND "Category" = @category""");
            parameters.Add("category", filter.Category);
        }

        if (!string.IsNullOrEmpty(filter.Actor))
        {
            sql.Append(""" AND "Actor" = @actor""");
            parameters.Add("actor", filter.Actor);
        }

        if (filter.From.HasValue)
        {
            sql.Append(""" AND "OccurredAt" >= @from""");
            parameters.Add("from", filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            sql.Append(""" AND "OccurredAt" <= @to""");
            parameters.Add("to", filter.To.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            // LOWER on both sides rather than relying on collation: SQLite's LIKE is
            // case-insensitive for ASCII only, and PostgreSQL's is case-sensitive throughout, so
            // the same search would return different rows on the two providers.
            sql.Append("""
                 AND (LOWER("EventName") LIKE @search
                   OR LOWER("Actor") LIKE @search
                   OR LOWER(COALESCE("Source", '')) LIKE @search
                   OR LOWER(COALESCE("Detail", '')) LIKE @search)
                """);
            parameters.Add("search", $"%{filter.SearchText.Trim().ToLowerInvariant()}%");
        }
    }
}
