using System.Text.Json;
using System.Text.RegularExpressions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Reads a tool request that a model wrote as text instead of as a structured tool call, so the agent loop runs it
/// rather than returning it as the answer (REQ-RAG-159, Sevak TR-RAG-062).
/// </summary>
/// <remarks>
/// <para><b>What it reads.</b> Small local models often answer a tool prompt with the request itself as text: a bare
/// JSON object (<c>{"name": "rag-search", "arguments": {...}}</c>), the same inside a code fence, or inside
/// <c>&lt;tool_call&gt;</c> tags. The object may also be <c>{"tool_calls": [...]}</c>, an array of calls, or the
/// OpenAI shape <c>{"function": {"name", "arguments"}}</c>; <c>parameters</c> is read as <c>arguments</c>, and
/// arguments written as a JSON string are unwrapped.</para>
/// <para><b>What it never reads.</b> A request is taken only when it names one of the tools the loop offered; JSON
/// that names no offered tool stays the model's answer. Prose with a JSON object somewhere inside it is not a tool
/// request; only the three forms above are.</para>
/// </remarks>
internal static partial class TextToolCallReader
{
    private const string TagOpen = "<tool_call>";
    private const string Fence = "```";

    /// <summary>Reads the tool requests written in a model's answer.</summary>
    /// <param name="text">The answer text.</param>
    /// <param name="tools">The tools the loop offered on this call.</param>
    /// <returns>The calls, each with a fresh id and the offered tool's exact name; null when the text holds no request for an offered tool.</returns>
    internal static IReadOnlyList<ToolCall>? Read(string? text, IReadOnlyList<ToolDefinition>? tools)
    {
        if (string.IsNullOrWhiteSpace(text) || tools is not { Count: > 0 })
        {
            return null;
        }

        var bodies = new List<string>();
        var tagged = TaggedPattern().Matches(text);
        if (tagged.Count > 0)
        {
            bodies.AddRange(tagged.Select(match => match.Groups["body"].Value));
        }
        else
        {
            var fenced = FencedPattern().Matches(text);
            if (fenced.Count > 0)
            {
                bodies.AddRange(fenced.Select(match => match.Groups["body"].Value));
            }
            else
            {
                bodies.Add(text);
            }
        }

        var calls = new List<ToolCall>();
        foreach (var body in bodies)
        {
            ReadJson(body.Trim(), tools, calls);
        }

        return calls.Count > 0 ? calls : null;
    }

    /// <summary>
    /// Returns whether text streamed so far may still turn out to be a written tool request, so the streaming loop
    /// holds it back instead of showing it.
    /// </summary>
    /// <param name="textSoFar">The turn's text so far.</param>
    /// <returns><see langword="true"/> while the text is empty or starts like one of the forms <see cref="Read"/> takes.</returns>
    internal static bool MayBeToolCall(string textSoFar)
    {
        var start = textSoFar.TrimStart();
        if (start.Length == 0 || start[0] is '{' or '[')
        {
            return true;
        }

        return start[0] switch
        {
            '`' => start.Length < Fence.Length ? Fence.StartsWith(start, StringComparison.Ordinal) : start.StartsWith(Fence, StringComparison.Ordinal),
            '<' => start.Length < TagOpen.Length ? TagOpen.StartsWith(start, StringComparison.OrdinalIgnoreCase) : start.StartsWith(TagOpen, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>Reads the calls in one JSON text; text that is not JSON adds nothing.</summary>
    private static void ReadJson(string json, IReadOnlyList<ToolDefinition> tools, List<ToolCall> calls)
    {
        if (json.Length == 0 || json[0] is not ('{' or '['))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    ReadCall(element, tools, calls);
                }
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("tool_calls", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in list.EnumerateArray())
                {
                    ReadCall(element, tools, calls);
                }
            }
            else
            {
                ReadCall(root, tools, calls);
            }
        }
        catch (JsonException)
        {
            // Not JSON: the text is the model's answer.
        }
    }

    /// <summary>Reads one call object; adds it only when it names an offered tool.</summary>
    private static void ReadCall(JsonElement element, IReadOnlyList<ToolDefinition> tools, List<ToolCall> calls)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var source = element.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object ? function : element;
        if (!source.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var name = nameElement.GetString();
        var tool = tools.FirstOrDefault(definition => string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            return;
        }

        calls.Add(new ToolCall
        {
            Id = "text_" + Guid.NewGuid().ToString("N"),
            Name = tool.Name,
            ArgumentsJson = Arguments(source)
        });
    }

    /// <summary>The call's arguments as a JSON object text.</summary>
    private static string Arguments(JsonElement call)
    {
        if (!call.TryGetProperty("arguments", out var arguments) && !call.TryGetProperty("parameters", out arguments))
        {
            return "{}";
        }

        if (arguments.ValueKind == JsonValueKind.Object)
        {
            return arguments.GetRawText();
        }

        if (arguments.ValueKind == JsonValueKind.String && arguments.GetString() is { } text)
        {
            try
            {
                using var inner = JsonDocument.Parse(text);
                return inner.RootElement.ValueKind == JsonValueKind.Object ? inner.RootElement.GetRawText() : "{}";
            }
            catch (JsonException)
            {
                return "{}";
            }
        }

        return "{}";
    }

    /// <summary>A <c>&lt;tool_call&gt;</c> block; the closing tag may be missing at the end of the text.</summary>
    [GeneratedRegex(@"<tool_call>(?<body>.*?)(?:</tool_call>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TaggedPattern();

    /// <summary>A code fence, with or without a language word such as <c>json</c>.</summary>
    [GeneratedRegex(@"```[A-Za-z_]*[ \t]*\r?\n?(?<body>.*?)```", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FencedPattern();
}
