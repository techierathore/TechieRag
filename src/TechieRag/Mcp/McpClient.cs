using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Mcp;

/// <summary>One tool advertised by an MCP server.</summary>
/// <param name="Name">The tool name as the server calls it.</param>
/// <param name="Description">What the tool does, shown to the model.</param>
/// <param name="InputSchemaJson">JSON Schema for the tool's arguments.</param>
public sealed record McpToolDescriptor(string Name, string Description, string InputSchemaJson);

/// <summary>The outcome of one MCP tool invocation.</summary>
/// <param name="Text">The flattened textual content the server returned.</param>
/// <param name="IsError">Whether the server flagged the call as failed.</param>
public sealed record McpToolCallResult(string Text, bool IsError);

/// <summary>
/// Speaks the Model Context Protocol to one server: handshake, tool discovery, tool invocation
/// (REQ-RAG-038).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Turns an <see cref="IMcpTransport"/> into the three MCP operations the
/// agent loop actually needs — <c>initialize</c>, <c>tools/list</c>, <c>tools/call</c>. Resources
/// and prompts are out of scope for tool calling and are not implemented.</para>
/// <para><b>Trust:</b> The server is an untrusted peer. Its tool list is filtered through
/// <see cref="McpServerConfig.AllowedTools"/>, and a call to a tool outside that list is refused
/// here even if something upstream asked for it. Tool output is returned as data; this client never
/// interprets it as instructions.</para>
/// <para><b>Lifetime:</b> Disposing the client disposes its transport, which shuts down the child
/// process for a stdio server.</para>
/// </remarks>
public sealed class McpClient : IAsyncDisposable
{
    /// <summary>The MCP protocol revision this client negotiates.</summary>
    public const string ProtocolVersion = "2025-06-18";

    private readonly IMcpTransport transport;
    private readonly McpServerConfig config;
    private readonly ILogger<McpClient> logger;
    private readonly HashSet<string> allowedTools;

    /// <summary>Gets the configured server name.</summary>
    public string ServerName => config.Name;

    /// <summary>Gets whether the initialize handshake has completed.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>Gets the protocol version the server reported, once initialized.</summary>
    public string? NegotiatedProtocolVersion { get; private set; }

    /// <summary>Gets the server's self-reported name, once initialized.</summary>
    public string? ServerTitle { get; private set; }

    /// <summary>
    /// Creates a client over an existing transport.
    /// </summary>
    /// <param name="transport">The transport to speak through; owned by this client.</param>
    /// <param name="config">The server configuration, for its name and tool allow-list.</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public McpClient(IMcpTransport transport, McpServerConfig config, ILogger<McpClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(config);

        this.transport = transport;
        this.config = config;
        this.logger = logger ?? NullLogger<McpClient>.Instance;
        allowedTools = new HashSet<string>(config.AllowedTools, StringComparer.Ordinal);
    }

    /// <summary>
    /// Creates a client for a server configuration, choosing and validating the transport.
    /// </summary>
    /// <param name="config">The server configuration.</param>
    /// <param name="policy">The host's trust policy.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>A client that has not yet been initialized.</returns>
    /// <exception cref="McpConfigurationException">Thrown when the configuration is not permitted by the policy.</exception>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public static McpClient Create(McpServerConfig config, McpTrustPolicy policy, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(policy);

        IMcpTransport transport = config.Transport switch
        {
            McpTransportKind.Stdio => new StdioMcpTransport(config, policy, loggerFactory?.CreateLogger<StdioMcpTransport>()),
            McpTransportKind.Http => new HttpMcpTransport(config, policy, loggerFactory?.CreateLogger<HttpMcpTransport>()),
            _ => throw new McpConfigurationException(config.Name, [$"Unsupported transport '{config.Transport}'."])
        };

        return new McpClient(transport, config, loggerFactory?.CreateLogger<McpClient>());
    }

    /// <summary>
    /// Starts the transport and performs the MCP initialize handshake.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task that completes once the server has acknowledged initialization.</returns>
    /// <exception cref="McpException">The server could not be started, or rejected the handshake.</exception>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsInitialized) return;

        await transport.StartAsync(cancellationToken).ConfigureAwait(false);

        var parameters = new Dictionary<string, object>
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new Dictionary<string, object> { ["tools"] = new Dictionary<string, object>() },
            ["clientInfo"] = new Dictionary<string, object>
            {
                ["name"] = "TechieRag",
                ["version"] = "1.0.0"
            }
        };

        var result = await transport.SendRequestAsync("initialize", parameters, cancellationToken).ConfigureAwait(false);

        NegotiatedProtocolVersion = ReadString(result, "protocolVersion");
        if (result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("serverInfo", out var serverInfo))
        {
            ServerTitle = ReadString(serverInfo, "name");
        }

        await transport.SendNotificationAsync("notifications/initialized", null, cancellationToken).ConfigureAwait(false);

        IsInitialized = true;
        logger.LogInformation(
            "Initialized MCP server {ServerName} (protocol {Protocol})",
            ServerName,
            NegotiatedProtocolVersion ?? "unreported");
    }

    /// <summary>
    /// Lists the tools this server exposes, after applying the configured allow-list.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Every advertised tool the configuration permits.</returns>
    /// <exception cref="McpException">The server returned an error or an unreadable tool list.</exception>
    /// <remarks>Pages through <c>nextCursor</c> so a server with many tools is not silently truncated.</remarks>
    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var tools = new List<McpToolDescriptor>();
        string? cursor = null;

        do
        {
            var parameters = cursor is null
                ? null
                : new Dictionary<string, object> { ["cursor"] = cursor };

            var result = await transport.SendRequestAsync("tools/list", parameters, cancellationToken).ConfigureAwait(false);

            if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("tools", out var listed)
                && listed.ValueKind == JsonValueKind.Array)
            {
                foreach (var tool in listed.EnumerateArray())
                {
                    var descriptor = ReadTool(tool);
                    if (descriptor is null) continue;
                    if (!IsToolAllowed(descriptor.Name))
                    {
                        logger.LogDebug(
                            "MCP server {ServerName} advertised tool {ToolName}, which the configured allow-list excludes",
                            ServerName, descriptor.Name);
                        continue;
                    }

                    tools.Add(descriptor);
                }
            }

            cursor = ReadString(result, "nextCursor");
        }
        while (!string.IsNullOrEmpty(cursor));

        return tools;
    }

    /// <summary>
    /// Invokes a tool on this server.
    /// </summary>
    /// <param name="toolName">The tool name as the server advertised it.</param>
    /// <param name="argumentsJson">A JSON object of arguments; blank or unparseable input is sent as <c>{}</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The flattened text the server returned, and whether it flagged an error.</returns>
    /// <exception cref="McpException">The tool is not permitted, or the server returned a protocol error.</exception>
    public async Task<McpToolCallResult> CallToolAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        if (!IsToolAllowed(toolName))
        {
            throw new McpException(ServerName, $"Tool '{toolName}' is not in the allow-list configured for MCP server '{ServerName}'.");
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var parameters = new Dictionary<string, object>
        {
            ["name"] = toolName,
            ["arguments"] = ParseArguments(argumentsJson)
        };

        var result = await transport.SendRequestAsync("tools/call", parameters, cancellationToken).ConfigureAwait(false);

        var isError = result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("isError", out var errorFlag)
            && errorFlag.ValueKind == JsonValueKind.True;

        return new McpToolCallResult(FlattenContent(result), isError);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await transport.DisposeAsync().ConfigureAwait(false);
    }

    private bool IsToolAllowed(string toolName) => allowedTools.Count == 0 || allowedTools.Contains(toolName);

    /// <summary>
    /// Converts the model's argument string into a JSON object element.
    /// </summary>
    /// <param name="argumentsJson">The raw argument text produced by the LLM.</param>
    /// <returns>A JSON object; an empty object when the input is blank, invalid, or not an object.</returns>
    /// <remarks>
    /// Models emit an empty string or malformed JSON often enough that failing the call would be a
    /// worse outcome than sending no arguments and letting the server report what it needs.
    /// </remarks>
    private static JsonElement ParseArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return JsonRpc.EmptyObject();

        try
        {
            var parsed = JsonRpc.ParseDetached(argumentsJson);
            return parsed.ValueKind == JsonValueKind.Object ? parsed : JsonRpc.EmptyObject();
        }
        catch (JsonException)
        {
            return JsonRpc.EmptyObject();
        }
    }

    /// <summary>
    /// Flattens an MCP <c>content</c> array into plain text for the agent loop.
    /// </summary>
    /// <param name="result">The <c>tools/call</c> result element.</param>
    /// <returns>The concatenated text parts; non-text parts are named rather than dropped silently.</returns>
    private static string FlattenContent(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var type = ReadString(item, "type");
            if (type == "text")
            {
                parts.Add(ReadString(item, "text") ?? string.Empty);
            }
            else
            {
                // A caller that receives "[image]" knows something was returned that this text-only
                // path cannot carry. Dropping it would look like the tool returned nothing.
                parts.Add($"[{type ?? "unknown"} content omitted]");
            }
        }

        return string.Join("\n", parts);
    }

    private static McpToolDescriptor? ReadTool(JsonElement tool)
    {
        if (tool.ValueKind != JsonValueKind.Object) return null;

        var name = ReadString(tool, "name");
        if (string.IsNullOrWhiteSpace(name)) return null;

        var description = ReadString(tool, "description") ?? ReadString(tool, "title") ?? name;

        var schema = tool.TryGetProperty("inputSchema", out var schemaElement)
            && schemaElement.ValueKind == JsonValueKind.Object
            ? schemaElement.GetRawText()
            : """{"type":"object","properties":{}}""";

        return new McpToolDescriptor(name, description, schema);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
