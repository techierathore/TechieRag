using TechieRag.Abstractions;
using TechieRag.Mcp;
using TechieRag.Services;

namespace TechieDesk.Services.Agents.Mcp;

/// <summary>
/// The default <see cref="IWorkspaceMcpService"/> (REQ-RAG-023 / BRD-86).
/// </summary>
/// <inheritdoc cref="IWorkspaceMcpService"/>
public sealed class WorkspaceMcpService : IWorkspaceMcpService
{
    private readonly IMcpServerRegistry registry;
    private readonly IMcpServerAdministration administration;
    private readonly IMcpSecretStore secretStore;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<WorkspaceMcpService> logger;

    /// <summary>Initializes the service.</summary>
    /// <param name="registry">Durable registration storage, read through the library contract.</param>
    /// <param name="administration">The same storage, read through the app-only contract.</param>
    /// <param name="secretStore">Where header and environment values are kept.</param>
    /// <param name="timeProvider">Clock, so discovery timestamps are testable.</param>
    /// <param name="loggerFactory">Passed through to the library's MCP clients and transports.</param>
    /// <param name="logger">Diagnostics. Never receives a credential value.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public WorkspaceMcpService(
        IMcpServerRegistry registry,
        IMcpServerAdministration administration,
        IMcpSecretStore secretStore,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        ILogger<WorkspaceMcpService> logger)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.administration = administration ?? throw new ArgumentNullException(nameof(administration));
        this.secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public McpCredentialProtection CredentialProtection => secretStore.Protection;

    /// <inheritdoc />
    public Task<IReadOnlyList<McpServerRecord>> ListAsync(
        string workspaceId, CancellationToken cancellationToken = default) =>
        administration.ListRecordsAsync(workspaceId, cancellationToken);

    /// <inheritdoc />
    public async Task<McpConnectionReport> TestAsync(
        string workspaceId, McpServerDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(draft);

        var config = draft.ToConfig(secretStore.Read(workspaceId, draft.Name.Trim()));
        var problems = config.FindProblems(McpTrustPolicyFactory.Desktop);
        if (problems.Count > 0)
        {
            return new McpConnectionReport(IsSuccess: false, [], problems);
        }

        await using var client = McpClient.Create(config, McpTrustPolicyFactory.Desktop, loggerFactory);

        try
        {
            var tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
            var advertised = tools
                .Select(tool => new McpAdvertisedTool(tool.Name, tool.Description))
                .ToList();

            // Refresh the cached list so the screen can show these tools later without dialling
            // again. Silently does nothing when the server has not been saved yet, which is the
            // normal "test before you save" case.
            await administration
                .RecordDiscoveredToolsAsync(
                    workspaceId, config.Name, tools, timeProvider.GetUtcNow().UtcDateTime, cancellationToken)
                .ConfigureAwait(false);

            return new McpConnectionReport(IsSuccess: true, advertised, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // McpException messages are written to carry the server name and the protocol error and
            // never a header value; anything else is reported by type and message only.
            logger.LogWarning(ex, "MCP test connection to {Server} failed", config.Describe());
            return new McpConnectionReport(IsSuccess: false, [], [ex.Message]);
        }
    }

    /// <inheritdoc />
    public async Task<McpSaveOutcome> SaveAsync(
        string workspaceId, McpServerDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(draft);

        var config = draft.ToConfig(secretStore.Read(workspaceId, draft.Name.Trim()));

        // FindProblems rather than Validate, so the form can show every reason at once instead of
        // one error per attempt.
        var problems = config.FindProblems(McpTrustPolicyFactory.Desktop);
        if (problems.Count > 0)
        {
            return new McpSaveOutcome(IsSuccess: false, problems);
        }

        var registration = new McpServerRegistration
        {
            WorkspaceId = workspaceId,
            Server = config,
            IsEnabled = draft.IsEnabled,
            RegisteredAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

        try
        {
            await registry.RegisterAsync(registration, cancellationToken).ConfigureAwait(false);
            return new McpSaveOutcome(IsSuccess: true, []);
        }
        catch (McpConfigurationException ex)
        {
            return new McpSaveOutcome(IsSuccess: false, ex.Problems);
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(
        string workspaceId, string serverName, CancellationToken cancellationToken = default) =>
        registry.UnregisterAsync(workspaceId, serverName, cancellationToken);

    /// <inheritdoc />
    public Task<bool> SetEnabledAsync(
        string workspaceId, string serverName, bool isEnabled, CancellationToken cancellationToken = default) =>
        registry.SetEnabledAsync(workspaceId, serverName, isEnabled, cancellationToken);

    /// <inheritdoc />
    public async Task<McpTurnTools> BuildTurnToolsAsync(
        string workspaceId,
        IToolHandler localTools,
        EgressGate gate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(localTools);
        ArgumentNullException.ThrowIfNull(gate);

        var registrations = await administration
            .ListRecordsAsync(workspaceId, cancellationToken)
            .ConfigureAwait(false);

        var enabled = registrations.Where(record => record.IsEnabled).ToList();
        if (enabled.Count == 0)
        {
            // The zero-egress path, stated explicitly rather than left to fall out of an empty loop.
            // No client is created, no transport is constructed, no socket is opened and no process
            // is started — which is what REQ-NFR-008 promises a stock install (BRD-99).
            return new McpTurnTools(localTools, started: null, [], []);
        }

        var started = await registry
            .BuildWorkspaceToolsAsync(
                workspaceId,
                McpTrustPolicyFactory.Desktop,
                localTools: null,
                loggerFactory,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var failure in started.Failures)
        {
            logger.LogWarning(
                "MCP server {ServerName} is registered for this workspace but could not be used: {Reason}",
                failure.ServerName, failure.Reason);
        }

        if (started.StartedServers.Count == 0)
        {
            await started.DisposeAsync().ConfigureAwait(false);
            return new McpTurnTools(localTools, started: null, [], started.Failures);
        }

        // REQ-NFR-013: an HTTP MCP server is a third party off this machine, so its tools go through
        // the same once-per-turn confirmation the catalogue's egress skills do. Stdio servers are
        // local processes and are not gated — see McpEgressGuard for that reasoning in full.
        var gatedServerNames = enabled
            .Where(record => record.Registration.Server.Transport == McpTransportKind.Http)
            .Select(record => record.ServerName)
            .ToList();

        var guarded = new McpEgressGuard(started.ToolHandler, gatedServerNames, gate);

        // Local skills first: a registered server must not be able to shadow rag-search by naming a
        // tool the same thing. CompositeToolHandler gives the first handler the name.
        var composed = new CompositeToolHandler(localTools, guarded);

        return new McpTurnTools(composed, started, started.StartedServers, started.Failures);
    }
}
