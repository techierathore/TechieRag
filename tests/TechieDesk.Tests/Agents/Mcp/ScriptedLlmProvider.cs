using System.Runtime.CompilerServices;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieDesk.Tests.Agents.Mcp;

/// <summary>
/// An <see cref="ILlmProvider"/> that calls exactly the tool it is told to and then summarises what
/// came back, so the agent loop can be driven deterministically (REQ-RAG-023).
/// </summary>
/// <remarks>
/// <para>The point of these tests is the tool path, not the model. Scripting the model means the
/// assertion is about what the MCP server received and what the tool returned, with no
/// non-determinism in between — and, crucially, that the FINAL ANSWER is built from the real tool
/// result, so a tool that silently returned nothing cannot look like a pass.</para>
/// <para>It also records the tool definitions it was offered, which is how "a registered server's
/// tools reach the model" is checked rather than assumed.</para>
/// </remarks>
public sealed class ScriptedLlmProvider : ILlmProvider
{
    private readonly string toolNameToCall;
    private readonly string argumentsJson;
    private int callsMade;

    /// <summary>Creates a provider that calls one tool once, then answers.</summary>
    /// <param name="toolNameToCall">The qualified tool name to invoke.</param>
    /// <param name="argumentsJson">The arguments to send.</param>
    public ScriptedLlmProvider(string toolNameToCall, string argumentsJson = "{}")
    {
        this.toolNameToCall = toolNameToCall;
        this.argumentsJson = argumentsJson;
    }

    /// <inheritdoc />
    public string Name => "scripted";

    /// <inheritdoc />
    public string ModelName => "scripted-1";

    /// <inheritdoc />
    public bool SupportsToolCalling => true;

    /// <inheritdoc />
    public bool SupportsStreaming => false;

    /// <summary>Gets the tool names the loop offered on the first turn.</summary>
    public IReadOnlyList<string> OfferedToolNames { get; private set; } = [];

    /// <inheritdoc />
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        OnCompletionCompleted?.Invoke(this, null!);

        if (callsMade++ == 0)
        {
            OfferedToolNames = options?.Tools?.Select(tool => tool.Name).ToList() ?? [];

            return Task.FromResult(new LlmResponse
            {
                Content = null,
                ToolCalls = [new ToolCall { Id = "call-1", Name = toolNameToCall, ArgumentsJson = argumentsJson }],
                FinishReason = "tool_calls",
                ModelName = ModelName,
                Usage = new TokenUsage()
            });
        }

        // The final answer is assembled from the tool message the loop appended, so an empty or
        // missing tool result shows up in the assertion rather than being papered over.
        var toolText = messages.LastOrDefault(message => message.Role == "tool")?.Content ?? "(no tool result)";

        return Task.FromResult(new LlmResponse
        {
            Content = "The tool said: " + toolText,
            FinishReason = "stop",
            ModelName = ModelName,
            Usage = new TokenUsage()
        });
    }

    /// <inheritdoc />
    public Task<LlmResponse> CompleteAsync(
        string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        ChatAsync([ChatMessage.User(prompt)], options, cancellationToken);

    /// <inheritdoc />
    public Task<T> CompleteAsync<T>(
        string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
        where T : class =>
        throw new NotSupportedException("The scripted provider does not produce structured output.");

    /// <inheritdoc />
    public async IAsyncEnumerable<string> CompleteStreamAsync(
        string prompt,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <inheritdoc />
    public int EstimateTokenCount(string text) => text.Length / 4;
}
