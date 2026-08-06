using System.Text.Json;
using Dapper;
using TechieDesk.Services.Data;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// Dapper implementation of <see cref="IConnectorRepository"/> over the app database
/// (REQ-RAG-019 / REQ-RAG-020, ADR-005).
/// </summary>
/// <remarks>
/// <para><b>Upsert, not insert-then-update.</b> Saving a connector is one statement with
/// <c>ON CONFLICT DO UPDATE</c>, which both SQLite and PostgreSQL support. A read-then-branch would
/// have been two round trips with a window between them in which the connector hub and a running job
/// can both write the same row.</para>
/// <para><b><c>CreatedUtc</c> is preserved on update by the SQL, not by the caller.</b> Re-saving a
/// connector from a screen that never loaded the original would otherwise silently reset the date it
/// was added.</para>
/// </remarks>
public sealed class ConnectorRepository : IConnectorRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes a new instance of the <see cref="ConnectorRepository"/> class.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public ConnectorRepository(IAppDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConnectorDefinition>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT * FROM "Connector" ORDER BY "UpdatedUtc" DESC;
            """;
        using var connection = connectionFactory.CreateConnection();
        var rows = await connection
            .QueryAsync<ConnectorDefinition>(new CommandDefinition(sql, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<ConnectorDefinition?> GetAsync(
        string connectorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);

        const string sql = """
            SELECT * FROM "Connector" WHERE "ConnectorId" = @connectorId;
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection
            .QuerySingleOrDefaultAsync<ConnectorDefinition>(
                new CommandDefinition(sql, new { connectorId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        ConnectorDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ConnectorId);

        const string sql = """
            INSERT INTO "Connector" (
                "ConnectorId", "ConnectorType", "DisplayName", "WorkspaceId", "Pinned",
                "Settings", "CredentialRef", "CreatedUtc", "UpdatedUtc")
            VALUES (
                @ConnectorId, @ConnectorType, @DisplayName, @WorkspaceId, @Pinned,
                @Settings, @CredentialRef, @CreatedUtc, @UpdatedUtc)
            ON CONFLICT ("ConnectorId") DO UPDATE SET
                "ConnectorType" = excluded."ConnectorType",
                "DisplayName"   = excluded."DisplayName",
                "WorkspaceId"   = excluded."WorkspaceId",
                "Pinned"        = excluded."Pinned",
                "Settings"      = excluded."Settings",
                "CredentialRef" = excluded."CredentialRef",
                "UpdatedUtc"    = excluded."UpdatedUtc";
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection
            .ExecuteAsync(new CommandDefinition(sql, definition, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string connectorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);

        // The child rows are deleted explicitly rather than relying on ON DELETE CASCADE: SQLite
        // enforces foreign keys only when "PRAGMA foreign_keys" is on for the connection, and it is
        // off by default. Depending on a pragma nobody sets would leave orphaned sync state that a
        // later connector reusing the id would silently inherit.
        const string sql = """
            DELETE FROM "ConnectorItemDocument" WHERE "ConnectorId" = @connectorId;
            DELETE FROM "ConnectorSync" WHERE "ConnectorId" = @connectorId;
            DELETE FROM "Connector" WHERE "ConnectorId" = @connectorId;
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection
            .ExecuteAsync(new CommandDefinition(sql, new { connectorId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ConnectorSyncState?> GetSyncAsync(
        string connectorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);

        const string sql = """
            SELECT "LastRunUtc", "ItemVersions" FROM "ConnectorSync" WHERE "ConnectorId" = @connectorId;
            """;
        using var connection = connectionFactory.CreateConnection();
        var row = await connection
            .QuerySingleOrDefaultAsync<SyncRow>(
                new CommandDefinition(sql, new { connectorId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        return new ConnectorSyncState
        {
            LastRunUtc = row.LastRunUtc is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(row.LastRunUtc.Value, DateTimeKind.Utc)),
            ItemVersions = ReadVersions(row.ItemVersions),
        };
    }

    /// <inheritdoc />
    public async Task SaveSyncAsync(
        string connectorId, ConnectorSyncState sync, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentNullException.ThrowIfNull(sync);

        const string sql = """
            INSERT INTO "ConnectorSync" ("ConnectorId", "LastRunUtc", "ItemVersions", "UpdatedUtc")
            VALUES (@connectorId, @lastRunUtc, @itemVersions, @updatedUtc)
            ON CONFLICT ("ConnectorId") DO UPDATE SET
                "LastRunUtc"   = excluded."LastRunUtc",
                "ItemVersions" = excluded."ItemVersions",
                "UpdatedUtc"   = excluded."UpdatedUtc";
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                connectorId,
                lastRunUtc = sync.LastRunUtc?.UtcDateTime,
                itemVersions = JsonSerializer.Serialize(sync.ItemVersions, SerializerOptions),
                updatedUtc = DateTime.UtcNow,
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <summary>Reads the stored item-version map, tolerating a damaged value.</summary>
    /// <param name="json">The stored JSON.</param>
    /// <returns>The map, empty when it could not be read.</returns>
    /// <remarks>
    /// An unreadable map means "we know nothing about the previous run", which costs a full re-sync.
    /// Throwing instead would make a corrupt row a permanently failing connector with no way back.
    /// </remarks>
    private static Dictionary<string, string> ReadVersions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>The shape one <c>ConnectorSync</c> row is materialized into.</summary>
    /// <remarks>
    /// A class with settable properties, not a positional record. Dapper matches a record's
    /// constructor on exact parameter types, and SQLite hands a <c>TEXT</c> timestamp back as a
    /// string — so the record form fails at run time with "a matching signature is required" while
    /// the property form goes through Dapper's ordinary type conversion, exactly as
    /// <c>Schedule.LastRunUtc</c> already does.
    /// </remarks>
    private sealed class SyncRow
    {
        /// <summary>Gets or sets when the previous run completed.</summary>
        public DateTime? LastRunUtc { get; set; }

        /// <summary>Gets or sets the stored item-version map, as JSON.</summary>
        public string? ItemVersions { get; set; }
    }
}
