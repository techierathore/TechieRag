using System.Text;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-049 / BRD-135: decoding real mail — folded headers, encoded words, nested multiparts,
/// base64 and quoted-printable, and attachments — without a mail server.
/// </summary>
public sealed class MimeParserTests
{
    /// <summary>A plain message yields its headers and body.</summary>
    [Fact]
    public void ParsesAPlainMessage()
    {
        var message = Parse(
            "From: Ada <ada@example.test>",
            "To: Bob <bob@example.test>",
            "Subject: Renewal approved",
            "Date: Fri, 02 Jan 2026 10:00:00 +0000",
            "Message-ID: <abc@example.test>",
            "",
            "The renewal is approved.");

        Assert.Equal("Renewal approved", message.Subject);
        Assert.Equal("Ada <ada@example.test>", message.From);
        Assert.Equal("<abc@example.test>", message.MessageId);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero), message.Date);
        Assert.Equal("The renewal is approved.", message.Body);
    }

    /// <summary>
    /// A folded header is rejoined. Treating the continuation as a separate header is how a long
    /// subject comes out truncated.
    /// </summary>
    [Fact]
    public void UnfoldsAContinuedHeader()
    {
        var message = Parse(
            "Subject: A subject long enough that the client",
            "\twrapped it onto a second line",
            "",
            "body");

        Assert.Equal("A subject long enough that the client wrapped it onto a second line", message.Subject);
    }

    /// <summary>
    /// An encoded word is decoded. Left alone, a message from anyone whose name carries an accent is
    /// indexed under an unreadable and unsearchable subject.
    /// </summary>
    [Theory]
    [InlineData("=?utf-8?B?w4RwZmVs?=", "Äpfel")]
    [InlineData("=?utf-8?Q?caf=C3=A9?=", "café")]
    [InlineData("=?utf-8?Q?two_words?=", "two words")]
    public void DecodesEncodedWords(string encoded, string expected) =>
        Assert.Equal(expected, MimeParser.DecodeEncodedWords(encoded));

    /// <summary>A malformed encoded word is left as it arrived rather than losing the whole header.</summary>
    [Fact]
    public void LeavesAMalformedEncodedWordAlone() =>
        Assert.Equal("=?utf-8?B?not base64!?=", MimeParser.DecodeEncodedWords("=?utf-8?B?not base64!?="));

    /// <summary>
    /// A quoted-printable body is decoded, and a trailing "=" rejoins the line it split. Emitting
    /// that newline would put a break in the middle of a word the encoder never broke.
    /// </summary>
    [Fact]
    public void DecodesQuotedPrintableBody()
    {
        var message = Parse(
            "Content-Type: text/plain; charset=utf-8",
            "Content-Transfer-Encoding: quoted-printable",
            "",
            "caf=C3=A9 and a soft=",
            "break");

        Assert.Equal("café and a softbreak", message.Body);
    }

    /// <summary>A base64 body is decoded.</summary>
    [Fact]
    public void DecodesBase64Body()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("decoded body"));
        var message = Parse(
            "Content-Type: text/plain; charset=utf-8",
            "Content-Transfer-Encoding: base64",
            "",
            payload);

        Assert.Equal("decoded body", message.Body);
    }

    /// <summary>
    /// With both alternatives present the plain part wins: it is what the sender's client generated
    /// from the same source and needs no stripping.
    /// </summary>
    [Fact]
    public void PrefersPlainTextOverHtml()
    {
        var message = Parse(
            "Content-Type: multipart/alternative; boundary=\"b1\"",
            "",
            "preamble that is not content",
            "--b1",
            "Content-Type: text/plain; charset=utf-8",
            "",
            "the plain version",
            "--b1",
            "Content-Type: text/html; charset=utf-8",
            "",
            "<p>the html version</p>",
            "--b1--");

        Assert.Equal("the plain version", message.Body);
    }

    /// <summary>HTML is used when it is all there is, and arrives as prose rather than markup.</summary>
    [Fact]
    public void FallsBackToHtmlWhenThereIsNoPlainPart()
    {
        var message = Parse(
            "Content-Type: text/html; charset=utf-8",
            "",
            "<p>Approved on <strong>Tuesday</strong>.</p>");

        Assert.Contains("Approved on", message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>", message.Body, StringComparison.Ordinal);
    }

    /// <summary>A nested multipart is walked, not treated as one opaque part.</summary>
    [Fact]
    public void WalksNestedMultiparts()
    {
        var message = Parse(
            "Content-Type: multipart/mixed; boundary=\"outer\"",
            "",
            "--outer",
            "Content-Type: multipart/alternative; boundary=\"inner\"",
            "",
            "--inner",
            "Content-Type: text/plain",
            "",
            "buried text",
            "--inner--",
            "--outer--");

        Assert.Equal("buried text", message.Body);
    }

    /// <summary>An attachment is decoded to bytes and kept apart from the body.</summary>
    [Fact]
    public void SeparatesAttachmentsFromTheBody()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("PDF-ish bytes"));
        var message = Parse(
            "Content-Type: multipart/mixed; boundary=\"b1\"",
            "",
            "--b1",
            "Content-Type: text/plain",
            "",
            "See attached.",
            "--b1",
            "Content-Type: application/pdf; name=\"contract.pdf\"",
            "Content-Disposition: attachment; filename=\"contract.pdf\"",
            "Content-Transfer-Encoding: base64",
            "",
            payload,
            "--b1--");

        Assert.Equal("See attached.", message.Body);
        var attachment = Assert.Single(message.Attachments);
        Assert.Equal("contract.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.Equal("PDF-ish bytes", Encoding.UTF8.GetString(attachment.Content));
    }

    /// <summary>
    /// An attachment name is reduced to a bare file name. A sender controls this string, and a name
    /// with a path in it is a real thing that arrives in real mail.
    /// </summary>
    [Fact]
    public void StripsPathsFromAttachmentNames()
    {
        var message = Parse(
            "Content-Type: multipart/mixed; boundary=\"b1\"",
            "",
            "--b1",
            "Content-Type: application/octet-stream",
            "Content-Disposition: attachment; filename=\"../../.ssh/authorized_keys\"",
            "",
            "x",
            "--b1--");

        Assert.Equal("authorized_keys", Assert.Single(message.Attachments).FileName);
    }

    /// <summary>A declared charset other than UTF-8 is honoured.</summary>
    [Fact]
    public void HonoursADeclaredCharset()
    {
        var latin = Encoding.Latin1.GetString(Encoding.Latin1.GetBytes("café"));
        var message = Parse(
            "Content-Type: text/plain; charset=iso-8859-1",
            "",
            latin);

        Assert.Equal("café", message.Body);
    }

    /// <summary>An unknown charset falls back rather than failing the message.</summary>
    [Fact]
    public void FallsBackWhenTheCharsetIsUnknown()
    {
        var message = Parse(
            "Content-Type: text/plain; charset=x-nonsense-9000",
            "",
            "still readable");

        Assert.Equal("still readable", message.Body);
    }

    /// <summary>A message with neither Content-Type nor body still parses to something usable.</summary>
    [Fact]
    public void ParsesAMessageWithNoContentType()
    {
        var message = Parse("Subject: bare", "", "just text");

        Assert.Equal("just text", message.Body);
    }

    /// <summary>A structured header's parameters are readable individually.</summary>
    [Theory]
    [InlineData("text/plain; charset=\"utf-8\"", "charset", "utf-8")]
    [InlineData("attachment; filename*=UTF-8''report.pdf", "filename", "UTF-8''report.pdf")]
    [InlineData("text/plain", "charset", null)]
    public void ReadsHeaderParameters(string header, string parameter, string? expected) =>
        Assert.Equal(expected, MimeParser.ReadParameter(header, parameter));

    /// <summary>Malformed input yields an empty message rather than an exception.</summary>
    [Fact]
    public void SurvivesGarbage()
    {
        var message = MimeParser.Parse(Encoding.UTF8.GetBytes("  not a message at all"));

        Assert.NotNull(message);
        Assert.Empty(message.Attachments);
    }

    private static ParsedMailMessage Parse(params string[] lines) =>
        MimeParser.Parse(Encoding.Latin1.GetBytes(string.Join("\r\n", lines)));
}
