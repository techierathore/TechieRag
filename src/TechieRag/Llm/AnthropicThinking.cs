using System.Text.Json;

namespace TechieRag.Llm;

/// <summary>
/// Anthropic extended thinking: the request switch, and the signed blocks a turn hands back (REQ-RAG-139).
/// </summary>
/// <remarks>
/// <para>The service thinks only when the request carries <c>thinking</c>; it is turned on by
/// <see cref="Models.LlmCompletionOptions.MaxReasoningTokens"/> or
/// <see cref="Models.LlmCompletionOptions.ReasoningEffort"/> and kept off by
/// <see cref="Models.LlmCompletionOptions.DisableReasoning"/>.</para>
/// <para>With thinking on, the service refuses a tool-use turn whose earlier assistant message lost its
/// thinking blocks, so the blocks travel in <see cref="Models.ChatMessage.ProviderState"/> as
/// <c>{"anthropic":[…]}</c> and go back first in that message's content, unchanged.</para>
/// </remarks>
internal static class AnthropicThinking
{
    /// <summary>The smallest budget the service accepts.</summary>
    internal const int MinimumBudget = 1024;

    private const string StateKey = "anthropic";

    /// <summary>The thinking budget a call asks for, or null when it asks for no thinking.</summary>
    /// <param name="options">The call's options.</param>
    /// <returns>The <c>budget_tokens</c> value, or null.</returns>
    public static int? Budget(Models.LlmCompletionOptions? options)
    {
        if (options is null || options.DisableReasoning) return null;
        if (options.MaxReasoningTokens is > 0 and var budget) return Math.Max(budget, MinimumBudget);

        return options.ReasoningEffort?.ToLowerInvariant() switch
        {
            "low" or "minimal" => MinimumBudget,
            "medium" => 4096,
            "high" => 16384,
            _ => null
        };
    }

    /// <summary>A signed thinking block as the service sent it.</summary>
    /// <param name="thinking">The thinking text.</param>
    /// <param name="signature">The block's signature.</param>
    /// <returns>The block, ready to send back.</returns>
    public static object Block(string thinking, string signature) =>
        new Dictionary<string, object> { ["type"] = "thinking", ["thinking"] = thinking, ["signature"] = signature };

    /// <summary>Packs a turn's thinking blocks for <see cref="Models.ChatMessage.ProviderState"/>.</summary>
    /// <param name="blocks">The blocks, in order.</param>
    /// <returns>The state, or null when there are no blocks.</returns>
    public static string? ToProviderState(IReadOnlyList<object> blocks) =>
        blocks.Count == 0 ? null : JsonSerializer.Serialize(new Dictionary<string, object> { [StateKey] = blocks });

    /// <summary>Reads back the thinking blocks of an assistant message; empty when it carries none of ours.</summary>
    /// <param name="providerState">The message's provider state.</param>
    /// <returns>The blocks to put first in the message's content.</returns>
    public static IReadOnlyList<JsonElement> FromProviderState(string? providerState)
    {
        if (string.IsNullOrEmpty(providerState)) return [];

        try
        {
            using var document = JsonDocument.Parse(providerState);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(StateKey, out var blocks)
                && blocks.ValueKind == JsonValueKind.Array
                ? blocks.EnumerateArray().Select(b => b.Clone()).ToList()
                : [];
        }
        catch (JsonException)
        {
            // Another provider's state, or text that is not ours: there is nothing to send back.
            return [];
        }
    }
}
