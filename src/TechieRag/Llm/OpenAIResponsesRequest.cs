using System.Text.Json;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// Builds an OpenAI Responses-API request body from a chat conversation (REQ-RAG-069).
/// </summary>
/// <remarks>
/// <para><b>Why not the chat-completions body:</b> the Codex backend that serves a ChatGPT subscription
/// speaks only the Responses API. System messages become <c>instructions</c> (required there), user and
/// assistant turns become <c>message</c> items, an assistant tool call becomes a <c>function_call</c> item
/// and a tool result a <c>function_call_output</c> item.</para>
/// <para><b>What is not sent:</b> sampling settings (temperature, max tokens, penalties, seed) and JSON
/// mode. The subscription backend fixes them per plan, and a request carrying them risks refusal;
/// <see cref="LlmCompletionOptions.JsonMode"/> is honoured by the instruction the caller already adds.
/// Images are not sent either: this provider is text and tools only.</para>
/// </remarks>
internal static class OpenAIResponsesRequest
{
    /// <summary>Builds the body.</summary>
    /// <param name="messages">The conversation.</param>
    /// <param name="options">Per-call options; model, system prompt, tools and tool choice are honoured.</param>
    /// <param name="model">The provider's model when the options name none.</param>
    /// <param name="defaultInstructions">Instructions used when the conversation has no system text.</param>
    /// <returns>The request body, ready to serialise.</returns>
    public static Dictionary<string, object> Build(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options,
        string model,
        string defaultInstructions)
    {
        var request = new Dictionary<string, object>
        {
            ["model"] = options?.Model ?? model,
            ["instructions"] = BuildInstructions(messages, options, defaultInstructions),
            ["input"] = messages.Where(m => m.Role != "system").SelectMany(ToInputItems).ToList(),
            ["stream"] = true,
            ["store"] = false
        };

        if (options?.Tools is not { Count: > 0 }) return request;

        request["tools"] = options.Tools.Select(tool => new Dictionary<string, object>
        {
            ["type"] = "function",
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = JsonSerializer.Deserialize<JsonElement>(tool.ParametersSchema)
        }).ToList();

        if (options.ToolChoice is not null) request["tool_choice"] = options.ToolChoice;
        return request;
    }

    private static string BuildInstructions(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options, string defaultInstructions)
    {
        var parts = messages
            .Where(m => m.Role == "system" && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => m.Content!)
            .Prepend(options?.SystemPrompt)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct()
            .ToList();

        return parts.Count > 0 ? string.Join("\n\n", parts) : defaultInstructions;
    }

    private static IEnumerable<object> ToInputItems(ChatMessage message)
    {
        if (message.Role == "tool")
        {
            yield return new Dictionary<string, object>
            {
                ["type"] = "function_call_output",
                ["call_id"] = message.ToolCallId ?? string.Empty,
                ["output"] = message.Content ?? string.Empty
            };
            yield break;
        }

        var text = TextOf(message);
        var isAssistant = message.Role == "assistant";
        if (!string.IsNullOrEmpty(text))
        {
            yield return new Dictionary<string, object>
            {
                ["type"] = "message",
                ["role"] = isAssistant ? "assistant" : "user",
                ["content"] = new[] { new Dictionary<string, object> { ["type"] = isAssistant ? "output_text" : "input_text", ["text"] = text } }
            };
        }

        foreach (var call in message.ToolCalls ?? [])
        {
            yield return new Dictionary<string, object>
            {
                ["type"] = "function_call",
                ["call_id"] = call.Id,
                ["name"] = call.Name,
                ["arguments"] = call.ArgumentsJson
            };
        }
    }

    private static string? TextOf(ChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.Content) || message.Parts is not { Count: > 0 }) return message.Content;

        var texts = message.Parts.Where(part => part.Kind == ChatContentKind.Text).Select(part => part.Text);
        return string.Join("\n", texts);
    }
}
