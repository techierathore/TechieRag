using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Mcp;

/// <summary>
/// Presents the tools of one or more MCP servers to the agent loop as an ordinary
/// <see cref="IToolHandler"/> (REQ-RAG-038).
/// </summary>
/// <remarks>
/// <para><b>The whole point:</b> MCP surfaces through the tool abstraction the library already has.
/// <c>AgentLoopRunner</c> contains no MCP branch, <c>ILlmProvider</c> gained nothing, and
/// <see cref="IToolHandler"/> was not widened — an MCP tool is indistinguishable from a delegate
/// registered on <c>ToolRegistry</c>. That also means an MCP failure behaves like any other tool
/// failure: it comes back as an unsuccessful <see cref="ToolResult"/> that the model can read and
/// react to, never as an exception that aborts the turn.</para>
/// <para><b>Tool naming:</b> Names are qualified as <c>{server}-{tool}</c>, because two servers may
/// each expose a <c>search</c> and the model must be able to say which. Hyphen is used rather than a
/// dot or colon so the result still matches the <c>[A-Za-z0-9_-]{1,64}</c> pattern the mainstream
/// providers enforce; names that would exceed 64 characters are shortened with a deterministic hash
/// suffix rather than being dropped.</para>
/// <para><b>Bounded output:</b> Results longer than
/// <see cref="McpTrustPolicy.MaxToolResultCharacters"/> are truncated with a visible marker. An
/// untrusted server must not be able to evict a conversation from the context window with one
/// oversized reply.</para>
/// <para><b>Lifetime:</b> This handler owns the clients passed to it and shuts them down on
/// disposal.</para>
/// </remarks>
public sealed class McpToolHandler : IToolHandler, IAsyncDisposable
{
    private const int MaxToolNameLength = 64;

    private readonly List<ToolDefinition> definitions = [];
    private readonly Dictionary<string, McpToolBinding> bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<McpClient> clients;
    private readonly McpTrustPolicy policy;
    private readonly ILogger<McpToolHandler> logger;

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> ToolDefinitions => definitions;

    /// <summary>Gets the names of the servers whose tools this handler exposes.</summary>
    public IReadOnlyList<string> ServerNames => clients.Select(client => client.ServerName).ToList();

    /// <summary>
    /// Gets the server a qualified tool name belongs to (TR-RAG-041).
    /// </summary>
    /// <param name="qualifiedToolName">The name as the model sees it, from <see cref="ToolDefinitions"/>.</param>
    /// <returns>The configured server name, or null when this handler does not expose that tool.</returns>
    /// <remarks>
    /// <para><b>Why this is a lookup and not a string match.</b> A host applying a per-server policy —
    /// TechieDesk gates HTTP-hosted MCP tools through its egress prompt and deliberately does not gate
    /// stdio ones — was previously left to recover the server from the <c>{server}-</c> prefix that
    /// <see cref="QualifyToolName"/> produces. That works only because of two facts that were never
    /// contractual (the server name comes first, and truncation happens on the right), and it cannot
    /// distinguish a server named <c>acme</c> from one named <c>acme-eu</c>. A security boundary
    /// should not be deduced from a string; this returns the binding the handler already holds.</para>
    /// </remarks>
    public string? ServerNameFor(string qualifiedToolName) =>
        !string.IsNullOrEmpty(qualifiedToolName) && bindings.TryGetValue(qualifiedToolName, out var binding)
            ? binding.Client.ServerName
            : null;

    private McpToolHandler(List<McpClient> clients, McpTrustPolicy policy, ILogger<McpToolHandler> logger)
    {
        this.clients = clients;
        this.policy = policy;
        this.logger = logger;
    }

    /// <summary>
    /// Discovers the tools of the given clients and builds a handler exposing all of them.
    /// </summary>
    /// <param name="clients">Clients to expose; each is initialized and queried. Ownership transfers to the handler.</param>
    /// <param name="policy">The host's trust policy, for the result size bound.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A handler whose <see cref="ToolDefinitions"/> are ready for the agent loop.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <exception cref="McpException">A server failed to initialize or list its tools. Use
    /// <c>McpAgentExtensions</c> when one bad server should not fail the whole set.</exception>
    public static async Task<McpToolHandler> CreateAsync(
        IEnumerable<McpClient> clients,
        McpTrustPolicy policy,
        ILogger<McpToolHandler>? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(policy);

        var discovered = new List<(McpClient Client, IReadOnlyList<McpToolDescriptor> Tools)>();
        foreach (var client in clients)
        {
            discovered.Add((client, await client.ListToolsAsync(cancellationToken).ConfigureAwait(false)));
        }

        return FromDiscovered(discovered, policy, logger);
    }

    /// <summary>
    /// Builds a handler from clients whose tools the caller has already listed.
    /// </summary>
    /// <param name="discovered">Client/tool-list pairs; ownership of the clients transfers to the handler.</param>
    /// <param name="policy">The host's trust policy, for the result size bound.</param>
    /// <param name="logger">Logger instance.</param>
    /// <returns>A handler exposing the given tools.</returns>
    /// <remarks>
    /// Internal seam for <see cref="McpAgentExtensions"/>, which must list each server's tools inside
    /// its own error handling so one failing server does not take the others down — and must not pay
    /// for a second <c>tools/list</c> round trip to hand them over.
    /// </remarks>
    internal static McpToolHandler FromDiscovered(
        IEnumerable<(McpClient Client, IReadOnlyList<McpToolDescriptor> Tools)> discovered,
        McpTrustPolicy policy,
        ILogger<McpToolHandler>? logger = null)
    {
        var pairs = discovered.ToList();
        var handler = new McpToolHandler(
            pairs.Select(pair => pair.Client).ToList(),
            policy,
            logger ?? NullLogger<McpToolHandler>.Instance);

        foreach (var pair in pairs)
        {
            handler.AddTools(pair.Client, pair.Tools);
        }

        return handler;
    }

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        if (!bindings.TryGetValue(toolCall.Name, out var binding))
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error: Unknown tool '{toolCall.Name}'",
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolCall.Name}' is not exposed by any registered MCP server"
            };
        }

        try
        {
            var result = await binding.Client
                .CallToolAsync(binding.ToolName, toolCall.ArgumentsJson, cancellationToken)
                .ConfigureAwait(false);

            var content = Truncate(result.Text);

            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = content,
                IsSuccess = !result.IsError,
                ErrorMessage = result.IsError ? $"MCP tool '{binding.ToolName}' reported an error" : null
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Same contract as ToolRegistry: a failing tool is a message back to the model, not an
            // exception that ends the agent's turn.
            logger.LogWarning(ex, "MCP tool {ToolName} on {ServerName} failed", binding.ToolName, binding.Client.ServerName);
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error executing tool: {ex.Message}",
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var client in clients)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        clients.Clear();
    }

    /// <summary>
    /// Builds the qualified tool name the model will see for a server's tool.
    /// </summary>
    /// <param name="serverName">The configured MCP server name.</param>
    /// <param name="toolName">The tool name as the server advertises it.</param>
    /// <returns>A name within the 64-character provider limit, unique to the server/tool pair.</returns>
    /// <remarks>
    /// Exposed because callers that want to pre-authorise or display specific MCP tools need to
    /// compute the same name the model is given.
    /// </remarks>
    public static string QualifyToolName(string serverName, string toolName)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverName);
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        var qualified = $"{serverName}-{toolName}";
        if (qualified.Length <= MaxToolNameLength) return qualified;

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(qualified))).ToLowerInvariant()[..8];
        var keep = MaxToolNameLength - digest.Length - 1;
        return $"{qualified[..keep]}-{digest}";
    }

    private void AddTools(McpClient client, IReadOnlyList<McpToolDescriptor> tools)
    {
        foreach (var tool in tools)
        {
            var qualified = QualifyToolName(client.ServerName, tool.Name);

            if (bindings.ContainsKey(qualified))
            {
                logger.LogWarning(
                    "MCP tool {ToolName} from {ServerName} collides with an already-registered tool name and was skipped",
                    tool.Name, client.ServerName);
                continue;
            }

            bindings[qualified] = new McpToolBinding(client, tool.Name);
            definitions.Add(new ToolDefinition
            {
                Name = qualified,
                Description = tool.Description,
                ParametersSchema = tool.InputSchemaJson
            });
        }
    }

    private string Truncate(string text)
    {
        var limit = policy.MaxToolResultCharacters;
        if (limit <= 0 || text.Length <= limit) return text;

        logger.LogWarning("Truncated an MCP tool result from {Actual} to {Limit} characters", text.Length, limit);
        return string.Concat(text.AsSpan(0, limit), "\n[truncated by TechieRag: result exceeded the configured MCP result limit]");
    }

    private sealed record McpToolBinding(McpClient Client, string ToolName);
}
