using System.Text;
using System.Text.Json.Nodes;
using TechieRag.Models;

namespace TechieRag.Local;

/// <summary>
/// Turns a conversation into the exact prompt text a model was trained on (REQ-RAG-059 / BRD-98).
/// </summary>
/// <remarks>
/// Applied in managed code rather than by the runtime, so every platform feeds the model the same
/// characters (REQ-RAG-058). The beginning-of-text token is left to the runtime's tokenizer, which adds
/// it when the model's own configuration asks for it.
/// </remarks>
internal static class ChatTemplateFormatter
{
    /// <summary>
    /// Formats a conversation and opens the assistant's turn.
    /// </summary>
    /// <param name="template">The model's chat format.</param>
    /// <param name="messages">The conversation.</param>
    /// <returns>The prompt text.</returns>
    public static string Format(LocalChatTemplate template, IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var builder = new StringBuilder();
        var pendingSystem = template == LocalChatTemplate.Gemma ? SystemText(messages) : null;

        foreach (var message in messages)
        {
            var role = NormaliseRole(message.Role);
            if (template == LocalChatTemplate.Gemma && role == "system")
            {
                continue;
            }

            var text = TextOf(message);
            if (pendingSystem is not null && role == "user")
            {
                text = pendingSystem + "\n\n" + text;
                pendingSystem = null;
            }

            AppendTurn(builder, template, role, text);
        }

        AppendAssistantOpening(builder, template);
        return builder.ToString();
    }

    /// <summary>Gets the marker that ends a turn, which is also a stop sequence.</summary>
    /// <param name="template">The model's chat format.</param>
    /// <returns>
    /// The end-of-turn marker; null for <see cref="LocalChatTemplate.ModelDefined"/>, whose end-of-turn token
    /// is among the end tokens of the model's own <c>genai_config.json</c>, where the engine stops on it.
    /// </returns>
    public static string? EndOfTurn(LocalChatTemplate template) => template switch
    {
        LocalChatTemplate.ChatMl => "<|im_end|>",
        LocalChatTemplate.Phi3 => "<|end|>",
        LocalChatTemplate.Llama3 => "<|eot_id|>",
        LocalChatTemplate.Gemma => "<end_of_turn>",
        LocalChatTemplate.ModelDefined => null,
        _ => throw new ArgumentOutOfRangeException(nameof(template))
    };

    /// <summary>
    /// Turns a conversation into the messages a model's own chat template takes
    /// (<see cref="LocalChatTemplate.ModelDefined"/>, REQ-RAG-108): every system message joined into one
    /// first message, a tool result sent as a user turn, and consecutive messages of one role joined,
    /// because templates such as Gemma's refuse a conversation whose turns do not alternate.
    /// </summary>
    /// <param name="messages">The conversation.</param>
    /// <returns>A JSON array of <c>{"role", "content"}</c> objects.</returns>
    public static string ToMessagesJson(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var turns = new List<(string Role, string Text)>();
        if (SystemText(messages) is { } system)
        {
            turns.Add(("system", system));
        }

        foreach (var message in messages)
        {
            var role = NormaliseRole(message.Role);
            if (role == "system")
            {
                continue;
            }

            var text = TextOf(message);
            if (turns.Count > 0 && turns[^1].Role == role)
            {
                turns[^1] = (role, turns[^1].Text + "\n\n" + text);
            }
            else
            {
                turns.Add((role, text));
            }
        }

        var array = new JsonArray();
        foreach (var (role, text) in turns)
        {
            array.Add(new JsonObject { ["role"] = role, ["content"] = text });
        }

        return array.ToJsonString();
    }

    private static void AppendTurn(StringBuilder builder, LocalChatTemplate template, string role, string text)
    {
        switch (template)
        {
            case LocalChatTemplate.ChatMl:
                builder.Append("<|im_start|>").Append(role).Append('\n').Append(text).Append("<|im_end|>\n");
                break;
            case LocalChatTemplate.Phi3:
                builder.Append("<|").Append(role).Append("|>\n").Append(text).Append("<|end|>\n");
                break;
            case LocalChatTemplate.Llama3:
                builder.Append("<|start_header_id|>").Append(role).Append("<|end_header_id|>\n\n").Append(text).Append("<|eot_id|>");
                break;
            case LocalChatTemplate.Gemma:
                builder.Append("<start_of_turn>").Append(role == "assistant" ? "model" : "user").Append('\n')
                    .Append(text).Append("<end_of_turn>\n");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(template));
        }
    }

    private static void AppendAssistantOpening(StringBuilder builder, LocalChatTemplate template)
    {
        _ = template switch
        {
            LocalChatTemplate.ChatMl => builder.Append("<|im_start|>assistant\n"),
            LocalChatTemplate.Phi3 => builder.Append("<|assistant|>\n"),
            LocalChatTemplate.Llama3 => builder.Append("<|start_header_id|>assistant<|end_header_id|>\n\n"),
            LocalChatTemplate.Gemma => builder.Append("<start_of_turn>model\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };
    }

    /// <summary>A tool result becomes a user turn: none of the shipped templates has a tool role the provider uses.</summary>
    private static string NormaliseRole(string role) => role.ToLowerInvariant() switch
    {
        "system" => "system",
        "assistant" => "assistant",
        _ => "user"
    };

    private static string? SystemText(IReadOnlyList<ChatMessage> messages)
    {
        var parts = messages.Where(m => NormaliseRole(m.Role) == "system").Select(TextOf).ToList();
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    private static string TextOf(ChatMessage message)
    {
        if (!string.IsNullOrEmpty(message.Content))
        {
            return message.Content;
        }

        return message.Parts is null
            ? string.Empty
            : string.Concat(message.Parts.Where(p => p.Kind == ChatContentKind.Text).Select(p => p.Text));
    }
}
