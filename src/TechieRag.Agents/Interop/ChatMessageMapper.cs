using System.Text.Json;
using Microsoft.Extensions.AI;
using CoreChatMessage = TechieRag.Models.ChatMessage;
using CoreContentPart = TechieRag.Models.ChatContentPart;
using CoreImage = TechieRag.Models.ChatImage;
using CoreToolCall = TechieRag.Models.ToolCall;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace TechieRag.Agents.Interop;

/// <summary>
/// Maps chat messages between TechieRag's model and Microsoft.Extensions.AI's, in both directions
/// (REQ-RAG-017 / BRD-85).
/// </summary>
/// <remarks>
/// Text, images, assistant tool calls and tool results cross the seam; anything else an
/// <see cref="AIContent"/> can carry (reasoning, approval requests, hosted-file references) has no core
/// equivalent and is dropped, which is the documented v1 posture (proposal §6 item 10).
/// </remarks>
internal static class ChatMessageMapper
{
    /// <summary>Converts MEAI messages to core messages, one core tool message per function result.</summary>
    /// <param name="messages">The MEAI messages.</param>
    /// <returns>The core messages, in order.</returns>
    public static List<CoreChatMessage> ToCore(IEnumerable<MeaiChatMessage> messages)
    {
        var result = new List<CoreChatMessage>();
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.Tool)
            {
                result.AddRange(message.Contents.OfType<FunctionResultContent>()
                    .Select(content => CoreChatMessage.Tool(content.CallId, ResultToString(content.Result))));
                continue;
            }

            result.Add(ToCoreMessage(message));
        }

        return result;
    }

    /// <summary>Converts core messages to MEAI messages.</summary>
    /// <param name="messages">The core messages.</param>
    /// <returns>The MEAI messages, in order.</returns>
    public static List<MeaiChatMessage> ToMeai(IEnumerable<CoreChatMessage> messages) =>
        messages.Select(ToMeaiMessage).ToList();

    /// <summary>Builds the MEAI function-call content for a core tool call.</summary>
    /// <param name="toolCall">The core tool call.</param>
    /// <returns>The function-call content.</returns>
    public static FunctionCallContent ToFunctionCall(CoreToolCall toolCall) =>
        new(toolCall.Id, toolCall.Name, ParseArguments(toolCall.ArgumentsJson));

    /// <summary>Parses a JSON arguments object into the dictionary MEAI uses; non-objects give an empty one.</summary>
    /// <param name="argumentsJson">The JSON arguments.</param>
    /// <returns>Name → value (values are <see cref="JsonElement"/>s).</returns>
    public static Dictionary<string, object?> ParseArguments(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return new Dictionary<string, object?>();

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, object?>();

            return document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        }
        catch (JsonException)
        {
            // A model that emits malformed arguments gets an empty set; the tool then reports the
            // missing parameter in words the model can act on, which a parser exception is not.
            return new Dictionary<string, object?>();
        }
    }

    /// <summary>Serializes a tool's arguments dictionary to a JSON object string.</summary>
    /// <param name="arguments">The arguments, or null.</param>
    /// <returns>The JSON.</returns>
    public static string SerializeArguments(IEnumerable<KeyValuePair<string, object?>>? arguments) =>
        JsonSerializer.Serialize(arguments?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<string, object?>());

    /// <summary>Renders a function result as the string a core tool message carries.</summary>
    /// <param name="result">The result object.</param>
    /// <returns>The text.</returns>
    public static string ResultToString(object? result) => result switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => JsonSerializer.Serialize(result)
    };

    private static CoreChatMessage ToCoreMessage(MeaiChatMessage message)
    {
        var text = string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));
        var toolCalls = message.Contents.OfType<FunctionCallContent>()
            .Select(call => new CoreToolCall { Id = call.CallId, Name = call.Name, ArgumentsJson = SerializeArguments(call.Arguments) })
            .ToList();
        var images = message.Contents.Select(ToImage).OfType<CoreImage>().ToList();

        var core = new CoreChatMessage
        {
            Role = message.Role.Value,
            Content = text.Length > 0 || toolCalls.Count == 0 ? text : null,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            Name = message.AuthorName
        };

        if (images.Count > 0)
        {
            var parts = new List<CoreContentPart>();
            if (text.Length > 0) parts.Add(CoreContentPart.FromText(text));
            parts.AddRange(images.Select(CoreContentPart.FromImage));
            core.Parts = parts;
        }

        return core;
    }

    private static CoreImage? ToImage(AIContent content) => content switch
    {
        DataContent data when data.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            => CoreImage.FromBytes(data.Data.ToArray(), data.MediaType),
        UriContent uri when uri.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            => CoreImage.FromUrl(uri.Uri, uri.MediaType),
        _ => null
    };

    private static MeaiChatMessage ToMeaiMessage(CoreChatMessage message)
    {
        var role = new ChatRole(message.Role);
        var contents = new List<AIContent>();

        if (message.Role == "tool")
        {
            contents.Add(new FunctionResultContent(message.ToolCallId ?? string.Empty, message.Content ?? string.Empty));
            return new MeaiChatMessage(role, contents);
        }

        if (!string.IsNullOrEmpty(message.Content)) contents.Add(new TextContent(message.Content));
        foreach (var image in (message.Parts ?? []).Select(part => part.Image).OfType<CoreImage>())
        {
            contents.Add(image.IsInline
                ? new DataContent(Convert.FromBase64String(image.Base64Data!), image.MediaType)
                : new UriContent(image.Url!, image.MediaType));
        }

        foreach (var toolCall in message.ToolCalls ?? [])
        {
            contents.Add(ToFunctionCall(toolCall));
        }

        return new MeaiChatMessage(role, contents) { AuthorName = message.Name };
    }
}
