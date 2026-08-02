using Dapper;

namespace TechieDesk.Services.Data;

/// <summary>
/// Dapper implementation of <see cref="IGdprRequestRepository"/> using parameterized
/// SQL portable across SQLite and PostgreSQL (BRD-102).
/// </summary>
public sealed class GdprRequestRepository : IGdprRequestRepository
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes the repository.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public GdprRequestRepository(IAppDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<long> InsertAsync(GdprRequest request)
    {
        if (request.RequestedAt == default)
        {
            request.RequestedAt = DateTime.UtcNow;
        }

        const string sql = """
            INSERT INTO "GdprRequest" ("UserId", "RequestType", "Status", "RequestedAt")
            VALUES (@UserId, @RequestType, @Status, @RequestedAt)
            RETURNING "GdprRequestId";
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(sql, request).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GdprRequest>> ListAsync(string? userId = null)
    {
        const string sql = """
            SELECT * FROM "GdprRequest"
            WHERE (@userId IS NULL OR "UserId" = @userId)
            ORDER BY "RequestedAt" DESC;
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<GdprRequest>(
            sql, new { userId }).ConfigureAwait(false);
        return rows.ToList();
    }
}
