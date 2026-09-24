using System.Text;
using System.Text.Json;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// Folds Anthropic Messages API stream events into typed events (REQ-RAG-067 / BRD-110).
/// </summary>
/// <remarks>
/// A <c>tool_use</c> block arrives as <c>content_block_start</c> (id and name), zero or more
/// <c>content_block_delta</c> events of type <c>input_json_delta</c> (argument fragments), then
/// <c>content_block_stop</c>. The call is emitted at the stop, whole. Usage arrives split: input and
/// cache tokens on <c>message_start</c>, output tokens and the stop reason on <c>message_delta</c>.
/// </remarks>
internal sealed class AnthropicStreamState
{
    private readonly StringBuilder toolArguments = new();
    private string? toolId;
    private string? toolName;

    /// <summary>Gets the streamed assistant text so far.</summary>
    public StringBuilder OutputText { get; } = new();

    /// <summary>Gets the reported input tokens.</summary>
    public int InputTokens { get; private set; }

    /// <summary>Gets the reported output tokens.</summary>
    public int OutputTokens { get; private set; }

    /// <summary>Gets the reported cache-read tokens.</summary>
    public int CacheReadTokens { get; private set; }

    /// <summary>Gets the reported cache-write tokens.</summary>
    public int CacheWriteTokens { get; private set; }

    /// <summary>Gets the reported stop reason, or null.</summary>
    public string? StopReason { get; private set; }

    /// <summary>Gets how many tool calls were emitted.</summary>
    public int ToolCallCount { get; private set; }

    /// <summary>Applies one stream event.</summary>
    /// <param name="eventData">The parsed <c>data:</c> payload.</param>
    /// <returns>A typed event to yield, or null when the payload only updates state.</returns>
    public LlmStreamEvent? Apply(JsonElement eventData)
    {
        if (!eventData.TryGetProperty("type", out var typeElement)) return null;

        switch (typeElement.GetString())
        {
            case "message_start":
                ReadStartUsage(eventData);
                return null;
            case "content_block_start":
                StartBlock(eventData);
                return null;
            case "content_block_delta":
                return ApplyDelta(eventData);
            case "content_block_stop":
                return StopBlock();
            case "message_delta":
                ReadDelta(eventData);
                return null;
            default:
                return null;
        }
    }

    private void ReadStartUsage(JsonElement eventData)
    {
        if (!eventData.TryGetProperty("message", out var message) || !message.TryGetProperty("usage", out var usage)) return;

        InputTokens = ReadInt(usage, "input_tokens");
        CacheReadTokens = ReadInt(usage, "cache_read_input_tokens");
        CacheWriteTokens = ReadInt(usage, "cache_creation_input_tokens");
    }

    private void StartBlock(JsonElement eventData)
    {
        if (!eventData.TryGetProperty("content_block", out var block)) return;
        if (!block.TryGetProperty("type", out var blockType) || blockType.GetString() != "tool_use") return;

        toolId = block.TryGetProperty("id", out var id) ? id.GetString() : null;
        toolName = block.TryGetProperty("name", out var name) ? name.GetString() : null;
        toolArguments.Clear();
    }

    private LlmStreamEvent? ApplyDelta(JsonElement eventData)
    {
        if (!eventData.TryGetProperty("delta", out var delta)) return null;

        if (delta.TryGetProperty("partial_json", out var partial))
        {
            toolArguments.Append(partial.GetString());
            return null;
        }

        if (!delta.TryGetProperty("text", out var text)) return null;

        var value = text.GetString();
        if (string.IsNullOrEmpty(value)) return null;

        OutputText.Append(value);
        return LlmStreamEvent.FromText(value);
    }

    private LlmStreamEvent? StopBlock()
    {
        if (toolName is null) return null;

        var toolCall = new ToolCall
        {
            Id = toolId ?? Guid.NewGuid().ToString(),
            Name = toolName,
            ArgumentsJson = toolArguments.Length > 0 ? toolArguments.ToString() : "{}"
        };
        toolId = null;
        toolName = null;
        toolArguments.Clear();
        ToolCallCount++;
        return LlmStreamEvent.FromToolCall(toolCall);
    }

    private void ReadDelta(JsonElement eventData)
    {
        if (eventData.TryGetProperty("delta", out var delta)
            && delta.TryGetProperty("stop_reason", out var stopReason)
            && stopReason.ValueKind == JsonValueKind.String)
        {
            StopReason = stopReason.GetString();
        }

        if (eventData.TryGetProperty("usage", out var usage))
        {
            OutputTokens = ReadInt(usage, "output_tokens");
        }
    }

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
}
