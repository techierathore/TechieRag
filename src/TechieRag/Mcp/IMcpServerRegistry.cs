namespace TechieRag.Mcp;

/// <summary>
/// One MCP server registered against one workspace (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Binds a validated <see cref="McpServerConfig"/> to a workspace, with an
/// enabled flag so an administrator can suspend a server without losing its configuration.</para>
/// <para><b>Logging:</b> Log <see cref="Describe"/>, never the object graph —
/// <see cref="McpServerConfig.Headers"/> and <see cref="McpServerConfig.EnvironmentVariables"/> hold
/// credentials.</para>
/// </remarks>
public sealed record McpServerRegistration
{
    /// <summary>Gets the workspace this server belongs to.</summary>
    public required string WorkspaceId { get; init; }

    /// <summary>Gets the server configuration.</summary>
    public required McpServerConfig Server { get; init; }

    /// <summary>Gets whether the server's tools are offered to the agent.</summary>
    /// <remarks>Disabled servers stay registered but are never started.</remarks>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Gets when the registration was created (UTC).</summary>
    public DateTime RegisteredAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Produces a log-safe description of this registration.</summary>
    /// <returns>The workspace, enabled state, and the server's redacted description.</returns>
    public string Describe() => $"workspace={WorkspaceId} enabled={IsEnabled} {Server.Describe()}";
}

/// <summary>
/// Stores which MCP servers are registered for which workspace (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The registration side of "admin-registered MCP servers expose tools to the
/// agent". It answers one question — which servers may this workspace's agent use — and leaves
/// starting them to <see cref="McpAgentExtensions"/> and rendering them to the application.</para>
/// <para><b>A new interface, deliberately.</b> <c>IWorkspaceStore</c> is published and implemented
/// by consumers; adding MCP methods to it would break every existing implementer for a feature most
/// of them do not use. A separate interface also lets a host keep MCP registrations somewhere other
/// than the workspace database (an admin service, a policy file) without reimplementing workspace
/// storage.</para>
/// <para><b>Validation belongs here.</b> An implementation must validate against the host's
/// <see cref="McpTrustPolicy"/> on registration, so a configuration that could never be launched
/// safely is rejected while an administrator is looking at it rather than at the moment an agent
/// tries to use it.</para>
/// <para><b>Seam for REQ-RAG-022 (per-workspace skill toggles):</b> that requirement owns the
/// library's built-in skills and their per-workspace on/off state, and produces an
/// <c>IToolHandler</c> of enabled skills. This interface produces MCP-backed tools for the same
/// workspace. The two meet at
/// <see cref="McpAgentExtensions.BuildWorkspaceToolsAsync(IMcpServerRegistry, string, McpTrustPolicy, Abstractions.IToolHandler, Microsoft.Extensions.Logging.ILoggerFactory, CancellationToken)"/>,
/// which composes the skills handler with the MCP handler. Neither side needs to know the other's
/// storage.</para>
/// </remarks>
public interface IMcpServerRegistry
{
    /// <summary>
    /// Registers a server for a workspace, replacing any existing registration with the same name.
    /// </summary>
    /// <param name="registration">The registration to store.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous registration.</returns>
    /// <exception cref="McpConfigurationException">The configuration is not permitted by the trust policy.</exception>
    Task RegisterAsync(McpServerRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a server registration from a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="serverName">The configured server name.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True when a registration was removed; false when there was none.</returns>
    Task<bool> UnregisterAsync(string workspaceId, string serverName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables a registered server without removing it.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="serverName">The configured server name.</param>
    /// <param name="isEnabled">True to offer its tools to the agent; false to suspend it.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True when the registration was found and updated.</returns>
    Task<bool> SetEnabledAsync(string workspaceId, string serverName, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every server registered for a workspace, enabled or not.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The workspace's registrations, ordered by server name.</returns>
    Task<IReadOnlyList<McpServerRegistration>> ListAsync(string workspaceId, CancellationToken cancellationToken = default);
}
