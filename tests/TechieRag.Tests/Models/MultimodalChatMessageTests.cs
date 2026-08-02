using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Models;

/// <summary>
/// Unit tests for multimodal message construction (REQ-RAG-039 / BRD-120).
/// </summary>
public class MultimodalChatMessageTests
{
    private static ChatImage SampleImage() => ChatImage.FromBase64("QUJD", "image/png");

    /// <summary>Text leads, images follow, in the order the caller supplied them.</summary>
    [Fact]
    public void UserWithImagesOrdersTextBeforeImages()
    {
        var first = ChatImage.FromBase64("AAA", "image/png");
        var second = ChatImage.FromBase64("BBB", "image/jpeg");

        var message = ChatMessage.UserWithImages("What differs?", first, second);

        Assert.NotNull(message.Parts);
        Assert.Equal(3, message.Parts!.Count);
        Assert.Equal(ChatContentKind.Text, message.Parts[0].Kind);
        Assert.Equal("What differs?", message.Parts[0].Text);
        Assert.Equal(ChatContentKind.Image, message.Parts[1].Kind);
        Assert.Equal("AAA", message.Parts[1].Image!.Base64Data);
        Assert.Equal("BBB", message.Parts[2].Image!.Base64Data);
    }

    /// <summary>
    /// Content still carries the text so that conversation stores, token estimators and log lines
    /// that only understand strings keep working against a multimodal message.
    /// </summary>
    [Fact]
    public void UserWithImagesStillPopulatesContent()
    {
        var message = ChatMessage.UserWithImages("Describe this", SampleImage());

        Assert.Equal("Describe this", message.Content);
    }

    /// <summary>An images-only question is legitimate and produces no empty text part.</summary>
    [Fact]
    public void EmptyTextProducesNoTextPart()
    {
        var message = ChatMessage.UserWithImages(string.Empty, SampleImage());

        var part = Assert.Single(message.Parts!);
        Assert.Equal(ChatContentKind.Image, part.Kind);
    }

    /// <summary>Calling the multimodal factory with no images is a mistake worth naming.</summary>
    [Fact]
    public void UserWithImagesRequiresAtLeastOneImage()
    {
        Assert.Throws<ArgumentException>(() => ChatMessage.UserWithImages("hello"));
    }

    /// <summary>A plain text message reports no images and keeps the cheap wire shape.</summary>
    [Fact]
    public void PlainTextMessageHasNoParts()
    {
        var message = ChatMessage.User("hello");

        Assert.Null(message.Parts);
        Assert.False(message.HasImages);
    }

    /// <summary>A multimodal message advertises its images.</summary>
    [Fact]
    public void MultimodalMessageReportsHasImages()
    {
        Assert.True(ChatMessage.UserWithImages("x", SampleImage()).HasImages);
    }

    /// <summary>Cache breakpoints default off so an ordinary message never claims one.</summary>
    [Fact]
    public void CacheBoundaryDefaultsToFalse()
    {
        Assert.False(ChatMessage.User("hello").CacheBoundary);
    }
}
