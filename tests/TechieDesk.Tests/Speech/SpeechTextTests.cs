using TechieDesk.Services.Speech;
using Xunit;

namespace TechieDesk.Tests.Speech;

/// <summary>
/// Unit tests for <see cref="SpeechText"/>, which prepares an assistant response for read-aloud
/// (REQ-UI-036 / BRD-88).
/// </summary>
public class SpeechTextTests
{
    /// <summary>Verifies empty input produces nothing to speak rather than a stray utterance.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void EmptyInputProducesNothing(string? markdown)
    {
        Assert.Equal(string.Empty, SpeechText.ForSpeech(markdown));
    }

    /// <summary>Verifies a fenced code block is replaced rather than read out character by character.</summary>
    [Fact]
    public void FencedCodeBlockIsReplacedWithAPlaceholder()
    {
        const string markdown = "Run this:\n```bash\ndotnet build --no-restore\n```\nThen retry.";

        var spoken = SpeechText.ForSpeech(markdown);

        Assert.Contains(SpeechText.CodeBlockPlaceholder, spoken);
        Assert.DoesNotContain("dotnet build", spoken);
        Assert.DoesNotContain("```", spoken);
        Assert.EndsWith("Then retry.", spoken);
    }

    /// <summary>Verifies an unterminated code fence still swallows the rest of the answer.</summary>
    [Fact]
    public void UnterminatedCodeFenceIsStillReplaced()
    {
        var spoken = SpeechText.ForSpeech("Here:\n```\nnever closed");

        Assert.Contains(SpeechText.CodeBlockPlaceholder, spoken);
        Assert.DoesNotContain("never closed", spoken);
    }

    /// <summary>Verifies a markdown link is spoken as its label, never as its URL.</summary>
    [Fact]
    public void MarkdownLinkIsSpokenAsItsLabel()
    {
        var spoken = SpeechText.ForSpeech("See [the architecture doc](https://example.com/a/b?c=1).");

        Assert.Equal("See the architecture doc.", spoken);
    }

    /// <summary>Verifies heading, list and blockquote markers are dropped.</summary>
    [Fact]
    public void StructuralMarkersAreDropped()
    {
        const string markdown = "## Findings\n\n- first point\n- second point\n\n> a quoted line";

        var spoken = SpeechText.ForSpeech(markdown);

        Assert.Equal("Findings first point second point a quoted line", spoken);
    }

    /// <summary>Verifies emphasis and inline-code markers are dropped.</summary>
    [Fact]
    public void EmphasisAndInlineCodeMarkersAreDropped()
    {
        var spoken = SpeechText.ForSpeech("The **build** is `green` and ~~not~~ broken.");

        Assert.Equal("The build is green and not broken.", spoken);
    }

    /// <summary>
    /// Verifies an underscore becomes a word break rather than vanishing, so an identifier is
    /// spoken as words instead of one unpronounceable run.
    /// </summary>
    [Fact]
    public void UnderscoreBecomesAWordBreak()
    {
        Assert.Equal("max chunk size is 500.", SpeechText.ForSpeech("max_chunk_size is 500."));
    }

    /// <summary>
    /// Verifies a sharp survives, because stripping it would turn "C#" into "C" — the reason
    /// headings are handled by a line-anchored pattern instead of a character strip.
    /// </summary>
    [Fact]
    public void SharpSurvivesOutsideAHeading()
    {
        Assert.Equal("Written in C# throughout.", SpeechText.ForSpeech("Written in C# throughout."));
    }

    /// <summary>Verifies runs of whitespace collapse to a single space.</summary>
    [Fact]
    public void WhitespaceCollapses()
    {
        Assert.Equal("one two three", SpeechText.ForSpeech("one\n\n  two\t\tthree  "));
    }

    /// <summary>Verifies a very long answer is cut short and the listener is told so.</summary>
    [Fact]
    public void LongAnswerIsTruncatedWithASpokenNotice()
    {
        var markdown = string.Join(" ", Enumerable.Repeat("sentence text here.", 400));

        var spoken = SpeechText.ForSpeech(markdown);

        Assert.EndsWith(SpeechText.TruncationNotice, spoken);
        Assert.True(spoken.Length <= SpeechText.MaxSpokenCharacters + SpeechText.TruncationNotice.Length);
    }

    /// <summary>Verifies a truncated answer ends on a sentence boundary when one is close enough.</summary>
    [Fact]
    public void TruncationPrefersASentenceBoundary()
    {
        var markdown = string.Join(" ", Enumerable.Repeat("sentence text here.", 400));

        var spoken = SpeechText.ForSpeech(markdown);
        var body = spoken[..^SpeechText.TruncationNotice.Length];

        Assert.EndsWith(".", body);
    }

    /// <summary>Verifies an answer at the cap is spoken whole, with no notice appended.</summary>
    [Fact]
    public void ShortAnswerIsNotTruncated()
    {
        var spoken = SpeechText.ForSpeech("A short answer.");

        Assert.Equal("A short answer.", spoken);
        Assert.DoesNotContain(SpeechText.TruncationNotice.Trim(), spoken);
    }
}
