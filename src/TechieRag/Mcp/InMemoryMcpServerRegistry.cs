using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Mcp;

/// <summary>
/// Process-lifetime <see cref="IMcpServerRegistry"/> that validates every registration against the
/// host's trust policy (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The library's default registry. It is the whole of the registration
/// behaviour a host needs — validate, store, enable/disable, list — without imposing a schema on
/// applications that already have somewhere to keep this. A desktop application that wants
/// registrations to survive a restart implements <see cref="IMcpServerRegistry"/> over its own
/// storage and reuses everything else.</para>
/// <para><b>Validation on the way in, not on the way out.</b> A configuration that the trust policy
/// forbids is refused by <see cref="RegisterAsync"/>. That keeps the failure next to the person who
/// wrote the configuration, and means nothing in the store can be launched unsafely later.</para>
/// <para><b>Logging:</b> Only <see cref="McpServerRegistration.Describe"/> is logged, so header and
/// environment values never reach a log sink.</para>
/// <para><b>Threading:</b> Safe for concurrent use.</para>
/// </remarks>
public sealed class InMemoryMcpServerRegistry : IMcpServerRegistry
{
    private readonly Dictionary<string, Dictionary<string, McpServerRegistration>> byWorkspace =
        new(StringComparer.Ordinal);
    // Plain object rather than System.Threading.Lock: the library also targets net8.0, where that
    // type does not exist.
    private readonly object gate = new();
    private readonly McpTrustPolicy policy;
    private readonly ILogger<InMemoryMcpServerRegistry> logger;

    /// <summary>Gets the trust policy every registration is validated against.</summary>
    public McpTrustPolicy Policy => policy;

    /// <summary>
    /// Creates a registry bound to a trust policy.
    /// </summary>
    /// <param name="policy">The host's trust policy; defaults to <see cref="McpTrustPolicy.Strict"/>.</param>
    /// <param name="logger">Logger instance.</param>
    public InMemoryMcpServerRegistry(McpTrustPolicy? policy = null, ILogger<InMemoryMcpServerRegistry>? logger = null)
    {
        this.policy = policy ?? McpTrustPolicy.Strict;
        this.logger = logger ?? NullLogger<InMemoryMcpServerRegistry>.Instance;
    }

    /// <inheritdoc/>
    public Task RegisterAsync(McpServerRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.WorkspaceId);

        registration.Server.Validate(policy);

        lock (gate)
        {
            if (!byWorkspace.TryGetValue(registration.WorkspaceId, out var servers))
            {
                servers = new Dictionary<string, McpServerRegistration>(StringComparer.OrdinalIgnoreCase);
                byWorkspace[registration.WorkspaceId] = servers;
            }

            servers[registration.Server.Name] = registration;
        }

        logger.LogInformation("Registered MCP server {Registration}", registration.Describe());
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> UnregisterAsync(string workspaceId, string serverName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        lock (gate)
        {
            var removed = byWorkspace.TryGetValue(workspaceId, out var servers) && servers.Remove(serverName);
            return Task.FromResult(removed);
        }
    }

    /// <inheritdoc/>
    public Task<bool> SetEnabledAsync(string workspaceId, string serverName, bool isEnabled, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        lock (gate)
        {
            if (!byWorkspace.TryGetValue(workspaceId, out var servers)
                || !servers.TryGetValue(serverName, out var existing))
            {
                return Task.FromResult(false);
            }

            servers[serverName] = existing with { IsEnabled = isEnabled };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<McpServerRegistration>> ListAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        lock (gate)
        {
            if (!byWorkspace.TryGetValue(workspaceId, out var servers))
            {
                return Task.FromResult<IReadOnlyList<McpServerRegistration>>([]);
            }

            var listed = servers.Values
                .OrderBy(registration => registration.Server.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult<IReadOnlyList<McpServerRegistration>>(listed);
        }
    }
}
