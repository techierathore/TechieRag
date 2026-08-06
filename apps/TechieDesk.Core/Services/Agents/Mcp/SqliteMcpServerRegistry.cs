using System.Text.Json;
using Dapper;
using TechieDesk.Services.Data;
using TechieRag.Mcp;

namespace TechieDesk.Services.Agents.Mcp;

/// <summary>
/// The durable <see cref="IMcpServerRegistry"/>: Dapper over SQLite, credentials in the OS
/// credential store, registrations scoped to a workspace (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Why this exists at all.</b> The library ships
/// <c>InMemoryMcpServerRegistry</c> and documents it as process-lifetime storage, telling a desktop
/// host to implement the interface over its own store if registrations should survive a restart.
/// They must: an MCP server the administrator has to re-enter every launch is not a registered
/// server, and a registry that forgets is indistinguishable from a feature that was never built.
/// This is that implementation, and it is what the DI container resolves — the in-memory registry is
/// never registered in this application.</para>
/// <para><b>Validation on the way in, exactly as the in-memory registry does it.</b> A configuration
/// the trust policy forbids is refused by <see cref="RegisterAsync"/>, so the failure lands next to
/// the administrator who typed it and nothing unusable can reach the table. The policy is re-applied
/// when a client is created, so a row cannot be started under a laxer rule than it was stored under.</para>
/// <para><b>Credentials never touch this table.</b> Header and environment VALUES go to
/// <see cref="IMcpSecretStore"/>; the row keeps their NAMES and a credential reference, which is
/// useless to anyone who copies the file (REQ-FN-039).</para>
/// <para><b>Logging:</b> only <see cref="McpServerRegistration.Describe"/> is logged, so header and
/// environment values never reach a log sink.</para>
/// </remarks>
public sealed class SqliteMcpServerRegistry : IMcpServerRegistry, IMcpServerAdministration
{
    private static readonly IReadOnlyList<string> NoStrings = [];

    private readonly IAppDbConnectionFactory connectionFactory;
    private readonly IMcpSecretStore secretStore;
    private readonly McpTrustPolicy policy;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SqliteMcpServerRegistry> logger;

    /// <summary>Gets the trust policy every registration is validated against.</summary>
    public McpTrustPolicy Policy => policy;

    /// <summary>Initializes the registry.</summary>
    /// <param name="connectionFactory">The app database connection factory (Dapper over SQLite).</param>
    /// <param name="secretStore">Where header and environment values are kept.</param>
    /// <param name="timeProvider">Clock, so registration timestamps are testable.</param>
    /// <param name="logger">Diagnostics. Never receives a credential value.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public SqliteMcpServerRegistry(
        IAppDbConnectionFactory connectionFactory,
        IMcpSecretStore secretStore,
        TimeProvider timeProvider,
        ILogger<SqliteMcpServerRegistry> logger)
    {
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        policy = McpTrustPolicyFactory.Desktop;
    }

    /// <inheritdoc />
    public async Task RegisterAsync(
        McpServerRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.WorkspaceId);

        // Same order as InMemoryMcpServerRegistry: the policy decides before anything is stored, so
        // the table can never hold a configuration that could not be launched safely.
        registration.Server.Validate(policy);

        var server = registration.Server;
        var isStdio = server.Transport == McpTransportKind.Stdio;
        var secrets = isStdio ? server.EnvironmentVariables : server.Headers;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        secretStore.Write(registration.WorkspaceId, server.Name, secrets);

        // RegisteredUtc is deliberately NOT in the update list: re-saving a server's endpoint is an
        // edit, not a new registration, and "registered on" should keep meaning what it says.
        // AdvertisedTools IS cleared, because a changed endpoint or command may be a different
        // server entirely and showing the previous one's tools would be a fabricated tool list.
        const string sql = """
            INSERT INTO "WorkspaceMcpServer" (
                "WorkspaceId", "ServerName", "Transport", "Command", "Arguments", "WorkingDirectory",
                "Endpoint", "SecretKeyNames", "CredentialRef", "AllowedTools", "TimeoutSeconds",
                "IsEnabled", "AdvertisedTools", "LastCheckedUtc", "RegisteredUtc", "UpdatedUtc")
            VALUES (
                @workspaceId, @serverName, @transport, @command, @arguments, @workingDirectory,
                @endpoint, @secretKeyNames, @credentialRef, @allowedTools, @timeoutSeconds,
                @isEnabled, NULL, NULL, @registeredUtc, @updatedUtc)
            ON CONFLICT ("WorkspaceId", "ServerName") DO UPDATE SET
                "Transport"        = @transport,
                "Command"          = @command,
                "Arguments"        = @arguments,
                "WorkingDirectory" = @workingDirectory,
                "Endpoint"         = @endpoint,
                "SecretKeyNames"   = @secretKeyNames,
                "CredentialRef"    = @credentialRef,
                "AllowedTools"     = @allowedTools,
                "TimeoutSeconds"   = @timeoutSeconds,
                "IsEnabled"        = @isEnabled,
                "AdvertisedTools"  = NULL,
                "LastCheckedUtc"   = NULL,
                "UpdatedUtc"       = @updatedUtc;
            """;

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            workspaceId = registration.WorkspaceId,
            serverName = server.Name,
            transport = server.Transport.ToString(),
            command = server.Command,
            arguments = JsonSerializer.Serialize(server.Arguments),
            workingDirectory = server.WorkingDirectory,
            endpoint = server.Endpoint,
            secretKeyNames = JsonSerializer.Serialize(secrets.Keys.ToList()),
            credentialRef = secrets.Count == 0
                ? null
                : McpSecretStore.CredentialRef(registration.WorkspaceId, server.Name),
            allowedTools = JsonSerializer.Serialize(server.AllowedTools),
            timeoutSeconds = server.TimeoutSeconds,
            isEnabled = registration.IsEnabled,
            registeredUtc = ToText(registration.RegisteredAtUtc == default ? now : registration.RegisteredAtUtc),
            updatedUtc = ToText(now)
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        logger.LogInformation("Registered MCP server {Registration}", registration.Describe());
    }

    /// <inheritdoc />
    public async Task<bool> UnregisterAsync(
        string workspaceId, string serverName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        const string sql = """
            DELETE FROM "WorkspaceMcpServer"
            WHERE "WorkspaceId" = @workspaceId AND "ServerName" = @serverName COLLATE NOCASE;
            """;

        using var connection = connectionFactory.CreateConnection();
        var removed = await connection.ExecuteAsync(new CommandDefinition(
            sql, new { workspaceId, serverName }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        // The credential goes with the registration, always. Leaving it behind would keep a revoked
        // token recoverable from the platform store after the server it belonged to was removed.
        secretStore.Delete(workspaceId, serverName);

        return removed > 0;
    }

    /// <inheritdoc />
    public async Task<bool> SetEnabledAsync(
        string workspaceId, string serverName, bool isEnabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        const string sql = """
            UPDATE "WorkspaceMcpServer"
            SET "IsEnabled" = @isEnabled, "UpdatedUtc" = @updatedUtc
            WHERE "WorkspaceId" = @workspaceId AND "ServerName" = @serverName COLLATE NOCASE;
            """;

        using var connection = connectionFactory.CreateConnection();
        var updated = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            workspaceId,
            serverName,
            isEnabled,
            updatedUtc = ToText(timeProvider.GetUtcNow().UtcDateTime)
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpServerRegistration>> ListAsync(
        string workspaceId, CancellationToken cancellationToken = default)
    {
        var records = await ListRecordsAsync(workspaceId, cancellationToken).ConfigureAwait(false);
        return records.Select(record => record.Registration).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpServerRecord>> ListRecordsAsync(
        string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        const string sql = """
            SELECT "ServerName", "Transport", "Command", "Arguments", "WorkingDirectory", "Endpoint",
                   "SecretKeyNames", "AllowedTools", "TimeoutSeconds", "IsEnabled", "AdvertisedTools",
                   "LastCheckedUtc", "RegisteredUtc"
            FROM "WorkspaceMcpServer"
            WHERE "WorkspaceId" = @workspaceId
            ORDER BY "ServerName" COLLATE NOCASE;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<McpServerRow>(new CommandDefinition(
            sql, new { workspaceId }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.Select(row => ToRecord(workspaceId, row)).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> RecordDiscoveredToolsAsync(
        string workspaceId,
        string serverName,
        IReadOnlyList<McpToolDescriptor> tools,
        DateTime observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentNullException.ThrowIfNull(tools);

        const string sql = """
            UPDATE "WorkspaceMcpServer"
            SET "AdvertisedTools" = @advertisedTools, "LastCheckedUtc" = @lastCheckedUtc
            WHERE "WorkspaceId" = @workspaceId AND "ServerName" = @serverName COLLATE NOCASE;
            """;

        var advertised = tools
            .Select(tool => new McpAdvertisedTool(tool.Name, tool.Description))
            .ToList();

        using var connection = connectionFactory.CreateConnection();
        var updated = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            workspaceId,
            serverName,
            advertisedTools = JsonSerializer.Serialize(advertised),
            lastCheckedUtc = ToText(observedAtUtc)
        }, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return updated > 0;
    }

    /// <summary>Rebuilds one registration from its row, reattaching any recoverable credentials.</summary>
    /// <param name="workspaceId">The workspace the row belongs to.</param>
    /// <param name="row">The stored row.</param>
    /// <returns>The record the screen and the agent loop both read.</returns>
    private McpServerRecord ToRecord(string workspaceId, McpServerRow row)
    {
        var isStdio = string.Equals(row.Transport, nameof(McpTransportKind.Stdio), StringComparison.OrdinalIgnoreCase);
        var secrets = secretStore.Read(workspaceId, row.ServerName);

        var config = new McpServerConfig
        {
            Name = row.ServerName,
            Transport = isStdio ? McpTransportKind.Stdio : McpTransportKind.Http,
            Command = row.Command,
            Arguments = ReadStringList(row.Arguments),
            WorkingDirectory = row.WorkingDirectory,
            Endpoint = row.Endpoint,
            TimeoutSeconds = row.TimeoutSeconds,
            AllowedTools = ReadStringList(row.AllowedTools),
            EnvironmentVariables = isStdio
                ? new Dictionary<string, string>(secrets, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal),
            Headers = isStdio
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(secrets, StringComparer.OrdinalIgnoreCase)
        };

        var registration = new McpServerRegistration
        {
            WorkspaceId = workspaceId,
            Server = config,
            IsEnabled = row.IsEnabled,
            RegisteredAtUtc = FromText(row.RegisteredUtc) ?? default
        };

        return new McpServerRecord(
            registration,
            ReadStringList(row.SecretKeyNames),
            ReadAdvertisedTools(row.AdvertisedTools),
            FromText(row.LastCheckedUtc));
    }

    /// <summary>Reads a JSON array column into a list, tolerating a null or corrupt value.</summary>
    /// <param name="json">The stored JSON array.</param>
    /// <returns>The list, or an empty list when the column is absent or unreadable.</returns>
    private static IReadOnlyList<string> ReadStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return NoStrings;

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            // A corrupt column must not stop every other server in the workspace from loading. An
            // empty argument list is also the SAFE reading: it can only ever narrow what runs.
            return NoStrings;
        }
    }

    /// <summary>Reads the cached tool list, tolerating a null or corrupt value.</summary>
    /// <param name="json">The stored JSON array.</param>
    /// <returns>The tools, or an empty list.</returns>
    private static IReadOnlyList<McpAdvertisedTool> ReadAdvertisedTools(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<McpAdvertisedTool>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Formats a UTC timestamp for storage in round-trip form.</summary>
    /// <param name="value">The timestamp.</param>
    /// <returns>The ISO-8601 round-trip text.</returns>
    private static string ToText(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O");

    /// <summary>Parses a stored timestamp back to UTC.</summary>
    /// <param name="value">The stored text.</param>
    /// <returns>The timestamp, or null when absent or unparseable.</returns>
    private static DateTime? FromText(string? value) =>
        DateTime.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc)
            : null;

    /// <summary>One stored registration row, exactly as Dapper reads it.</summary>
    private sealed class McpServerRow
    {
        /// <summary>Gets or sets the configured server name.</summary>
        public string ServerName { get; set; } = string.Empty;

        /// <summary>Gets or sets the transport name.</summary>
        public string Transport { get; set; } = string.Empty;

        /// <summary>Gets or sets the stdio executable path.</summary>
        public string? Command { get; set; }

        /// <summary>Gets or sets the stdio argument list, as a JSON array.</summary>
        public string? Arguments { get; set; }

        /// <summary>Gets or sets the stdio working directory.</summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>Gets or sets the HTTP endpoint.</summary>
        public string? Endpoint { get; set; }

        /// <summary>Gets or sets the configured credential NAMES, as a JSON array.</summary>
        public string? SecretKeyNames { get; set; }

        /// <summary>Gets or sets the tool allow-list, as a JSON array.</summary>
        public string? AllowedTools { get; set; }

        /// <summary>Gets or sets the per-request timeout in seconds.</summary>
        public int TimeoutSeconds { get; set; }

        /// <summary>Gets or sets whether the server's tools are offered to the agent.</summary>
        public bool IsEnabled { get; set; }

        /// <summary>Gets or sets the cached tool list, as a JSON array.</summary>
        public string? AdvertisedTools { get; set; }

        /// <summary>Gets or sets when the tool list was last observed.</summary>
        public string? LastCheckedUtc { get; set; }

        /// <summary>Gets or sets when the server was first registered.</summary>
        public string? RegisteredUtc { get; set; }
    }
}
