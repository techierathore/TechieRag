using System.Runtime.CompilerServices;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// A deterministic LLM stand-in that answers from a script and RECORDS every conversation it was
/// handed.
/// </summary>
/// <remarks>
/// <para>The recording is the point. Most orchestration claims — what a handoff carried, what a
/// sub-agent was told, whether a system prompt leaked across a boundary — are claims about the
/// messages a model was sent, and they can only be checked by keeping them.</para>
/// <para>The script is a queue of turns, so one provider can serve several nodes and each node's
/// answer is fixed in advance. Running past the end of the script is a test bug, not a silent
/// default, so it throws.</para>
/// </remarks>
public sealed class ScriptedLlmProvider : ILlmProvider
{
    private readonly Queue<LlmResponse> script;

    /// <summary>Creates a provider that returns the given responses in order.</summary>
    /// <param name="name">The provider name, so a test with several can tell them apart.</param>
    /// <param name="responses">The turns to return.</param>
    public ScriptedLlmProvider(string name, params LlmResponse[] responses)
    {
        Name = name;
        script = new Queue<LlmResponse>(responses);
    }

    /// <summary>Gets every conversation this provider was sent, in order.</summary>
    public List<IReadOnlyList<ChatMessage>> Conversations { get; } = [];

    /// <summary>Gets the flattened text of every message this provider ever saw.</summary>
    public string AllSeenText => string.Join(
        "\n",
        Conversations.SelectMany(conversation => conversation).Select(message => $"{message.Role}: {message.Content}"));

    /// <summary>Gets how many turns were requested.</summary>
    public int TurnCount => Conversations.Count;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string ModelName => "scripted-model";

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => false;

    /// <inheritdoc/>
#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;
#pragma warning restore CS0067

    /// <summary>Builds a plain text answer.</summary>
    /// <param name="content">The answer text.</param>
    /// <returns>A response with no tool calls.</returns>
    public static LlmResponse Says(string content) => new()
    {
        Content = content,
        Usage = new TokenUsage { InputTokens = 10, OutputTokens = 5, ModelName = "scripted-model" },
        FinishReason = "stop"
    };

    /// <summary>Builds a turn that asks for one tool call.</summary>
    /// <param name="toolName">The tool the model names.</param>
    /// <param name="argumentsJson">The arguments it supplies.</param>
    /// <param name="callId">The tool-call id.</param>
    /// <returns>A response carrying one tool call.</returns>
    public static LlmResponse CallsTool(string toolName, string argumentsJson = "{}", string callId = "call-1") => new()
    {
        Usage = new TokenUsage { InputTokens = 10, OutputTokens = 5, ModelName = "scripted-model" },
        ToolCalls = [new ToolCall { Id = callId, Name = toolName, ArgumentsJson = argumentsJson }],
        FinishReason = "tool_calls"
    };

    /// <inheritdoc/>
    public Task<LlmResponse> ChatAsync(
        IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Conversations.Add(messages.Select(Clone).ToList());

        if (script.Count == 0)
        {
            throw new InvalidOperationException(
                $"'{Name}' was asked for turn {Conversations.Count} but its script ran out. The flow ran more agent turns than the test expected.");
        }

        return Task.FromResult(script.Dequeue());
    }

    /// <inheritdoc/>
    public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        ChatAsync([ChatMessage.User(prompt)], options, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield break;
    }

    /// <inheritdoc/>
    public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public int EstimateTokenCount(string text) => Math.Max(1, text.Length / 4);

    /// <summary>Copies a message so a later mutation of the live list cannot rewrite history.</summary>
    /// <param name="message">The message as sent.</param>
    /// <returns>A detached copy.</returns>
    private static ChatMessage Clone(ChatMessage message) => new()
    {
        Role = message.Role,
        Content = message.Content,
        ToolCalls = message.ToolCalls,
        ToolCallId = message.ToolCallId,
        Name = message.Name
    };
}

/// <summary>
/// A real tool handler that records what it was asked to run, so a test can prove a guardrail
/// stopped a call rather than the model simply not making one.
/// </summary>
public sealed class RecordingToolHandler : IToolHandler
{
    private readonly Dictionary<string, Func<string, string>> implementations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ToolDefinition> definitions = [];

    /// <summary>Gets the names of the tools that actually executed, in order.</summary>
    public List<string> Executed { get; } = [];

    /// <summary>Gets the arguments each executed tool received, in order.</summary>
    public List<string> ExecutedArguments { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> ToolDefinitions => definitions;

    /// <summary>Registers a tool that returns a fixed transformation of its arguments.</summary>
    /// <param name="name">The tool name.</param>
    /// <param name="description">The description the model sees.</param>
    /// <param name="implementation">What the tool returns for a given arguments JSON.</param>
    /// <returns>This handler, for chaining.</returns>
    public RecordingToolHandler Register(string name, string description, Func<string, string> implementation)
    {
        definitions.Add(new ToolDefinition
        {
            Name = name,
            Description = description,
            ParametersSchema = """{"type":"object","properties":{"input":{"type":"string"}}}"""
        });
        implementations[name] = implementation;
        return this;
    }

    /// <inheritdoc/>
    public Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        if (!implementations.TryGetValue(toolCall.Name, out var implementation))
        {
            return Task.FromResult(new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error: Unknown tool '{toolCall.Name}'",
                IsSuccess = false,
                ErrorMessage = "not registered"
            });
        }

        Executed.Add(toolCall.Name);
        ExecutedArguments.Add(toolCall.ArgumentsJson);

        return Task.FromResult(new ToolResult
        {
            ToolCallId = toolCall.Id,
            Content = implementation(toolCall.ArgumentsJson)
        });
    }
}

/// <summary>An <see cref="IProgress{T}"/> that records reports inline, preserving their order.</summary>
public sealed class RecordingProgress : IProgress<AgentStep>
{
    /// <summary>Gets the steps reported, oldest first.</summary>
    public List<AgentStep> Steps { get; } = [];

    /// <inheritdoc/>
    public void Report(AgentStep value) => Steps.Add(value);
}
