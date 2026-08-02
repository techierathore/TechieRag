using System.Text;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// REQ-RAG-049 / BRD-135: what a message written to break the parser does to it.
/// </summary>
/// <remarks>
/// <para><b>Every byte here is attacker-chosen.</b> A message is the one input nobody vets before it
/// arrives — anyone with the address can send one, and spam folders are excluded from ingestion but
/// inboxes are not. So the parser is held to degrading rather than failing: it may produce less than
/// a well-formed message would, and it may not throw, recurse without bound, or turn a small message
/// into a large allocation.</para>
/// <para><b>Some of these are clean verdicts.</b> The depth cap, the encoded-word fallbacks and the
/// filename reduction were already right; the tests are here because "already right" is a claim that
/// needs a test to stay true, not because they found something.</para>
/// </remarks>
public sealed class MimeParserHardeningTests
{
    /// <summary>
    /// Nesting hundreds of multiparts deep costs the nested content, not the stack. A recursive
    /// descent with no depth cap is the standard way a MIME reader becomes a crash.
    /// </summary>
    [Fact]
    public void SurvivesDeeplyNestedMultiparts()
    {
        var message = new StringBuilder();
        for (var level = 0; level < 500; level++)
        {
            message.Append(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Content-Type: multipart/mixed; boundary=\"bound{level:D4}\"\r\n\r\n--bound{level:D4}\r\n");
        }

        message.Append("Content-Type: text/plain\r\n\r\nthe bottom\r\n");

        var parsed = MimeParser.Parse(Encoding.Latin1.GetBytes(message.ToString()));

        Assert.NotNull(parsed);
        Assert.Empty(parsed.Attachments);
    }

    /// <summary>
    /// A message built from hundreds of thousands of tiny parts does not decode to hundreds of
    /// thousands of retained attachments. Each part costs far more decoded than it does on the wire.
    /// </summary>
    [Fact]
    public void BoundsTheNumberOfAttachmentsOneMessageYields()
    {
        var message = new StringBuilder("Content-Type: multipart/mixed; boundary=X\r\n\r\n");
        for (var part = 0; part < 40_000; part++)
        {
            message.Append(
                "--X\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=\"a.bin\"\r\n\r\nA\r\n");
        }

        message.Append("--X--\r\n");

        var parsed = MimeParser.Parse(Encoding.Latin1.GetBytes(message.ToString()));

        Assert.Equal(MimeParser.MaxAttachments, parsed.Attachments.Count);
    }

    /// <summary>
    /// An attachment name is reduced to a bare file name. A sender controls this string completely,
    /// and it travels downstream as a document name.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\Windows\\win.ini", "win.ini")]
    [InlineData("/etc/shadow", "shadow")]
    [InlineData("C:\\Users\\ada\\secrets.pdf", "secrets.pdf")]
    [InlineData("....", "attachment")]
    [InlineData("report.pdf:evil.exe", "report.pdf_evil.exe")]
    public void ReducesAnAttachmentNameToABareFileName(string declared, string expected)
    {
        var message =
            "Content-Type: application/octet-stream\r\n" +
            $"Content-Disposition: attachment; filename=\"{declared}\"\r\n\r\nQQ==\r\n";

        var parsed = MimeParser.Parse(Encoding.Latin1.GetBytes(message));

        Assert.Equal(expected, Assert.Single(parsed.Attachments).FileName);
    }

    /// <summary>
    /// Malformed input degrades to less text rather than to an exception. The connector's per-item
    /// failure handling depends on this: a parser that throws turns one hostile message into a
    /// reported failure, and a parser that throws unpredictably makes that reason meaningless.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("\r\n\r\n")]
    [InlineData("Content-Type: multipart/mixed; boundary=\r\n\r\n--\r\n")]
    [InlineData("Subject: =?utf-8?B?!!!not-base64!!!?=\r\n\r\nbody")]
    [InlineData("Subject: =?no-such-charset?Q?a_b?=\r\n\r\nbody")]
    [InlineData("Content-Type: text/plain; charset=\"\"\r\n\r\nbody")]
    [InlineData("Content-Transfer-Encoding: base64\r\n\r\n$$$not base64$$$")]
    [InlineData("Content-Transfer-Encoding: quoted-printable\r\n\r\n=")]
    [InlineData("Content-Transfer-Encoding: quoted-printable\r\n\r\n=4")]
    [InlineData("Content-Type: multipart/mixed; boundary=X\r\n\r\n--X\r\nContent-Type: multipart/mixed; boundary=X\r\n\r\n--X\r\n")]
    [InlineData("Content-Type: multipart/mixed; boundary=\"\"\r\n\r\nbody")]
    public void DegradesRatherThanThrowingOnMalformedInput(string raw) =>
        Assert.Null(Record.Exception(() => MimeParser.Parse(Encoding.Latin1.GetBytes(raw))));

    /// <summary>An ordinary message still parses, so the hardening did not simply reject everything.</summary>
    [Fact]
    public void StillParsesAnOrdinaryMessage()
    {
        const string message =
            "Subject: =?utf-8?B?UmVuZXdhbA==?=\r\n" +
            "From: Ada <ada@example.test>\r\n" +
            "Content-Type: multipart/mixed; boundary=X\r\n\r\n" +
            "--X\r\nContent-Type: text/plain\r\n\r\nApproved.\r\n" +
            "--X\r\nContent-Type: application/pdf\r\nContent-Disposition: attachment; filename=\"contract.pdf\"\r\n\r\nQQ==\r\n" +
            "--X--\r\n";

        var parsed = MimeParser.Parse(Encoding.Latin1.GetBytes(message));

        Assert.Equal("Renewal", parsed.Subject);
        Assert.Contains("Approved.", parsed.Body, StringComparison.Ordinal);
        Assert.Equal("contract.pdf", Assert.Single(parsed.Attachments).FileName);
    }
}
