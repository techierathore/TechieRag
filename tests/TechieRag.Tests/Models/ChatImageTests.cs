using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Models;

/// <summary>
/// Unit tests for the multimodal chat input model (REQ-RAG-039 / BRD-120).
/// </summary>
public class ChatImageTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>Raw bytes become base64 the providers can embed directly.</summary>
    [Fact]
    public void FromBytesEncodesBase64()
    {
        var image = ChatImage.FromBytes(PngBytes, "image/png");

        Assert.True(image.IsInline);
        Assert.Equal(Convert.ToBase64String(PngBytes), image.Base64Data);
        Assert.Equal("image/png", image.MediaType);
        Assert.Null(image.Url);
    }

    /// <summary>An empty byte array is a caller bug, not an empty image.</summary>
    [Fact]
    public void FromBytesRejectsEmptyBytes()
    {
        Assert.Throws<ArgumentException>(() => ChatImage.FromBytes([], "image/png"));
    }

    /// <summary>A non-image media type is caught at construction, not by the provider's 400.</summary>
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("audio/wav")]
    [InlineData("text/plain")]
    public void MediaTypeMustBeAnImage(string mediaType)
    {
        Assert.Throws<ArgumentException>(() => ChatImage.FromBytes(PngBytes, mediaType));
    }

    /// <summary>A pasted data URI is accepted and its prefix stripped rather than double-encoded.</summary>
    [Fact]
    public void FromBase64StripsADataUriPrefix()
    {
        var image = ChatImage.FromBase64("data:image/png;base64,QUJD", "image/png");

        Assert.Equal("QUJD", image.Base64Data);
    }

    /// <summary>Bare base64 passes through untouched.</summary>
    [Fact]
    public void FromBase64KeepsBarePayload()
    {
        var image = ChatImage.FromBase64("QUJD", "image/png");

        Assert.Equal("QUJD", image.Base64Data);
    }

    /// <summary>A URL image carries no bytes and is not inline.</summary>
    [Fact]
    public void FromUrlIsNotInline()
    {
        var image = ChatImage.FromUrl(new Uri("https://example.com/cat.png"), "image/png");

        Assert.False(image.IsInline);
        Assert.Null(image.Base64Data);
        Assert.Equal("https://example.com/cat.png", image.Url!.ToString());
    }

    /// <summary>A file path is a local read no provider can perform, so it is refused.</summary>
    [Fact]
    public void FromUrlRejectsNonHttpSchemes()
    {
        Assert.Throws<ArgumentException>(
            () => ChatImage.FromUrl(new Uri("file:///tmp/cat.png"), "image/png"));
    }

    /// <summary>A relative URL cannot be resolved by a remote provider.</summary>
    [Fact]
    public void FromUrlRejectsRelativeUris()
    {
        Assert.Throws<ArgumentException>(
            () => ChatImage.FromUrl(new Uri("/cat.png", UriKind.Relative), "image/png"));
    }

    /// <summary>Inline images render to the data URI the OpenAI dialect expects.</summary>
    [Fact]
    public void ToDataUriRendersInlineBytes()
    {
        var image = ChatImage.FromBase64("QUJD", "image/jpeg");

        Assert.Equal("data:image/jpeg;base64,QUJD", image.ToDataUri());
    }

    /// <summary>A URL image has no bytes to encode, so asking for a data URI is a programming error.</summary>
    [Fact]
    public void ToDataUriThrowsForUrlImages()
    {
        var image = ChatImage.FromUrl(new Uri("https://example.com/cat.png"), "image/png");

        Assert.Throws<InvalidOperationException>(() => image.ToDataUri());
    }
}
