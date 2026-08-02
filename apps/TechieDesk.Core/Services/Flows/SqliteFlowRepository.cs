using System.Globalization;
using Dapper;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Flows;

/// <summary>
/// The durable <see cref="IFlowRepository"/>: Dapper over SQLite, one row per flow, the definition
/// stored verbatim (REQ-UI-040 / BRD-92).
/// </summary>
/// <remarks>
/// <para><b>The document is never rewritten on the way through.</b> What
/// <see cref="FlowRecord.DefinitionJson"/> carries is what is stored and what comes back, byte for
/// byte. Re-serializing here would make this class a second implementation of the library's format,
/// and the day the two disagreed the stored flow would stop being the flow the author composed.</para>
/// <para><b>Timestamps are round-trip ISO-8601 text</b>, matching <c>WorkspaceMcpServer</c>. SQLite
/// has no date type, and a locale-formatted string is a row that reads back differently on a machine
/// with different regional settings.</para>
/// </remarks>
public sealed class SqliteFlowRepository : IFlowRepository
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes the repository.</summary>
    /// <param name="connectionFactory">The app database connection factory.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionFactory"/> is null.</exception>
    public SqliteFlowRepository(IAppDbConnectionFactory connectionFactory) =>
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<IReadOnlyList<FlowRecord>> ListAsync(
        string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        const string sql = """
            SELECT "FlowId", "WorkspaceId", "Name", "Description", "DefinitionJson",
                   "SchemaVersion", "IsEnabled", "CreatedAtUtc", "UpdatedAtUtc"
            FROM "Flow"
            WHERE "WorkspaceId" = @workspaceId
            ORDER BY "UpdatedAtUtc" DESC, "Name" COLLATE NOCASE;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<FlowRow>(new CommandDefinition(
            sql, new { workspaceId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(ToRecord).ToList();
    }

    /// <inheritdoc />
    public async Task<FlowRecord?> FindAsync(
        string workspaceId, string flowId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        const string sql = """
            SELECT "FlowId", "WorkspaceId", "Name", "Description", "DefinitionJson",
                   "SchemaVersion", "IsEnabled", "CreatedAtUtc", "UpdatedAtUtc"
            FROM "Flow"
            WHERE "WorkspaceId" = @workspaceId AND "FlowId" = @flowId;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<FlowRow>(new CommandDefinition(
            sql, new { workspaceId, flowId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return row is null ? null : ToRecord(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(FlowRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.WorkspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.FlowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.DefinitionJson);

        // CreatedAtUtc is deliberately NOT in the update list: re-saving a flow is an edit, not a
        // creation, and "created on" should keep meaning what it says. WorkspaceId is in the WHERE
        // of the update so a flow id guessed from another workspace cannot be overwritten here.
        const string sql = """
            INSERT INTO "Flow" (
                "FlowId", "WorkspaceId", "Name", "Description", "DefinitionJson",
                "SchemaVersion", "IsEnabled", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES (
                @flowId, @workspaceId, @name, @description, @definitionJson,
                @schemaVersion, @isEnabled, @createdAtUtc, @updatedAtUtc)
            ON CONFLICT ("FlowId") DO UPDATE SET
                "Name"           = @name,
                "Description"    = @description,
                "DefinitionJson" = @definitionJson,
                "SchemaVersion"  = @schemaVersion,
                "IsEnabled"      = @isEnabled,
                "UpdatedAtUtc"   = @updatedAtUtc
            WHERE "Flow"."WorkspaceId" = @workspaceId;
            """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            flowId = record.FlowId,
            workspaceId = record.WorkspaceId,
            name = record.Name,
            description = record.Description,
            definitionJson = record.DefinitionJson,
            schemaVersion = record.SchemaVersion,
            isEnabled = record.IsEnabled,
            createdAtUtc = ToText(record.CreatedAtUtc),
            updatedAtUtc = ToText(record.UpdatedAtUtc)
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string workspaceId, string flowId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        const string sql = """
            DELETE FROM "Flow" WHERE "WorkspaceId" = @workspaceId AND "FlowId" = @flowId;
            """;

        using var connection = connectionFactory.CreateConnection();
        var removed = await connection.ExecuteAsync(new CommandDefinition(
            sql, new { workspaceId, flowId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return removed > 0;
    }

    /// <inheritdoc />
    public async Task<bool> SetEnabledAsync(
        string workspaceId, string flowId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        const string sql = """
            UPDATE "Flow" SET "IsEnabled" = @isEnabled, "UpdatedAtUtc" = @updatedAtUtc
            WHERE "WorkspaceId" = @workspaceId AND "FlowId" = @flowId;
            """;

        using var connection = connectionFactory.CreateConnection();
        var updated = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            workspaceId,
            flowId,
            isEnabled,
            updatedAtUtc = ToText(DateTime.UtcNow)
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return updated > 0;
    }

    /// <summary>Rebuilds a record from its row.</summary>
    /// <param name="row">The stored row.</param>
    /// <returns>The record.</returns>
    private static FlowRecord ToRecord(FlowRow row) => new()
    {
        FlowId = row.FlowId,
        WorkspaceId = row.WorkspaceId,
        Name = row.Name,
        Description = row.Description,
        DefinitionJson = row.DefinitionJson,
        SchemaVersion = row.SchemaVersion,
        IsEnabled = row.IsEnabled,
        CreatedAtUtc = FromText(row.CreatedAtUtc) ?? default,
        UpdatedAtUtc = FromText(row.UpdatedAtUtc) ?? default
    };

    /// <summary>Formats a UTC timestamp for storage in round-trip form.</summary>
    /// <param name="value">The timestamp.</param>
    /// <returns>The ISO-8601 round-trip text.</returns>
    private static string ToText(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Parses a stored timestamp back to UTC.</summary>
    /// <param name="value">The stored text.</param>
    /// <returns>The timestamp, or null when absent or unparseable.</returns>
    private static DateTime? FromText(string? value) =>
        DateTime.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc)
            : null;

    /// <summary>One stored flow row, exactly as Dapper reads it.</summary>
    private sealed class FlowRow
    {
        /// <summary>Gets or sets the flow identifier.</summary>
        public string FlowId { get; set; } = string.Empty;

        /// <summary>Gets or sets the owning workspace identifier.</summary>
        public string WorkspaceId { get; set; } = string.Empty;

        /// <summary>Gets or sets the mirrored display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the mirrored description.</summary>
        public string? Description { get; set; }

        /// <summary>Gets or sets the serialized flow.</summary>
        public string DefinitionJson { get; set; } = string.Empty;

        /// <summary>Gets or sets the mirrored schema version.</summary>
        public int SchemaVersion { get; set; }

        /// <summary>Gets or sets whether the flow may run.</summary>
        public bool IsEnabled { get; set; }

        /// <summary>Gets or sets when the flow was created.</summary>
        public string? CreatedAtUtc { get; set; }

        /// <summary>Gets or sets when the flow was last edited.</summary>
        public string? UpdatedAtUtc { get; set; }
    }
}
