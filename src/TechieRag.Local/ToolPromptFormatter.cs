using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TechieRag.Models;

namespace TechieRag.Local;

/// <summary>
/// Prompt engineering and output parsing that lets the built-in catalogue models call tools without
/// runtime support for tool constraints (REQ-RAG-063 / BRD-103, TR-RAG-052).
/// </summary>
/// <remarks>
/// <para><b>Qwen2.5</b> uses the Hermes-style ChatML tool convention: tool definitions are listed in
/// a <c>&lt;tools&gt;</c> block in the system prompt, the model answers with one or more
/// <c>&lt;tool_call&gt;{"name":...,"arguments":...}&lt;/tool_call&gt;</c> blocks, and tool results come
/// back as <c>&lt;tool_response&gt;</c> blocks.</para>
/// <para><b>Phi-3</b> is driven through JSON mode: the system prompt asks for either plain text or a
/// JSON object <c>{"tool_calls":[...]}</c>, and the provider parses that object.</para>
/// </remarks>
internal static class ToolPromptFormatter
{
    private const string ToolsOpen = "<tools>";
    private const string ToolsClose = "</tools>";
    private const string ToolCallOpen = "<tool_call>";
    private const string ToolCallClose = "</tool_call>";
    private const string ToolResponseOpen = "<tool_response>";
    private const string ToolResponseClose = "</tool_response>";

    /// <summary>
    /// Gets whether the chat template is one the provider knows how to tool-prompt.
    /// </summary>
    /// <param name="template">The template.</param>
    /// <returns>True when tool prompting is implemented for this template.</returns>
    public static bool CanFormat(LocalChatTemplate template) => template is LocalChatTemplate.ChatMl or LocalChatTemplate.Phi3;

    /// <summary>
    /// Builds the extra system instruction that tells a supported model how to emit tool calls.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="tools">The available tools.</param>
    /// <returns>The instruction text to add to the system prompt.</returns>
    /// <exception cref="NotSupportedException">The model's template has no tool-prompt format.</exception>
    public static string BuildToolInstruction(LocalModel model, IReadOnlyList<ToolDefinition> tools)
    {
        if (!CanFormat(model.ChatTemplate))
        {
            throw new NotSupportedException(
                $"The local model '{model.Id}' uses the {model.ChatTemplate} chat template, which has no built-in tool prompt format.");
        }

        var toolsJson = string.Join(
            ",\n",
            tools.Select(t => JsonSerializer.Serialize(new
            {
                name = t.Name,
                description = t.Description,
                parameters = JsonSerializer.Deserialize<JsonNode>(t.ParametersSchema)
            })));

        return model.ChatTemplate switch
        {
            LocalChatTemplate.ChatMl =>
                "You are a helpful assistant with access to tools.\n" +
                $"{ToolsOpen}\n" +
                $"[\n{toolsJson}\n]\n" +
                $"{ToolsClose}\n\n" +
                "When you need to use a tool, respond ONLY with one or more tool calls in this exact format:\n" +
                $"{ToolCallOpen}\n" +
                "{\"name\": \"tool_name\", \"arguments\": {...}}\n" +
                $"{ToolCallClose}\n" +
                $"If you need to call multiple tools, output one {ToolCallOpen}...{ToolCallClose} block per tool.\n" +
                "If no tool is needed, respond normally with plain text.",
            LocalChatTemplate.Phi3 =>
                "You are a helpful assistant with access to tools.\n" +
                $"{ToolsOpen}\n" +
                $"[\n{toolsJson}\n]\n" +
                $"{ToolsClose}\n\n" +
                "When you need to use a tool, respond ONLY with valid JSON in this exact format:\n" +
                "{\"tool_calls\":[{\"name\":\"tool_name\",\"arguments\":{...}}]}\n" +
                "If no tool is needed, respond normally with plain text.",
            _ => throw new NotSupportedException($"Template {model.ChatTemplate} is not supported for tool prompting.")
        };
    }

    /// <summary>
    /// Wraps a tool result so the model can match it to the tool call it made.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="toolCallId">The id of the call this result answers (ignored by the local prompt format).</param>
    /// <param name="toolName">The name of the tool that was called.</param>
    /// <param name="content">The result text.</param>
    /// <returns>The user-turn text to place in the conversation.</returns>
    public static string FormatToolResult(LocalModel model, string toolCallId, string toolName, string content)
    {
        if (!CanFormat(model.ChatTemplate))
        {
            return $"Result of {toolName}: {content}";
        }

        return model.ChatTemplate switch
        {
            LocalChatTemplate.ChatMl => FormatQwenToolResponse(toolCallId, toolName, content),
            LocalChatTemplate.Phi3 => string.IsNullOrEmpty(toolName)
                ? $"The result of call {toolCallId} is: {content}"
                : $"The result of tool '{toolName}' (call {toolCallId}) is: {content}",
            _ => $"Result of {toolName}: {content}"
        };
    }

    private static string FormatQwenToolResponse(string toolCallId, string toolName, string content)
    {
        var payload = new Dictionary<string, object> { ["content"] = content };
        if (!string.IsNullOrEmpty(toolName))
        {
            payload["name"] = toolName;
        }

        if (!string.IsNullOrEmpty(toolCallId))
        {
            payload["call_id"] = toolCallId;
        }

        var result = JsonSerializer.Serialize(payload);
        return $"{ToolResponseOpen}\n{result}\n{ToolResponseClose}";
    }

    /// <summary>
    /// Parses a model's raw output into tool calls when it followed the tool prompt format.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="content">The generated text.</param>
    /// <returns>The tool calls, or null when the output is plain text.</returns>
    public static List<ToolCall>? ParseToolCalls(LocalModel model, string content)
    {
        if (string.IsNullOrWhiteSpace(content) || !CanFormat(model.ChatTemplate))
        {
            return null;
        }

        return model.ChatTemplate switch
        {
            LocalChatTemplate.ChatMl => ParseChatMlToolCalls(content),
            LocalChatTemplate.Phi3 => ParsePhi3ToolCalls(content),
            _ => null
        };
    }

    private static List<ToolCall>? ParseChatMlToolCalls(string content)
    {
        var calls = new List<ToolCall>();
        var span = content.AsSpan();
        while (true)
        {
            var openIndex = span.IndexOf(ToolCallOpen.AsSpan(), StringComparison.Ordinal);
            if (openIndex < 0)
            {
                break;
            }

            span = span[(openIndex + ToolCallOpen.Length)..];
            var closeIndex = span.IndexOf(ToolCallClose.AsSpan(), StringComparison.Ordinal);
            if (closeIndex < 0)
            {
                // Malformed: treat the remainder as a single tool call attempt.
                closeIndex = span.Length;
            }

            var json = span[..closeIndex].Trim().ToString();
            span = closeIndex < span.Length ? span[(closeIndex + ToolCallClose.Length)..] : ReadOnlySpan<char>.Empty;

            if (TryParseToolCallJson(json, out var parsed))
            {
                calls.AddRange(parsed);
            }
        }

        return calls.Count > 0 ? calls : null;
    }

    private static List<ToolCall>? ParsePhi3ToolCalls(string content)
    {
        var trimmed = StripCodeFence(content).Trim();
        if (TryParseToolCallJson(trimmed, out var parsed))
        {
            return parsed.Count > 0 ? parsed : null;
        }

        return null;
    }

    private static bool TryParseToolCallJson(string json, out List<ToolCall> calls)
    {
        calls = [];
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (TryReadToolCall(element, out var call))
                    {
                        calls.Add(call);
                    }
                }
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("tool_calls", out var toolCallsElement)
                    && toolCallsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in toolCallsElement.EnumerateArray())
                    {
                        if (TryReadToolCall(element, out var call))
                        {
                            calls.Add(call);
                        }
                    }
                }
                else if (TryReadToolCall(document.RootElement, out var singleCall))
                {
                    calls.Add(singleCall);
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return calls.Count > 0;
    }

    private static bool TryReadToolCall(JsonElement element, out ToolCall call)
    {
        call = null!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var name = element.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : element.TryGetProperty("function", out var functionElement) && functionElement.ValueKind == JsonValueKind.Object
                ? functionElement.TryGetProperty("name", out var fnName)
                    ? fnName.GetString()
                    : null
                : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string arguments;
        if (element.TryGetProperty("arguments", out var argumentsElement))
        {
            arguments = argumentsElement.ValueKind == JsonValueKind.Object
                ? argumentsElement.GetRawText()
                : argumentsElement.GetString() ?? "{}";
        }
        else if (element.TryGetProperty("function", out var functionArgsElement) && functionArgsElement.ValueKind == JsonValueKind.Object
                 && functionArgsElement.TryGetProperty("arguments", out var fnArguments))
        {
            arguments = fnArguments.ValueKind == JsonValueKind.Object
                ? fnArguments.GetRawText()
                : fnArguments.GetString() ?? "{}";
        }
        else
        {
            arguments = "{}";
        }

        call = new ToolCall
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            ArgumentsJson = arguments
        };
        return true;
    }

    private static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        trimmed = firstNewline > 0 ? trimmed[(firstNewline + 1)..] : trimmed[3..];
        return (trimmed.EndsWith("```", StringComparison.Ordinal) ? trimmed[..^3] : trimmed).Trim();
    }
}
