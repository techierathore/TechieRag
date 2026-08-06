using Microsoft.Extensions.Logging;
using TechieRag.Abstractions;
using TechieRag.Services;

namespace TechieRag.Mcp;

/// <summary>An MCP server that could not be brought up, and why.</summary>
/// <param name="ServerName">The configured server name.</param>
/// <param name="Reason">A log-safe explanation. Never contains header or environment values.</param>
public sealed record McpServerFailure(string ServerName, string Reason);

/// <summary>
/// The tools available to one workspace's agent, plus what it took to get them (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Why a result object:</b> starting MCP servers partially succeeds all the time — one
/// endpoint is down, one binary was uninstalled. Returning only an <see cref="IToolHandler"/> would
/// make that indistinguishable from a workspace with no servers. <see cref="Failures"/> makes the
/// difference visible so an application can tell the user which server is broken.</para>
/// <para><b>Lifetime:</b> Disposing this shuts down every MCP server it started. Any locally
/// supplied tool handler is left alone — it was not created here.</para>
/// </remarks>
public sealed class McpWorkspaceTools : IAsyncDisposable
{
    private readonly McpToolHandler? mcpHandler;

    /// <summary>Gets the handler to give the agent loop.</summary>
    /// <remarks>Composes the caller's local tools with the workspace's MCP tools; local tools win on
    /// a name clash.</remarks>
    public IToolHandler ToolHandler { get; }

    /// <summary>Gets the names of the MCP servers that started and advertised their tools.</summary>
    public IReadOnlyList<string> StartedServers { get; }

    /// <summary>Gets the servers that were registered and enabled but could not be used.</summary>
    public IReadOnlyList<McpServerFailure> Failures { get; }

    /// <summary>
    /// Gets what each started server advertised, keyed by the configured server name (TR-RAG-041).
    /// </summary>
    /// <remarks>
    /// <para><b>Why it is returned.</b> A host with a PER-SERVER rule — TechieDesk gates tools hosted
    /// off the machine by an <c>http</c> server through its egress prompt, and deliberately does not
    /// gate a stdio server's local tools — needs to know which tool came from which server. Without
    /// it the only options were a second <c>tools/list</c> round trip (an extra outbound request, on
    /// exactly the servers a zero-egress posture cares about) or deducing the boundary from the
    /// <c>{server}-</c> prefix of a qualified name. Both were worse than returning a mapping the
    /// extension already had in hand and was discarding.</para>
    /// <para>Use <see cref="McpToolHandler.QualifyToolName"/> to turn one of these descriptors into
    /// the name the model sees, or <see cref="McpToolHandler.ServerNameFor"/> to go the other way.</para>
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<McpToolDescriptor>> ToolsByServer { get; }

    internal McpWorkspaceTools(
        IToolHandler toolHandler,
        McpToolHandler? mcpHandler,
        IReadOnlyList<string> startedServers,
        IReadOnlyList<McpServerFailure> failures,
        IReadOnlyDictionary<string, IReadOnlyList<McpToolDescriptor>>? toolsByServer = null)
    {
        ToolHandler = toolHandler;
        this.mcpHandler = mcpHandler;
        StartedServers = startedServers;
        Failures = failures;
        ToolsByServer = toolsByServer ?? new Dictionary<string, IReadOnlyList<McpToolDescriptor>>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the server a qualified tool name came from (TR-RAG-041).
    /// </summary>
    /// <param name="qualifiedToolName">The name as the model sees it.</param>
    /// <returns>The configured server name, or null for a local tool or an unknown name.</returns>
    /// <remarks>
    /// Returns null for a tool that came from the caller's own <c>localTools</c> handler, which is
    /// the correct answer: a local skill has no MCP server behind it.
    /// </remarks>
    public string? ServerNameFor(string qualifiedToolName) => mcpHandler?.ServerNameFor(qualifiedToolName);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (mcpHandler is not null)
        {
            await mcpHandler.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Turns a workspace's MCP registrations into tools the agent loop can call (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para><b>Extension methods, not interface members.</b> <see cref="IMcpServerRegistry"/> stays a
/// storage contract — four methods any host can implement over any backing store. Starting servers,
/// composing handlers and handling partial failure are behaviour, and behaviour that every
/// implementation would copy verbatim belongs in an extension, not in the interface. This mirrors
/// <c>WebIngestionExtensions</c>: shipped interfaces do not grow members for composable behaviour.</para>
/// <para><b>Seam for REQ-RAG-022:</b> pass the per-workspace skills handler as
/// <c>localTools</c>. That requirement decides which built-in skills are on for the workspace and
/// hands over an <see cref="IToolHandler"/>; this method merges it with the workspace's MCP tools
/// and returns the single handler the agent loop wants. Neither side knows the other's storage, and
/// local skills take precedence over an MCP tool of the same name.</para>
/// </remarks>
public static class McpAgentExtensions
{
    /// <summary>
    /// Starts every enabled MCP server registered for a workspace and exposes their tools.
    /// </summary>
    /// <param name="registry">The registry holding the workspace's MCP registrations.</param>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="policy">The host's trust policy, re-applied as each server is created.</param>
    /// <param name="localTools">The workspace's non-MCP tools (REQ-RAG-022 skills), or null.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The composed tool handler, which servers started, and which failed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <remarks>
    /// <para><b>Partial failure is normal.</b> A server that will not start is recorded in
    /// <see cref="McpWorkspaceTools.Failures"/> and the rest still run: one unreachable endpoint must
    /// not cost the user every other tool they have. Nothing is dropped silently.</para>
    /// <para><b>Disabled servers are never started</b> — not started and then filtered, but never
    /// contacted at all.</para>
    /// </remarks>
    public static async Task<McpWorkspaceTools> BuildWorkspaceToolsAsync(
        this IMcpServerRegistry registry,
        string workspaceId,
        McpTrustPolicy policy,
        IToolHandler? localTools = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);

        var registrations = await registry.ListAsync(workspaceId, cancellationToken).ConfigureAwait(false);

        var discovered = new List<(McpClient Client, IReadOnlyList<McpToolDescriptor> Tools)>();
        var started = new List<string>();
        var failures = new List<McpServerFailure>();

        foreach (var registration in registrations.Where(item => item.IsEnabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            McpClient? client = null;
            try
            {
                client = McpClient.Create(registration.Server, policy, loggerFactory);
                var tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
                discovered.Add((client, tools));
                started.Add(registration.Server.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new McpServerFailure(registration.Server.Name, ex.Message));
                if (client is not null) await client.DisposeAsync().ConfigureAwait(false);
            }
        }

        McpToolHandler? mcpHandler = discovered.Count > 0
            ? McpToolHandler.FromDiscovered(discovered, policy, loggerFactory?.CreateLogger<McpToolHandler>())
            : null;

        IToolHandler composed = mcpHandler is null
            ? localTools ?? new ToolRegistry()
            : new CompositeToolHandler(localTools, mcpHandler);

        // TR-RAG-041: the discovery this method already performed is returned rather than discarded,
        // so a host with a per-server policy does not have to pay for a second tools/list round trip
        // or reconstruct the boundary from a tool name's prefix.
        var toolsByServer = new Dictionary<string, IReadOnlyList<McpToolDescriptor>>(StringComparer.Ordinal);
        foreach (var (client, tools) in discovered)
        {
            toolsByServer[client.ServerName] = tools;
        }

        return new McpWorkspaceTools(composed, mcpHandler, started, failures, toolsByServer);
    }
}
