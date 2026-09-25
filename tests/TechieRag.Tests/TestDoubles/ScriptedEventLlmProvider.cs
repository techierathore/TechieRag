using System.Runtime.CompilerServices;
using System.Text;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;

namespace TechieRag.Tests.TestDoubles;

/// <summary>
/// An <see cref="ILlmProvider"/> that plays back one scripted list of typed stream events per model
/// call (REQ-RAG-067 / REQ-RAG-068), recording what each call received.
/// </summary>
public sealed class ScriptedEventLlmProvider : ILlmProvider
{
    private readonly Queue<IReadOnlyList<LlmStreamEvent>> turns;

    /// <summary>Creates the provider.</summary>
    /// <param name="turns">One event list per model call, in order.</param>
    public ScriptedEventLlmProvider(params IReadOnlyList<LlmStreamEvent>[] turns) =>
        this.turns = new Queue<IReadOnlyList<LlmStreamEvent>>(turns);

    /// <summary>Gets a snapshot of the messages each call received.</summary>
    public List<List<ChatMessage>> Calls { get; } = new();

    /// <summary>Gets the options each call received.</summary>
    public List<LlmCompletionOptions?> Options { get; } = new();

    /// <inheritdoc/>
    public string Name => "Scripted";

    /// <inheritdoc/>
    public string ModelName => "scripted-model";

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

#pragma warning disable CS0067 // Interface-mandated event never raised by this test double.
    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;
#pragma warning restore CS0067

    /// <summary>A text delta.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The event.</returns>
    public static LlmStreamEvent Text(string text) => LlmStreamEvent.FromText(text);

    /// <summary>A tool call.</summary>
    /// <param name="id">The call id.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="argumentsJson">The arguments.</param>
    /// <returns>The event.</returns>
    public static LlmStreamEvent Call(string id, string name, string argumentsJson) =>
        LlmStreamEvent.FromToolCall(new ToolCall { Id = id, Name = name, ArgumentsJson = argumentsJson });

    /// <summary>A completed event with fixed usage.</summary>
    /// <param name="finishReason">The finish reason.</param>
    /// <returns>The event.</returns>
    public static LlmStreamEvent Done(string finishReason = "stop") =>
        LlmStreamEvent.FromCompleted(new TokenUsage { InputTokens = 10, OutputTokens = 5 }, finishReason, "scripted-model");

    /// <inheritdoc/>
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamEventsAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var streamEvent in NextTurn(messages, options))
        {
            await Task.Yield();
            yield return streamEvent;
        }
    }

    /// <inheritdoc/>
    public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var events = NextTurn(messages, options);
        var text = new StringBuilder();
        foreach (var streamEvent in events.Where(e => e.Kind == LlmStreamEventKind.TextDelta)) text.Append(streamEvent.Text);
        var calls = events.Where(e => e.Kind == LlmStreamEventKind.ToolCall).Select(e => e.ToolCall!).ToList();

        return Task.FromResult(new LlmResponse
        {
            Content = text.Length > 0 ? text.ToString() : null,
            ToolCalls = calls.Count > 0 ? calls : null,
            Usage = new TokenUsage { InputTokens = 10, OutputTokens = 5 },
            FinishReason = calls.Count > 0 ? "tool_calls" : "stop"
        });
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        ChatStreamEventsAsync(messages, options, cancellationToken).ToTextStreamAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        ChatAsync([ChatMessage.User(prompt)], options, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        ChatStreamAsync([ChatMessage.User(prompt)], options, cancellationToken);

    /// <inheritdoc/>
    public Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public int EstimateTokenCount(string text) => Math.Max(1, text.Length / 4);

    private IReadOnlyList<LlmStreamEvent> NextTurn(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options)
    {
        Calls.Add(messages.ToList());
        Options.Add(options);
        if (turns.Count == 0) throw new InvalidOperationException("The script has no more model calls.");
        return turns.Dequeue();
    }
}
