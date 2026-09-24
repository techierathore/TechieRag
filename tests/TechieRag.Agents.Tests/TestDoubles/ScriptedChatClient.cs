using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace TechieRag.Agents.Tests.TestDoubles;

/// <summary>
/// An <see cref="IChatClient"/> that plays back one scripted response per model call and records what
/// each call received — the model side of an Agent Framework run, with no network.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<ChatResponse> responses;

    /// <summary>Creates the client.</summary>
    /// <param name="responses">One response per model call, in order.</param>
    public ScriptedChatClient(params ChatResponse[] responses) => this.responses = new Queue<ChatResponse>(responses);

    /// <summary>Gets the messages each call received.</summary>
    public List<List<ChatMessage>> Calls { get; } = new();

    /// <summary>Gets the options each call received.</summary>
    public List<ChatOptions?> Options { get; } = new();

    /// <summary>A response that calls one tool.</summary>
    /// <param name="callId">The call id.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The response.</returns>
    public static ChatResponse ToolCall(string callId, string name, Dictionary<string, object?> arguments) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]));

    /// <summary>A text response.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The response.</returns>
    public static ChatResponse Answer(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    /// <inheritdoc/>
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Next(messages, options));

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in Next(messages, options).ToChatResponseUpdates())
        {
            await Task.Yield();
            yield return update;
        }
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private ChatResponse Next(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        Calls.Add(messages.ToList());
        Options.Add(options);
        return responses.Count > 0 ? responses.Dequeue() : throw new InvalidOperationException("The script has no more model calls.");
    }
}
