using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Web;

/// <summary>
/// REQ-RAG-018 / BRD-62: recognising the URL shapes people actually paste. A miss here is a user
/// being told their perfectly valid link is not a YouTube video.
/// </summary>
public sealed class YouTubeUrlTests
{
    /// <summary>Every shape YouTube hands out resolves to the same id.</summary>
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42s")]
    [InlineData("https://www.youtube.com/watch?list=PL123&v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=42")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ")]
    [InlineData("  https://youtu.be/dQw4w9WgXcQ  ")]
    [InlineData("dQw4w9WgXcQ")]
    public void RecognisesEveryUrlShape(string input)
    {
        Assert.True(YouTubeUrl.TryGetVideoId(input, out var id));
        Assert.Equal("dQw4w9WgXcQ", id);
    }

    /// <summary>
    /// Non-YouTube and malformed inputs are rejected rather than producing a confident request for
    /// a video that cannot exist.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.test/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=tooshort")]
    [InlineData("https://www.youtube.com/watch?v=waaaaaaaaaaytoolong")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://www.youtube.com/watch")]
    [InlineData("https://vimeo.com/12345678")]
    [InlineData("not a url at all")]
    public void RejectsWhatIsNotAVideoUrl(string? input)
    {
        Assert.False(YouTubeUrl.TryGetVideoId(input, out _));
        Assert.False(YouTubeUrl.IsYouTube(input));
    }

    /// <summary>Ids using the full URL-safe alphabet survive intact.</summary>
    [Fact]
    public void AcceptsTheFullIdAlphabet()
    {
        Assert.True(YouTubeUrl.TryGetVideoId("https://youtu.be/aB3-_dEfG9h", out var id));
        Assert.Equal("aB3-_dEfG9h", id);
    }
}
