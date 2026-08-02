using System.Globalization;
using System.Text;
using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using TechieRag.Tests.Connectors;
using Xunit;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// REQ-RAG-049 / BRD-135 and BRD-65: what the connector says when a message defeats it.
/// </summary>
public sealed class EmailIngestionHardeningTests
{
    /// <summary>
    /// An IMAP <c>SINCE</c> key carries English month abbreviations regardless of the machine's
    /// culture. The protocol's date form is fixed, so a server running under a French or German
    /// locale would be sent <c>SINCE 02-janv.-2026</c> and reject the search — an incremental sync
    /// that silently returns nothing on some machines and not others.
    /// </summary>
    [Fact]
    public void FormatsTheSinceKeyInTheProtocolsOwnCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            var keys = ImapMailTransport.BuildSearchKeys(
                new MailSearchCriteria(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)));

            Assert.Equal("SINCE 02-Jan-2026", keys);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// A message whose attachment list the parser truncated says so on the item. BRD-65 asks for a
    /// reason an operator can act on for every skip, and a document that is quietly shorter than the
    /// message it came from is a skip nobody was told about.
    /// </summary>
    [Fact]
    public async Task ReportsWhenTheAttachmentListWasTruncated()
    {
        var raw = new StringBuilder("Subject: Many\r\nContent-Type: multipart/mixed; boundary=X\r\n\r\n");
        for (var part = 0; part <= MimeParser.MaxAttachments; part++)
        {
            raw.Append(
                "--X\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=\"a.bin\"\r\n\r\nA\r\n");
        }

        raw.Append("--X--\r\n");

        var transport = new FakeMailTransport().Message("INBOX", "1", "Many", "ada@example.test", raw.ToString());
        var connector = new EmailConnector(transport, new EmailConnectorOptions());

        var page = await connector.ListAsync(new ConnectorListRequest());
        var document = await connector.FetchAsync(page.Items[0]);

        Assert.Contains("truncated", document.Item.Metadata!["AttachmentsSkipped"], StringComparison.Ordinal);
    }

    /// <summary>An ordinary message carries no truncation note, so the note means something when it appears.</summary>
    [Fact]
    public async Task ReportsNoTruncationForAnOrdinaryMessage()
    {
        const string raw =
            "Subject: One\r\nContent-Type: multipart/mixed; boundary=X\r\n\r\n" +
            "--X\r\nContent-Type: text/plain\r\n\r\nApproved.\r\n" +
            "--X\r\nContent-Type: application/pdf\r\nContent-Disposition: attachment; filename=\"c.pdf\"\r\n\r\nQQ==\r\n--X--\r\n";

        var transport = new FakeMailTransport().Message("INBOX", "1", "One", "ada@example.test", raw);
        var connector = new EmailConnector(transport, new EmailConnectorOptions());

        var page = await connector.ListAsync(new ConnectorListRequest());
        var document = await connector.FetchAsync(page.Items[0]);

        Assert.False(document.Item.Metadata!.ContainsKey("AttachmentsSkipped"));
    }
}
