using System.Runtime.CompilerServices;
using System.Text.Json;
using TechieRag.Abstractions;
using TechieRag.Mcp;
using TechieRag.Models;

namespace TechieRag.Tests.Mcp;

/// <summary>
/// Scripted <see cref="IMcpTransport"/> that answers the MCP handshake and returns a fixed tool
/// list, so MCP behaviour can be exercised with no server present.
/// </summary>
public sealed class FakeMcpTransport : IMcpTransport
{
    private readonly string toolsResultJson;
    private readonly Func<string, string, string>? callTool;

    /// <summary>Creates a transport that advertises the given tools.</summary>
    /// <param name="serverName">The server name reported by the transport.</param>
    /// <param name="toolsResultJson">The JSON returned as the <c>tools/list</c> result.</param>
    /// <param name="callTool">Handler for <c>tools/call</c>: (toolName, argumentsJson) =&gt; result JSON.</param>
    public FakeMcpTransport(string serverName, string toolsResultJson, Func<string, string, string>? callTool = null)
    {
        ServerName = serverName;
        this.toolsResultJson = toolsResultJson;
        this.callTool = callTool;
    }

    /// <inheritdoc/>
    public string ServerName { get; }

    /// <summary>Gets every JSON-RPC method name this transport was asked for, in order.</summary>
    public List<string> Methods { get; } = [];

    /// <summary>Gets the arguments JSON of the most recent tools/call.</summary>
    public string? LastArgumentsJson { get; private set; }

    /// <summary>Gets whether the transport was started.</summary>
    public bool IsStarted { get; private set; }

    /// <summary>Gets whether the transport was disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        IsStarted = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        Methods.Add(method);

        return method switch
        {
            "initialize" => Task.FromResult(Parse("""{"protocolVersion":"2025-06-18","serverInfo":{"name":"fake"}}""")),
            "tools/list" => Task.FromResult(Parse(toolsResultJson)),
            "tools/call" => Task.FromResult(Parse(InvokeTool(parameters))),
            _ => throw new McpException(ServerName, $"unexpected method {method}")
        };
    }

    /// <inheritdoc/>
    public Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken = default)
    {
        Methods.Add(method);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    private string InvokeTool(object? parameters)
    {
        var map = (IDictionary<string, object>)parameters!;
        var toolName = (string)map["name"];
        LastArgumentsJson = ((JsonElement)map["arguments"]).GetRawText();

        return callTool is null
            ? """{"content":[{"type":"text","text":"ok"}]}"""
            : callTool(toolName, LastArgumentsJson);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

/// <summary>
/// LLM test double that requests one tool call on its first turn and answers with text on the
/// second, so a full agent loop can be driven without a model.
/// </summary>
public sealed class ScriptedToolCallingLlmProvider : ILlmProvider
{
    private readonly string toolName;
    private readonly string argumentsJson;
    private int turn;

    /// <summary>Creates a provider that asks for one tool call, then answers.</summary>
    /// <param name="toolName">The tool the model will name on its first turn.</param>
    /// <param name="argumentsJson">The arguments the model will supply.</param>
    public ScriptedToolCallingLlmProvider(string toolName, string argumentsJson = "{}")
    {
        this.toolName = toolName;
        this.argumentsJson = argumentsJson;
    }

    /// <summary>Gets the tool-result messages the loop fed back to the model.</summary>
    public List<ChatMessage> ObservedToolMessages { get; } = [];

    /// <summary>Gets the tool definitions the loop offered the model.</summary>
    public IReadOnlyList<ToolDefinition>? OfferedTools { get; private set; }

    /// <inheritdoc/>
    public string Name => "ScriptedLlm";

    /// <inheritdoc/>
    public string ModelName => "scripted";

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => false;

    /// <inheritdoc/>
#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;
#pragma warning restore CS0067

    /// <inheritdoc/>
    public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        OfferedTools ??= options?.Tools;
        ObservedToolMessages.AddRange(messages.Where(message => message.Role == "tool"));

        if (turn++ == 0)
        {
            return Task.FromResult(new LlmResponse
            {
                Usage = new TokenUsage(),
                ToolCalls = [new ToolCall { Id = "call-1", Name = toolName, ArgumentsJson = argumentsJson }],
                FinishReason = "tool_calls"
            });
        }

        return Task.FromResult(new LlmResponse { Content = "done", Usage = new TokenUsage() });
    }

    /// <inheritdoc/>
    public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield break;
    }

    /// <inheritdoc/>
    public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public int EstimateTokenCount(string text) => Math.Max(1, text.Length / 4);
}
