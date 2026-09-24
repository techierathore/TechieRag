using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// Reads an OpenAI chat-completions server-sent-event stream into typed events (REQ-RAG-067 / BRD-110).
/// </summary>
/// <remarks>
/// Shared by the three providers that speak the dialect: <see cref="OpenAICompatibleLlmProvider"/>,
/// <see cref="LmStudioLlmProvider"/> and <see cref="AzureAIFoundryLlmProvider"/>. The service streams a
/// tool call as fragments keyed by <c>index</c>: the first carries the id and name, later ones append to
/// the arguments string. The fragments are assembled here and each call is emitted once, whole, after
/// the text. A server that sends a call in one chunk (older LM Studio builds) assembles trivially.
/// </remarks>
internal static class OpenAIStreamReader
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Reads the stream and yields text deltas, assembled tool calls and a completed event.</summary>
    /// <param name="reader">Reader over the response body.</param>
    /// <param name="context">Provider identity, token estimation and the completion callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The typed events of one call.</returns>
    public static async IAsyncEnumerable<LlmStreamEvent> ReadAsync(
        StreamReader reader,
        StreamReadContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolCalls = new SortedDictionary<int, ToolCallFragments>();
        var outputText = new StringBuilder();
        var usage = (Input: 0, Output: 0, Cached: 0);
        string? finishReason = null;

        // ReadLineAsync returning null is end of stream; EndOfStream would block synchronously (CA2024).
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            var chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data, ReadOptions);
            if (chunk?.Usage is not null)
            {
                usage = (chunk.Usage.PromptTokens, chunk.Usage.CompletionTokens, chunk.Usage.CachedTokens);
            }

            var choice = chunk?.Choices?.FirstOrDefault();
            finishReason = choice?.FinishReason ?? finishReason;
            AppendToolCallFragments(toolCalls, choice?.Delta?.ToolCalls);

            var delta = choice?.Delta?.Content;
            if (!string.IsNullOrEmpty(delta))
            {
                outputText.Append(delta);
                yield return LlmStreamEvent.FromText(delta);
            }
        }

        foreach (var fragments in toolCalls.Values)
        {
            yield return LlmStreamEvent.FromToolCall(fragments.ToToolCall());
        }

        var tokenUsage = context.BuildUsage(usage.Input, usage.Output, usage.Cached, outputText.ToString());
        context.OnCompleted(tokenUsage, toolCalls.Count > 0);

        var reason = finishReason ?? (toolCalls.Count > 0 ? "tool_calls" : "stop");
        yield return LlmStreamEvent.FromCompleted(tokenUsage, reason, context.ModelName);
    }

    private static void AppendToolCallFragments(SortedDictionary<int, ToolCallFragments> toolCalls, List<OpenAIToolCall>? deltas)
    {
        if (deltas is not { Count: > 0 }) return;

        for (var position = 0; position < deltas.Count; position++)
        {
            var delta = deltas[position];
            var index = delta.Index ?? position;
            if (!toolCalls.TryGetValue(index, out var fragments))
            {
                fragments = new ToolCallFragments();
                toolCalls[index] = fragments;
            }

            fragments.Append(delta);
        }
    }

    /// <summary>One tool call being assembled from its streamed fragments.</summary>
    private sealed class ToolCallFragments
    {
        private readonly StringBuilder arguments = new();
        private string? id;
        private string? name;

        public void Append(OpenAIToolCall delta)
        {
            if (!string.IsNullOrEmpty(delta.Id)) id = delta.Id;
            if (!string.IsNullOrEmpty(delta.Function?.Name)) name = delta.Function.Name;
            if (delta.Function?.Arguments is { } part) arguments.Append(part);
        }

        public ToolCall ToToolCall() => new()
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = name ?? string.Empty,
            ArgumentsJson = arguments.Length > 0 ? arguments.ToString() : "{}"
        };
    }
}
