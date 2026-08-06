using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-049 / BRD-135: a thread that keeps its quoted history contains its first message once per
/// reply, so retrieval returns the same answer many times over and cites an arbitrary copy.
/// </summary>
public sealed class ReplyTrimmerTests
{
    /// <summary>Quoted lines are removed and the reply above them is kept.</summary>
    [Fact]
    public void RemovesQuotedLines()
    {
        var trimmed = ReplyTrimmer.Trim("Agreed, ship it.\n\n> Should we ship?\n> Please advise.");

        Assert.Equal("Agreed, ship it.", trimmed);
    }

    /// <summary>The attribution line a client writes above a quote ends the message.</summary>
    [Fact]
    public void StopsAtAnAttributionLine()
    {
        var trimmed = ReplyTrimmer.Trim("Yes.\n\nOn Fri, 2 Jan 2026 at 10:00, Ada Lovelace wrote:\nthe whole prior message");

        Assert.Equal("Yes.", trimmed);
    }

    /// <summary>
    /// An ordinary sentence starting with "On" does not truncate the message — the pattern requires
    /// the line to be an attribution line and nothing else.
    /// </summary>
    [Fact]
    public void DoesNotTruncateOnAnOrdinarySentence()
    {
        var body = "On Tuesday we agreed to renew.\nThe paperwork follows.";

        Assert.Equal(body, ReplyTrimmer.Trim(body));
    }

    /// <summary>The RFC 3676 signature delimiter ends the message.</summary>
    [Fact]
    public void RemovesASignatureBlock()
    {
        var trimmed = ReplyTrimmer.Trim("The answer is 42.\n-- \nAda Lovelace\nChief Analyst\nacme.example.test");

        Assert.Equal("The answer is 42.", trimmed);
    }

    /// <summary>A forwarded-message separator ends the message.</summary>
    [Theory]
    [InlineData("-----Original Message-----")]
    [InlineData("---------- Forwarded message ---------")]
    [InlineData("Begin forwarded message:")]
    public void StopsAtAForwardSeparator(string separator)
    {
        var trimmed = ReplyTrimmer.Trim($"See below.\n\n{separator}\nFrom: someone\nSubject: something");

        Assert.Equal("See below.", trimmed);
    }

    /// <summary>
    /// A message that is nothing but a forward keeps its content. Trimming it to empty would drop a
    /// document the user asked for — false negatives cost duplication, false positives cost the mail.
    /// </summary>
    [Fact]
    public void KeepsAMessageThatIsEntirelyQuoted()
    {
        var body = "> the whole thing\n> was a quote";

        Assert.Equal(body, ReplyTrimmer.Trim(body));
    }

    /// <summary>Trimming can be switched off for each concern independently.</summary>
    [Fact]
    public void HonoursTheSwitches()
    {
        var body = "Reply.\n-- \nSignature";

        Assert.Contains("Signature", ReplyTrimmer.Trim(body, stripQuotedReplies: true, stripSignatures: false), StringComparison.Ordinal);
        Assert.DoesNotContain("Signature", ReplyTrimmer.Trim(body), StringComparison.Ordinal);
    }

    /// <summary>
    /// Removing quoted lines leaves behind the blank lines that separated them, so a run of blanks
    /// collapses to one — otherwise a trimmed message is mostly whitespace.
    /// </summary>
    [Fact]
    public void CollapsesTheBlankLinesLeftBehind()
    {
        var trimmed = ReplyTrimmer.Trim("Point one.\n\n> quoted\n\n> more quoted\n\nPoint two.");

        Assert.Equal("Point one.\n\nPoint two.", trimmed);
    }

    /// <summary>An empty body is returned unchanged rather than throwing.</summary>
    [Fact]
    public void SurvivesAnEmptyBody() => Assert.Equal(string.Empty, ReplyTrimmer.Trim(string.Empty));
}
