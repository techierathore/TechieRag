using System.Runtime.CompilerServices;
using System.Text;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;

namespace TechieRag.Agents.Tests.TestDoubles;

/// <summary>
/// A core <see cref="ILlmProvider"/> that plays back scripted typed events per call, for proving that
/// the seam adapters drive a TechieRag provider unchanged.
/// </summary>
public sealed class ScriptedLlmProvider : ILlmProvider
{
    private readonly Queue<IReadOnlyList<LlmStreamEvent>> turns;

    /// <summary>Creates the provider.</summary>
    /// <param name="turns">One event list per model call.</param>
    public ScriptedLlmProvider(params IReadOnlyList<LlmStreamEvent>[] turns) => this.turns = new(turns);

    /// <summary>Gets the messages each call received.</summary>
    public List<List<ChatMessage>> Calls { get; } = new();

    /// <summary>Gets the options each call received.</summary>
    public List<LlmCompletionOptions?> Options { get; } = new();

    /// <summary>Gets how many calls went through the streaming method.</summary>
    public int StreamingCalls { get; private set; }

    /// <inheritdoc/>
    public string Name => "ScriptedCore";

    /// <inheritdoc/>
    public string ModelName => "scripted-core";

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
    /// <param name="id">The id.</param>
    /// <param name="name">The name.</param>
    /// <param name="argumentsJson">The arguments.</param>
    /// <returns>The event.</returns>
    public static LlmStreamEvent Call(string id, string name, string argumentsJson) =>
        LlmStreamEvent.FromToolCall(new ToolCall { Id = id, Name = name, ArgumentsJson = argumentsJson });

    /// <summary>A completed event.</summary>
    /// <param name="finishReason">The finish reason.</param>
    /// <returns>The event.</returns>
    public static LlmStreamEvent Done(string finishReason = "stop") =>
        LlmStreamEvent.FromCompleted(new TokenUsage { InputTokens = 11, OutputTokens = 4 }, finishReason, "scripted-core");

    /// <inheritdoc/>
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamEventsAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamingCalls++;
        foreach (var streamEvent in Next(messages, options))
        {
            await Task.Yield();
            yield return streamEvent;
        }
    }

    /// <inheritdoc/>
    public Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var events = Next(messages, options);
        var text = new StringBuilder();
        foreach (var streamEvent in events.Where(e => e.Kind == LlmStreamEventKind.TextDelta)) text.Append(streamEvent.Text);
        var calls = events.Where(e => e.Kind == LlmStreamEventKind.ToolCall).Select(e => e.ToolCall!).ToList();
        return Task.FromResult(new LlmResponse
        {
            Content = text.Length > 0 ? text.ToString() : null,
            ToolCalls = calls.Count > 0 ? calls : null,
            Usage = new TokenUsage { InputTokens = 11, OutputTokens = 4 },
            FinishReason = calls.Count > 0 ? "tool_calls" : "stop",
            ModelName = ModelName
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

    private IReadOnlyList<LlmStreamEvent> Next(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options)
    {
        Calls.Add(messages.ToList());
        Options.Add(options);
        return turns.Count > 0 ? turns.Dequeue() : throw new InvalidOperationException("The script has no more model calls.");
    }
}
