using Dapper;

namespace TechieDesk.Services.Data;

/// <summary>
/// Dapper implementation of <see cref="IInstanceSettingRepository"/>. The upsert
/// uses <c>ON CONFLICT ... DO UPDATE</c>, which both SQLite and PostgreSQL support
/// with identical syntax (BRD-102).
/// </summary>
public sealed class InstanceSettingRepository : IInstanceSettingRepository
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes the repository.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public InstanceSettingRepository(IAppDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string settingKey)
    {
        const string sql = """
            SELECT "SettingValue" FROM "InstanceSetting"
            WHERE "SettingKey" = @settingKey;
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<string?>(
            sql, new { settingKey }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string? Get(string settingKey)
    {
        const string sql = """
            SELECT "SettingValue" FROM "InstanceSetting"
            WHERE "SettingKey" = @settingKey;
            """;
        using var connection = connectionFactory.CreateConnection();
        return connection.ExecuteScalar<string?>(sql, new { settingKey });
    }

    /// <inheritdoc />
    public async Task SetAsync(string settingKey, string settingValue)
    {
        const string sql = """
            INSERT INTO "InstanceSetting" ("SettingKey", "SettingValue", "UpdatedAt")
            VALUES (@settingKey, @settingValue, @updatedAt)
            ON CONFLICT ("SettingKey") DO UPDATE SET
                "SettingValue" = excluded."SettingValue",
                "UpdatedAt" = excluded."UpdatedAt";
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            sql, new { settingKey, settingValue, updatedAt = DateTime.UtcNow }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstanceSetting>> GetAllAsync()
    {
        const string sql = """
            SELECT * FROM "InstanceSetting"
            ORDER BY "SettingKey";
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<InstanceSetting>(sql).ConfigureAwait(false);
        return rows.ToList();
    }
}
