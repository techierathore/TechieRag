using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-049 / BRD-135: folder scope, the privacy defaults, incremental sync and attachment
/// handling — the parts of the email connector that are not the IMAP protocol.
/// </summary>
public sealed class EmailConnectorTests
{
    /// <summary>
    /// The envelope is part of the document. Someone searching a mailbox asks "what did legal say
    /// about the renewal", and a chunk holding only the body cannot match the sender or the subject.
    /// </summary>
    [Fact]
    public async Task IngestsTheEnvelopeAlongsideTheBody()
    {
        var transport = new FakeMailTransport().Message(
            "INBOX", "1", "Renewal", "legal@example.test", Raw("Renewal", "legal@example.test", "Approved."));

        var connector = new EmailConnector(transport, Options());
        var page = await connector.ListAsync(new ConnectorListRequest());
        var document = await connector.FetchAsync(page.Items[0]);

        Assert.Contains("Subject: Renewal", document.Text, StringComparison.Ordinal);
        Assert.Contains("From: legal@example.test", document.Text, StringComparison.Ordinal);
        Assert.Contains("Folder: INBOX", document.Text, StringComparison.Ordinal);
        Assert.Contains("Approved.", document.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Junk is skipped even when the folder was named explicitly. Spam is adversarial text written to
    /// persuade, and an index that answers from it is a phishing amplifier.
    /// </summary>
    [Fact]
    public async Task SkipsJunkFoldersEvenWhenNamed()
    {
        var transport = new FakeMailTransport().Message("Spam", "1", "You have won", "scam@example.test", Raw("won", "scam@example.test", "click"));

        var connector = new EmailConnector(transport, Options(o => o.Folders = ["Spam"]));
        var page = await connector.ListAsync(new ConnectorListRequest());

        Assert.Empty(page.Items);
        Assert.Empty(transport.Searches);
    }

    /// <summary>Junk is ingested when the operator asks for it, and only then.</summary>
    [Fact]
    public async Task IngestsJunkOnlyWhenAskedTo()
    {
        var transport = new FakeMailTransport().Message("Spam", "1", "You have won", "scam@example.test", Raw("won", "scam@example.test", "click"));

        var connector = new EmailConnector(transport, Options(o =>
        {
            o.Folders = ["Spam"];
            o.IncludeSpam = true;
        }));

        var page = await connector.ListAsync(new ConnectorListRequest());

        Assert.Single(page.Items);
    }

    /// <summary>Mail the account sent is excluded by default, so a thread is not indexed twice.</summary>
    [Fact]
    public async Task SkipsMailTheAccountSent()
    {
        var transport = new FakeMailTransport()
            .Message("INBOX", "1", "Renewal", "legal@example.test", Raw("Renewal", "legal@example.test", "in"))
            .Message("INBOX", "2", "Re: Renewal", "ada@example.test", Raw("Re: Renewal", "ada@example.test", "out"));

        var connector = new EmailConnector(transport, Options(o => o.AccountAddress = "ada@example.test"));
        var page = await connector.ListAsync(new ConnectorListRequest());

        Assert.Equal(["Renewal"], page.Items.Select(i => i.Name));
    }

    /// <summary>Sent mail is included when the operator asks for it.</summary>
    [Fact]
    public async Task IncludesSentMailWhenAskedTo()
    {
        var transport = new FakeMailTransport()
            .Message("INBOX", "2", "Re: Renewal", "ada@example.test", Raw("Re: Renewal", "ada@example.test", "out"));

        var connector = new EmailConnector(transport, Options(o =>
        {
            o.AccountAddress = "ada@example.test";
            o.IncludeSentByMe = true;
        }));

        var page = await connector.ListAsync(new ConnectorListRequest());

        Assert.Single(page.Items);
    }

    /// <summary>A folder is paged through before the walk moves to the next one.</summary>
    [Fact]
    public async Task PagesAFolderThenMovesToTheNext()
    {
        var transport = new FakeMailTransport()
            .Message("INBOX", "1", "One", "a@example.test", Raw("One", "a@example.test", "1"))
            .Message("INBOX", "2", "Two", "a@example.test", Raw("Two", "a@example.test", "2"))
            .Message("Archive", "3", "Three", "a@example.test", Raw("Three", "a@example.test", "3"));

        var connector = new EmailConnector(transport, Options(o =>
        {
            o.Folders = ["INBOX", "Archive"];
            o.PageSize = 1;
        }));

        var names = new List<string>();
        string? cursor = null;
        do
        {
            var page = await connector.ListAsync(new ConnectorListRequest(cursor));
            names.AddRange(page.Items.Select(i => i.Name));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(["One", "Two", "Three"], names);
    }

    /// <summary>
    /// Advancing by what the server returned rather than by what survived filtering is what stops a
    /// filtered-out message from being requested again forever.
    /// </summary>
    [Fact]
    public async Task AdvancesPastMessagesItFilteredOut()
    {
        var transport = new FakeMailTransport()
            .Message("INBOX", "1", "Mine", "ada@example.test", Raw("Mine", "ada@example.test", "x"))
            .Message("INBOX", "2", "Theirs", "bob@example.test", Raw("Theirs", "bob@example.test", "y"));

        var connector = new EmailConnector(transport, Options(o =>
        {
            o.AccountAddress = "ada@example.test";
            o.PageSize = 1;
        }));

        var first = await connector.ListAsync(new ConnectorListRequest());

        Assert.Empty(first.Items);
        Assert.Equal("0:1", first.NextCursor);
    }

    /// <summary>
    /// An incremental run asks the server for recent mail only, with a day of overlap because IMAP's
    /// date search is day-granular and clocks disagree.
    /// </summary>
    [Fact]
    public async Task AsksOnlyForMailSinceThePreviousRun()
    {
        var transport = new FakeMailTransport().Message(
            "INBOX", "1", "Recent", "a@example.test", Raw("Recent", "a@example.test", "x"), DateTimeOffset.UtcNow);

        var previous = new ConnectorSyncState { LastRunUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero) };
        await new EmailConnector(transport, Options()).ListAsync(new ConnectorListRequest(null, previous));

        Assert.Equal(new DateTimeOffset(2026, 5, 31, 9, 0, 0, TimeSpan.Zero), transport.Searches[0].Criteria.SinceUtc);
    }

    /// <summary>
    /// The connector reports that its listing is not the whole mailbox, so the runner never prunes
    /// the sync state that made the run incremental.
    /// </summary>
    [Fact]
    public void DeclaresThatItListsChangesOnly() =>
        Assert.False(new EmailConnector(new FakeMailTransport(), Options()).ListsEntireSource);

    /// <summary>Quoted history is removed before the message is ingested.</summary>
    [Fact]
    public async Task TrimsQuotedHistory()
    {
        var raw = Raw("Re: Renewal", "legal@example.test", "Approved.\r\n\r\n> Please confirm.\r\n> Thanks.");
        var transport = new FakeMailTransport().Message("INBOX", "1", "Re: Renewal", "legal@example.test", raw);

        var connector = new EmailConnector(transport, Options());
        var page = await connector.ListAsync(new ConnectorListRequest());
        var document = await connector.FetchAsync(page.Items[0]);

        Assert.Contains("Approved.", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Please confirm", document.Text, StringComparison.Ordinal);
    }

    /// <summary>Attachment text is appended when asked for, and read from memory rather than disk.</summary>
    [Fact]
    public async Task IncludesAttachmentTextWhenAsked()
    {
        var transport = new FakeMailTransport().Message(
            "INBOX", "1", "Contract", "legal@example.test", WithAttachment("contract.pdf", "application/pdf"));

        var connector = new EmailConnector(
            transport,
            Options(o => o.IncludeAttachments = true),
            [new StubAttachmentProcessor(".pdf", "the contract text")]);

        var page = await connector.ListAsync(new ConnectorListRequest());
        var document = await connector.FetchAsync(page.Items[0]);

        Assert.Contains("--- Attachment: contract.pdf ---", document.Text, StringComparison.Ordinal);
        Assert.Contains("the contract text", document.Text, StringComparison.Ordinal);
    }

    /// <summary>Attachments are left alone unless asked for: that is where the confidential files are.</summary>
    [Fact]
    public async Task LeavesAttachmentsAloneByDefault()
    {
        var transport = new FakeMailTransport().Message(
            "INBOX", "1", "Contract", "legal@example.test", WithAttachment("contract.pdf", "application/pdf"));

        var connector = new EmailConnector(
            transport, Options(), [new StubAttachmentProcessor(".pdf", "the contract text")]);

        var page = await connector.ListAsync(new ConnectorListRequest());
        var document = await connector.FetchAsync(page.Items[0]);

        Assert.DoesNotContain("the contract text", document.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// An attachment that could not be read is recorded on the item rather than written into the
    /// indexed text, where a note about a skipped file would itself be retrievable as content.
    /// </summary>
    [Fact]
    public async Task RecordsAttachmentsItCouldNotRead()
    {
        var transport = new FakeMailTransport().Message(
            "INBOX", "1", "Archive", "legal@example.test", WithAttachment("photos.zip", "application/zip"));

        var connector = new EmailConnector(transport, Options(o => o.IncludeAttachments = true));
        var page = await connector.ListAsync(new ConnectorListRequest());
        var document = await connector.FetchAsync(page.Items[0]);

        Assert.Contains("photos.zip", document.Item.Metadata!["AttachmentsSkipped"], StringComparison.Ordinal);
        Assert.DoesNotContain("photos.zip", document.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The folder's UID generation is part of a message's identity. When a server resets it, every
    /// UID means something new, and an id built from the UID alone would call the folder unchanged.
    /// </summary>
    [Fact]
    public async Task IdentityIncludesTheUidGeneration()
    {
        var transport = new FakeMailTransport().Message(
            "INBOX", "7", "Renewal", "a@example.test", Raw("Renewal", "a@example.test", "x"));

        var page = await new EmailConnector(transport, Options()).ListAsync(new ConnectorListRequest());

        Assert.Equal("INBOX/1/7", page.Items[0].Id);
        Assert.Equal("7", page.Items[0].Version);
    }

    /// <summary>Fetching a message this instance never listed is refused rather than guessed at.</summary>
    [Fact]
    public async Task RefusesToFetchAnItemItDidNotList()
    {
        var connector = new EmailConnector(new FakeMailTransport(), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.FetchAsync(new ConnectorItem("INBOX/1/99", "ghost", "")));
    }

    private static EmailConnectorOptions Options(Action<EmailConnectorOptions>? configure = null)
    {
        var options = new EmailConnectorOptions();
        configure?.Invoke(options);
        return options;
    }

    private static string Raw(string subject, string from, string body) =>
        $"From: {from}\r\nTo: bob@example.test\r\nSubject: {subject}\r\n"
        + "Date: Fri, 02 Jan 2026 10:00:00 +0000\r\n\r\n" + body;

    private static string WithAttachment(string fileName, string mediaType)
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("binary-ish"));

        return "From: legal@example.test\r\nSubject: Contract\r\n"
               + "Content-Type: multipart/mixed; boundary=\"b1\"\r\n\r\n"
               + "--b1\r\nContent-Type: text/plain\r\n\r\nSee attached.\r\n"
               + $"--b1\r\nContent-Type: {mediaType}; name=\"{fileName}\"\r\n"
               + $"Content-Disposition: attachment; filename=\"{fileName}\"\r\n"
               + $"Content-Transfer-Encoding: base64\r\n\r\n{payload}\r\n--b1--\r\n";
    }
}
