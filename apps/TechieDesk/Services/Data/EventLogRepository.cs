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
            INSERT INTO "EventLog" ("OccurredAt", "Category", "Actor", "EventName", "Detail", "Source")
            VALUES (@OccurredAt, @Category, @Actor, @EventName, @Detail, @Source)
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
        sql.Append(""" ORDER BY "OccurredAt" DESC LIMIT @limit;""");
        parameters.Add("limit", filter.Limit);

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<EventLog>(
            sql.ToString(), parameters).ConfigureAwait(false);
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
    }
}
