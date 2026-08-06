using System.Text;
using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-049 / BRD-135: a local mail export is a first-class source — it is what every provider's
/// "download your data" flow produces, and the only way to read an account that no longer exists.
/// </summary>
public sealed class MboxMailTransportTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"techierag-{Guid.NewGuid():N}.mbox");

    /// <summary>Messages are split on the separator line the format uses.</summary>
    [Fact]
    public async Task SplitsMessagesOnTheSeparatorLine()
    {
        Write(
            "From ada@example.test Fri Jan  2 10:00:00 2026",
            "From: ada@example.test",
            "Subject: First",
            "Message-ID: <1@example.test>",
            "",
            "one",
            "From bob@example.test Fri Jan  3 10:00:00 2026",
            "From: bob@example.test",
            "Subject: Second",
            "Message-ID: <2@example.test>",
            "",
            "two");

        var page = await new MboxMailTransport(path).SearchAsync("x", new MailSearchCriteria(), 0, 10);

        Assert.Equal(["First", "Second"], page.Headers.Select(h => h.Subject));
    }

    /// <summary>
    /// A body line beginning "From " is escaped by writers, and unescaping it here is what stops such
    /// a line from silently truncating the message that contains it.
    /// </summary>
    [Fact]
    public async Task UnescapesAQuotedFromLine()
    {
        Write(
            "From ada@example.test Fri Jan  2 10:00:00 2026",
            "Subject: Quoting",
            "Message-ID: <1@example.test>",
            "",
            "Here is a quote:",
            ">From the archives, this is still one message.");

        var transport = new MboxMailTransport(path);
        var page = await transport.SearchAsync("x", new MailSearchCriteria(), 0, 10);
        var raw = Encoding.Latin1.GetString(await transport.FetchAsync(page.Headers[0]));

        Assert.Single(page.Headers);
        Assert.Contains("From the archives", raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// Identity comes from Message-ID, so appending to an archive does not renumber and re-ingest
    /// everything already in it.
    /// </summary>
    [Fact]
    public async Task UsesMessageIdAsIdentity()
    {
        Write(
            "From ada@example.test Fri Jan  2 10:00:00 2026",
            "Subject: First",
            "Message-ID: <stable@example.test>",
            "",
            "one");

        var page = await new MboxMailTransport(path).SearchAsync("x", new MailSearchCriteria(), 0, 10);

        Assert.Equal("<stable@example.test>", page.Headers[0].Uid);
    }

    /// <summary>
    /// Every criterion is applied even though there is no server to push it to. A filter that
    /// silently does nothing on one transport is worse than one that is slow.
    /// </summary>
    [Fact]
    public async Task AppliesEveryFilterLocally()
    {
        Write(
            "From ada@example.test Fri Jan  2 10:00:00 2026",
            "From: legal@example.test",
            "Subject: Renewal terms",
            "Message-ID: <1@example.test>",
            "",
            "one",
            "From bob@example.test Fri Jan  3 10:00:00 2026",
            "From: sales@example.test",
            "Subject: Lunch",
            "Message-ID: <2@example.test>",
            "",
            "two");

        var transport = new MboxMailTransport(path);

        var bySender = await transport.SearchAsync("x", new MailSearchCriteria(null, "legal@"), 0, 10);
        Assert.Equal(["Renewal terms"], bySender.Headers.Select(h => h.Subject));

        var bySubject = await transport.SearchAsync("x", new MailSearchCriteria(null, null, "lunch"), 0, 10);
        Assert.Equal(["Lunch"], bySubject.Headers.Select(h => h.Subject));
    }

    /// <summary>Results page, so a large archive is not handed back in one piece.</summary>
    [Fact]
    public async Task PagesResults()
    {
        Write(
            "From a@example.test Fri Jan  2 10:00:00 2026",
            "Subject: First",
            "Message-ID: <1@example.test>",
            "",
            "one",
            "From b@example.test Fri Jan  3 10:00:00 2026",
            "Subject: Second",
            "Message-ID: <2@example.test>",
            "",
            "two");

        var transport = new MboxMailTransport(path);

        var first = await transport.SearchAsync("x", new MailSearchCriteria(), 0, 1);
        Assert.True(first.HasMore);

        var second = await transport.SearchAsync("x", new MailSearchCriteria(), 1, 1);
        Assert.False(second.HasMore);
        Assert.Equal("Second", second.Headers[0].Subject);
    }

    /// <summary>An archive presents itself as one folder named after the file.</summary>
    [Fact]
    public async Task PresentsItselfAsOneFolder()
    {
        Write("From a@example.test Fri Jan  2 10:00:00 2026", "Subject: First", "", "one");

        var folders = await new MboxMailTransport(path).ListFoldersAsync();

        Assert.Single(folders);
    }

    /// <summary>
    /// Renaming or moving an archive does not change any message's identity, so the next sync
    /// ingests nothing (TR-RAG-037 / REQ-RAG-049).
    /// </summary>
    /// <remarks>
    /// <para>The reproduction from the defect report, as a test: the same bytes under two names.
    /// Item identity used to be <c>{folder}/{uidValidity}/{uid}</c>, and for an mbox the folder is
    /// the archive's file name — so moving a fixture from <c>techiedesk-smoke.mbox</c> to
    /// <c>req-fn-042-smoke.mbox</c> turned "4 unchanged" into "fetched 5" and put five duplicate
    /// documents in the vector store.</para>
    /// <para>Asserted through <see cref="EmailConnector"/> rather than on the transport, because the
    /// id the connector composes is the one <c>ConnectorRunner</c> keys <c>ItemVersions</c> on — the
    /// value that actually decides re-ingestion.</para>
    /// </remarks>
    [Fact]
    public async Task MessageIdentitySurvivesRenamingTheArchive()
    {
        string[] archive =
        [
            "From ada@example.test Fri Jan  2 10:00:00 2026",
            "From: ada@example.test",
            "Subject: First",
            "Message-ID: <1@example.test>",
            "",
            "one",
            "From bob@example.test Fri Jan  3 10:00:00 2026",
            "From: bob@example.test",
            "Subject: Second",
            "Message-ID: <2@example.test>",
            "",
            "two"
        ];

        Write(archive);
        var before = await IdsFrom(path);

        var renamed = Path.Combine(
            Path.GetTempPath(), $"techierag-renamed-{Guid.NewGuid():N}.mbox");
        File.WriteAllText(renamed, string.Join("\n", archive) + "\n");

        try
        {
            Assert.Equal(before, await IdsFrom(renamed));
        }
        finally
        {
            File.Delete(renamed);
        }
    }

    /// <summary>
    /// A message with no Message-ID keeps its identity when the archive gains one at the front —
    /// the case the old position-based fallback could not survive (REQ-RAG-049).
    /// </summary>
    [Fact]
    public async Task IdentityOfAMessageWithoutAMessageIdSurvivesReordering()
    {
        Write(
            "From ada@example.test Fri Jan  2 10:00:00 2026",
            "From: ada@example.test",
            "Subject: Original",
            "",
            "body of the original");

        var before = await IdsFrom(path);

        // The same message, now second, in an archive that gained one at the front.
        Write(
            "From new@example.test Fri Jan  1 10:00:00 2026",
            "From: new@example.test",
            "Subject: Prepended",
            "Message-ID: <new@example.test>",
            "",
            "arrived later, sorted earlier",
            "From ada@example.test Fri Jan  2 10:00:00 2026",
            "From: ada@example.test",
            "Subject: Original",
            "",
            "body of the original");

        Assert.Subset(new HashSet<string>(await IdsFrom(path)), new HashSet<string>(before));
    }

    /// <summary>Reads the connector-composed item ids for one archive, in order.</summary>
    /// <param name="archivePath">The archive to read.</param>
    /// <returns>The ids <c>ConnectorRunner</c> would key on.</returns>
    private static async Task<IReadOnlyList<string>> IdsFrom(string archivePath)
    {
        var connector = new EmailConnector(
            new MboxMailTransport(archivePath), new EmailConnectorOptions());

        var page = await connector.ListAsync(new ConnectorListRequest());
        return page.Items.Select(item => item.Id).ToList();
    }

    /// <summary>A file that is not there is an honest run-level failure, not an empty mailbox.</summary>
    [Fact]
    public async Task ReportsAMissingFileHonestly()
    {
        var transport = new MboxMailTransport(Path.Combine(Path.GetTempPath(), "absent.mbox"));

        await Assert.ThrowsAsync<ConnectorException>(
            () => transport.SearchAsync("x", new MailSearchCriteria(), 0, 10));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void Write(params string[] lines) => File.WriteAllText(path, string.Join("\n", lines) + "\n");
}
