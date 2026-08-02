namespace TechieRag.Models;

/// <summary>Represents a single message in a chat conversation.</summary>
public class ChatMessage
{
    /// <summary>Gets or sets the role of the message sender.</summary>
    public required string Role { get; set; }

    /// <summary>Gets or sets the text content of the message.</summary>
    public string? Content { get; set; }

    /// <summary>Gets or sets tool calls requested by the assistant.</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; set; }

    /// <summary>Gets or sets the tool call ID this message responds to.</summary>
    public string? ToolCallId { get; set; }

    /// <summary>Gets or sets the display name of the sender.</summary>
    public string? Name { get; set; }

    /// <summary>Gets the timestamp when this message was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets the multimodal parts of this message (REQ-RAG-039 / BRD-120).</summary>
    /// <remarks>
    /// <para>Null for an ordinary text message, which is the overwhelming majority and stays on the
    /// cheaper single-string wire shape. When set, <see cref="Parts"/> is authoritative and
    /// <see cref="Content"/> is ignored by the providers — but <see cref="Content"/> is still
    /// populated by <see cref="UserWithImages"/> with the text so that conversation stores, token
    /// estimators and log lines that only know about strings keep working unchanged.</para>
    /// </remarks>
    public IReadOnlyList<ChatContentPart>? Parts { get; set; }

    /// <summary>Gets or sets whether the cacheable prefix ends at this message (REQ-RAG-043 / BRD-124).</summary>
    /// <remarks>
    /// A breakpoint, not a request to cache this message alone: providers cache everything up to and
    /// including the marked point. Honoured only when <see cref="LlmCompletionOptions.PromptCache"/>
    /// is also set, so a message can carry the mark harmlessly through a provider that has no caching.
    /// </remarks>
    public bool CacheBoundary { get; set; }

    /// <summary>Gets whether this message carries at least one image part.</summary>
    public bool HasImages => Parts is not null && Parts.Any(part => part.Kind == ChatContentKind.Image);

    /// <summary>Creates a system message.</summary>
    public static ChatMessage System(string content) => new() { Role = "system", Content = content };

    /// <summary>Creates a user message.</summary>
    public static ChatMessage User(string content) => new() { Role = "user", Content = content };

    /// <summary>Creates a user message carrying text and one or more images (REQ-RAG-039).</summary>
    /// <param name="text">The text prompt. May be empty when the images are the whole question.</param>
    /// <param name="images">The images, in the order the model should see them.</param>
    /// <returns>A multimodal user message.</returns>
    /// <exception cref="ArgumentException">No images were supplied.</exception>
    public static ChatMessage UserWithImages(string text, params ChatImage[] images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Length == 0)
        {
            throw new ArgumentException(
                "UserWithImages needs at least one image. Use ChatMessage.User for a text-only message.",
                nameof(images));
        }

        var parts = new List<ChatContentPart>(images.Length + 1);

        // Text first. Vision models weight a question asked before the image differently from one
        // asked after it, and "here is my question, here is the picture" is the order humans write in.
        if (!string.IsNullOrEmpty(text))
        {
            parts.Add(ChatContentPart.FromText(text));
        }

        foreach (var image in images)
        {
            parts.Add(ChatContentPart.FromImage(image));
        }

        return new ChatMessage { Role = "user", Content = text, Parts = parts };
    }

    /// <summary>Creates an assistant message.</summary>
    public static ChatMessage Assistant(string content) => new() { Role = "assistant", Content = content };

    /// <summary>Creates a tool result message.</summary>
    public static ChatMessage Tool(string toolCallId, string content) =>
        new() { Role = "tool", ToolCallId = toolCallId, Content = content };
}
