namespace TechieRag.Models;

/// <summary>The modality of one part of a multimodal chat message (REQ-RAG-039 / BRD-120).</summary>
/// <remarks>
/// Vision ships first, so <see cref="Image"/> is the only non-text member today. Audio and document
/// parts are added here as new members rather than as new interfaces, which is why provider capability
/// is reported through <see cref="Abstractions.IMultimodalLlmProvider.SupportsInput"/> taking this enum
/// instead of through a per-modality boolean property: a new modality then costs no breaking change.
/// </remarks>
public enum ChatContentKind
{
    /// <summary>Plain text.</summary>
    Text = 0,

    /// <summary>A still image (REQ-RAG-039, vision).</summary>
    Image = 1
}

/// <summary>One part of a multimodal chat message (REQ-RAG-039 / BRD-120).</summary>
/// <remarks>
/// A message with <see cref="ChatMessage.Parts"/> set carries an ordered sequence of these. Order is
/// preserved on the wire because it is meaningful: "what is wrong in this photo?" placed before or
/// after the image is a different prompt to most vision models.
/// </remarks>
public sealed class ChatContentPart
{
    /// <summary>Gets which modality this part carries.</summary>
    public required ChatContentKind Kind { get; init; }

    /// <summary>Gets the text, when <see cref="Kind"/> is <see cref="ChatContentKind.Text"/>.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the image, when <see cref="Kind"/> is <see cref="ChatContentKind.Image"/>.</summary>
    public ChatImage? Image { get; init; }

    /// <summary>Creates a text part.</summary>
    /// <param name="text">The text.</param>
    /// <returns>A text part.</returns>
    public static ChatContentPart FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new ChatContentPart { Kind = ChatContentKind.Text, Text = text };
    }

    /// <summary>Creates an image part.</summary>
    /// <param name="image">The image.</param>
    /// <returns>An image part.</returns>
    public static ChatContentPart FromImage(ChatImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new ChatContentPart { Kind = ChatContentKind.Image, Image = image };
    }
}
