using Dapper;

namespace TechieDesk.Services.Data;

/// <summary>
/// Dapper implementation of <see cref="ILicenseCacheRepository"/>. The upsert uses
/// <c>ON CONFLICT ("UserId") DO UPDATE</c>, identical on SQLite and PostgreSQL (BRD-102).
/// </summary>
public sealed class LicenseCacheRepository : ILicenseCacheRepository
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes the repository.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public LicenseCacheRepository(IAppDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(string userId, string payloadJson, DateTime validatedAt)
    {
        const string sql = """
            INSERT INTO "LicenseCache" ("UserId", "PayloadJson", "ValidatedAt")
            VALUES (@userId, @payloadJson, @validatedAt)
            ON CONFLICT ("UserId") DO UPDATE SET
                "PayloadJson" = excluded."PayloadJson",
                "ValidatedAt" = excluded."ValidatedAt";
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            sql, new { userId, payloadJson, validatedAt }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LicenseCache?> GetAsync(string userId)
    {
        const string sql = """
            SELECT * FROM "LicenseCache"
            WHERE "UserId" = @userId;
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<LicenseCache>(
            sql, new { userId }).ConfigureAwait(false);
    }
}
