using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// Reads an OpenAI Responses-API server-sent-event stream into typed events (REQ-RAG-069, contract of
/// REQ-RAG-067 / BRD-110).
/// </summary>
/// <remarks>
/// <para><b>Not <see cref="OpenAIStreamReader"/>:</b> that reader parses chat-completions chunks
/// (<c>choices[].delta</c>, tool calls in <c>index</c>-keyed fragments). The Responses API streams named
/// events instead — <c>response.output_text.delta</c> for text, <c>response.output_item.done</c> carrying a
/// whole <c>function_call</c>, and <c>response.completed</c> with usage — so the wire format does not match
/// and the shared piece is <see cref="StreamReadContext"/>, which both use for usage and telemetry.</para>
/// <para><b>Order kept:</b> text deltas as they arrive, then each tool call, then exactly one completed event.</para>
/// </remarks>
internal static class OpenAIResponsesStreamReader
{
    /// <summary>Reads the stream and yields text deltas, tool calls and a completed event.</summary>
    /// <param name="reader">Reader over the response body.</param>
    /// <param name="context">Provider identity, token estimation and the completion callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The typed events of one call.</returns>
    /// <exception cref="InvalidOperationException">The service reported the response failed.</exception>
    public static async IAsyncEnumerable<LlmStreamEvent> ReadAsync(
        StreamReader reader,
        StreamReadContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = new ResponseState();

        while (!state.Finished && await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line["data:".Length..].Trim();
            if (data.Length == 0 || data == "[DONE]") continue;

            var delta = state.Apply(data);
            if (!string.IsNullOrEmpty(delta)) yield return LlmStreamEvent.FromText(delta);
        }

        foreach (var call in state.ToolCalls)
        {
            yield return LlmStreamEvent.FromToolCall(call);
        }

        var usage = context.BuildUsage(state.InputTokens, state.OutputTokens, state.CachedTokens, state.OutputText.ToString());
        context.OnCompleted(usage, state.ToolCalls.Count > 0);
        yield return LlmStreamEvent.FromCompleted(usage, state.FinishReason, context.ModelName);
    }

    /// <summary>What has been read of one response so far.</summary>
    private sealed class ResponseState
    {
        public List<ToolCall> ToolCalls { get; } = [];
        public StringBuilder OutputText { get; } = new();
        public int InputTokens { get; private set; }
        public int OutputTokens { get; private set; }
        public int CachedTokens { get; private set; }
        public bool Finished { get; private set; }
        public bool Incomplete { get; private set; }

        public string FinishReason => ToolCalls.Count > 0 ? "tool_calls" : Incomplete ? "length" : "stop";

        /// <summary>Applies one event and returns its text delta, if it has one.</summary>
        public string? Apply(string data)
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;

            switch (type)
            {
                case "response.output_text.delta":
                    var delta = root.TryGetProperty("delta", out var text) ? text.GetString() : null;
                    OutputText.Append(delta);
                    return delta;
                case "response.output_item.done":
                    AddToolCall(root);
                    return null;
                case "response.completed":
                case "response.incomplete":
                    Incomplete = type == "response.incomplete";
                    ReadUsage(root);
                    Finished = true;
                    return null;
                case "response.failed":
                case "error":
                    throw new InvalidOperationException($"The subscription model call failed: {ReadError(root)}.");
                default:
                    return null;
            }
        }

        private void AddToolCall(JsonElement root)
        {
            if (!root.TryGetProperty("item", out var item) || ReadString(item, "type") != "function_call") return;

            ToolCalls.Add(new ToolCall
            {
                Id = ReadString(item, "call_id") ?? ReadString(item, "id") ?? Guid.NewGuid().ToString(),
                Name = ReadString(item, "name") ?? string.Empty,
                ArgumentsJson = ReadString(item, "arguments") is { Length: > 0 } arguments ? arguments : "{}"
            });
        }

        private void ReadUsage(JsonElement root)
        {
            if (!root.TryGetProperty("response", out var response) || !response.TryGetProperty("usage", out var usage)) return;
            if (usage.ValueKind != JsonValueKind.Object) return;

            InputTokens = ReadInt(usage, "input_tokens");
            OutputTokens = ReadInt(usage, "output_tokens");
            if (usage.TryGetProperty("input_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object)
            {
                CachedTokens = ReadInt(details, "cached_tokens");
            }
        }

        private static string ReadError(JsonElement root)
        {
            var error = root.TryGetProperty("response", out var response) && response.TryGetProperty("error", out var nested)
                ? nested
                : root.TryGetProperty("error", out var direct) ? direct : root;

            return ReadString(error, "code") ?? ReadString(error, "message") ?? "unknown error";
        }

        private static string? ReadString(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static int ReadInt(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    }
}
