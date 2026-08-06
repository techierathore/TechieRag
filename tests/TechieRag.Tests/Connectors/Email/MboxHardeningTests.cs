using System.Text;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// REQ-RAG-049 / BRD-135: an mbox is a file format with no length prefix, so its escaping is load-bearing.
/// </summary>
/// <remarks>
/// A message ends where the next line beginning <c>From </c> begins, which means a body containing
/// such a line has to be escaped by the writer and unescaped by the reader — and getting the
/// unescaping wrong does not fail loudly, it quietly indexes a body the sender did not write.
/// </remarks>
public sealed class MboxHardeningTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"techierag-mbox-{Guid.NewGuid():N}.mbox");

    /// <summary>
    /// A quoted reply that itself contains a "From " line comes back with one level of quoting, not
    /// two. mboxrd escapes any run of <c>&gt;</c> before <c>From </c>, so unescaping only the
    /// single-<c>&gt;</c> case leaves every deeper quote a level too deep.
    /// </summary>
    [Fact]
    public async Task UnescapesEveryDepthOfAnEscapedFromLine()
    {
        Write(
            "From envelope@example.test Mon Jan  1 00:00:00 2026\r\n" +
            "Subject: Thread\r\nMessage-ID: <a@example.test>\r\n\r\n" +
            "My reply.\r\n" +
            ">From the desk of Ada\r\n" +
            ">>From the desk of Bob\r\n");

        var body = await BodyAsync();

        Assert.Contains("From the desk of Ada", body, StringComparison.Ordinal);
        Assert.Contains(">From the desk of Bob", body, StringComparison.Ordinal);
        Assert.DoesNotContain(">>From", body, StringComparison.Ordinal);
    }

    /// <summary>An unescaped "From " line still splits the archive, which is the format's own ambiguity rather than a defect here.</summary>
    [Fact]
    public async Task SplitsOnAnUnescapedFromLine()
    {
        Write(
            "From envelope@example.test Mon Jan  1 00:00:00 2026\r\n" +
            "Subject: One\r\nMessage-ID: <a@example.test>\r\n\r\nfirst\r\n" +
            "From envelope@example.test Tue Jan  2 00:00:00 2026\r\n" +
            "Subject: Two\r\nMessage-ID: <b@example.test>\r\n\r\nsecond\r\n");

        var transport = new MboxMailTransport(path);
        var page = await transport.SearchAsync("x", new MailSearchCriteria(), 0, 10);

        Assert.Equal(2, page.Headers.Count);
        Assert.Equal(["One", "Two"], page.Headers.Select(h => h.Subject));
    }

    /// <summary>A missing file is an operator-facing run failure that names the path.</summary>
    [Fact]
    public async Task ReportsAMissingFile()
    {
        var transport = new MboxMailTransport(Path.Combine(Path.GetTempPath(), "techierag-absent.mbox"));

        var error = await Assert.ThrowsAsync<TechieRag.Connectors.ConnectorException>(
            () => transport.SearchAsync("x", new MailSearchCriteria(), 0, 10));

        Assert.Contains("does not exist", error.Message, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void Write(string content) => File.WriteAllBytes(path, Encoding.Latin1.GetBytes(content));

    private async Task<string> BodyAsync()
    {
        var transport = new MboxMailTransport(path);
        var page = await transport.SearchAsync("x", new MailSearchCriteria(), 0, 10);
        return MimeParser.Parse(await transport.FetchAsync(page.Headers[0])).Body;
    }
}
