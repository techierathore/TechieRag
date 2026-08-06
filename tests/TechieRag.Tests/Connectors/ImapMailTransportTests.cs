using System.Text;
using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-049 / BRD-135: the IMAP conversation itself — literal framing, tagged responses, UID
/// parsing, folder selection and the TLS refusal — driven by a scripted server with no network.
/// </summary>
public sealed class ImapMailTransportTests
{
    private const string HeaderBlock =
        "From: Ada <ada@example.test>\r\nSubject: Renewal\r\nMessage-ID: <m1@example.test>\r\n\r\n";

    /// <summary>
    /// An unencrypted session is refused before a single credential byte is written. BRD-135 requires
    /// refusal rather than a warning, and IMAP sends the password in the clear inside the session.
    /// </summary>
    [Fact]
    public async Task RefusesAnUnencryptedConnection()
    {
        var connection = new ScriptedImapConnection { IsSecure = false }.Line("* OK ready");
        var transport = Transport(connection);

        var error = await Assert.ThrowsAsync<ConnectorException>(() => transport.ListFoldersAsync());

        Assert.Contains("Plaintext IMAP is refused", error.Message, StringComparison.Ordinal);
        Assert.Empty(connection.Written);
        Assert.True(connection.IsDisposed);
    }

    /// <summary>A rejected credential is an honest run-level failure that does not echo the password.</summary>
    [Fact]
    public async Task ReportsRejectedCredentials()
    {
        var connection = new ScriptedImapConnection()
            .Line("* OK ready")
            .Line("T0001 NO [AUTHENTICATIONFAILED] Invalid credentials for ada@example.test");

        var error = await Assert.ThrowsAsync<ConnectorException>(() => Transport(connection).ListFoldersAsync());

        Assert.Equal(401, error.StatusCode);
        Assert.DoesNotContain("hunter2", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Container folders that hold no mail are not offered as folders to ingest.</summary>
    [Fact]
    public async Task ListsOnlySelectableFolders()
    {
        var connection = new ScriptedImapConnection()
            .Line("* OK ready")
            .Line("T0001 OK logged in")
            .Line("* LIST (\\HasNoChildren) \"/\" \"INBOX\"")
            .Line("* LIST (\\Noselect \\HasChildren) \"/\" \"Archive\"")
            .Line("* LIST (\\HasNoChildren) \"/\" \"Archive/2026\"")
            .Line("T0002 OK done");

        var folders = await Transport(connection).ListFoldersAsync();

        Assert.Equal(["INBOX", "Archive/2026"], folders);
    }

    /// <summary>The scope filters are sent as IMAP search keys, so the server does the filtering.</summary>
    [Fact]
    public async Task PushesTheScopeFiltersToTheServer()
    {
        var connection = Connected()
            .Line("* SEARCH")
            .Line("T0003 OK done");

        await Transport(connection).SearchAsync(
            "INBOX",
            new MailSearchCriteria(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "legal@", "renewal"),
            0,
            10);

        var search = connection.Written.Single(w => w.Contains("SEARCH", StringComparison.Ordinal));
        Assert.Contains("SINCE 02-Jan-2026", search, StringComparison.Ordinal);
        Assert.Contains("FROM \"legal@\"", search, StringComparison.Ordinal);
        Assert.Contains("SUBJECT \"renewal\"", search, StringComparison.Ordinal);
    }

    /// <summary>Headers arrive through a counted literal and are parsed into a usable header record.</summary>
    [Fact]
    public async Task ReadsHeadersThroughALiteral()
    {
        var connection = Connected()
            .Line("* SEARCH 5")
            .Line("T0003 OK done")
            .Line($"* 1 FETCH (UID 5 INTERNALDATE \"02-Jan-2026 10:00:00 +0000\" RFC822.SIZE 120 BODY[HEADER] {{{Length(HeaderBlock)}}}")
            .Literal(HeaderBlock)
            .Line(")")
            .Line("T0004 OK done");

        var page = await Transport(connection).SearchAsync("INBOX", new MailSearchCriteria(), 0, 10);

        var header = Assert.Single(page.Headers);
        Assert.Equal("5", header.Uid);
        Assert.Equal("4242", header.UidValidity);
        Assert.Equal("Renewal", header.Subject);
        Assert.Equal("Ada <ada@example.test>", header.From);
        Assert.Equal(120, header.SizeBytes);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero), header.Date);
        Assert.False(page.HasMore);
    }

    /// <summary>The UID list is paged, so a large folder is not fetched in one go.</summary>
    [Fact]
    public async Task PagesTheUidList()
    {
        var connection = Connected()
            .Line("* SEARCH 5 9 11")
            .Line("T0003 OK done")
            .Line($"* 1 FETCH (UID 5 RFC822.SIZE 10 BODY[HEADER] {{{Length(HeaderBlock)}}}")
            .Literal(HeaderBlock)
            .Line(")")
            .Line("T0004 OK done");

        var page = await Transport(connection).SearchAsync("INBOX", new MailSearchCriteria(), 0, 1);

        Assert.Single(page.Headers);
        Assert.True(page.HasMore);
    }

    /// <summary>
    /// Fetching uses BODY.PEEK, so ingesting a mailbox does not mark a year of mail as read — an act
    /// nobody can undo.
    /// </summary>
    [Fact]
    public async Task FetchesWithoutMarkingMailAsRead()
    {
        const string body = "Subject: Renewal\r\n\r\nApproved.\r\n";
        var connection = Connected()
            .Line($"* 1 FETCH (UID 5 BODY[] {{{Length(body)}}}")
            .Literal(body)
            .Line(")")
            .Line("T0003 OK done");

        var raw = await Transport(connection).FetchAsync(Header());

        Assert.Contains("Approved.", Encoding.Latin1.GetString(raw), StringComparison.Ordinal);
        Assert.Contains(connection.Written, w => w.Contains("BODY.PEEK[]", StringComparison.Ordinal));
        Assert.DoesNotContain(connection.Written, w => w.Contains("(BODY[]", StringComparison.Ordinal));
    }

    /// <summary>A folder that cannot be opened is a run-level failure that names the folder.</summary>
    [Fact]
    public async Task ReportsAFolderThatCannotBeOpened()
    {
        var connection = new ScriptedImapConnection()
            .Line("* OK ready")
            .Line("T0001 OK logged in")
            .Line("T0002 NO no such mailbox");

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => Transport(connection).SearchAsync("Nope", new MailSearchCriteria(), 0, 10));

        Assert.Contains("'Nope'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A message that vanished between search and fetch costs that message, not the run.</summary>
    [Fact]
    public async Task AMissingMessageIsNotARunLevelFailure()
    {
        var connection = Connected().Line("T0003 OK done");

        var error = await Record.ExceptionAsync(() => Transport(connection).FetchAsync(Header()));

        Assert.NotNull(error);
        Assert.IsNotType<ConnectorException>(error);
    }

    /// <summary>Search keys are joined as an implicit AND, and "everything" is spelled ALL.</summary>
    [Theory]
    [InlineData(null, null, "ALL")]
    [InlineData("legal@", null, "FROM \"legal@\"")]
    [InlineData("legal@", "renewal", "FROM \"legal@\" SUBJECT \"renewal\"")]
    public void BuildsSearchKeys(string? sender, string? subject, string expected) =>
        Assert.Equal(expected, ImapMailTransport.BuildSearchKeys(new MailSearchCriteria(null, sender, subject)));

    /// <summary>A trailing literal marker is recognised so the frame can be read by count.</summary>
    [Theory]
    [InlineData("* 1 FETCH (BODY[HEADER] {2048}", true, 2048)]
    [InlineData("* 1 FETCH (UID 5)", false, 0)]
    public void ReadsLiteralLengths(string line, bool expected, int length)
    {
        Assert.Equal(expected, ImapMailTransport.TryReadLiteralLength(line, out var actual));
        Assert.Equal(length, actual);
    }

    /// <summary>Folder names with spaces survive because tokenizing respects quotes.</summary>
    [Fact]
    public void ParsesAQuotedFolderName() =>
        Assert.Equal("Sent Items", ImapMailTransport.ParseListLine("* LIST (\\HasNoChildren) \".\" \"Sent Items\""));

    /// <summary>A non-LIST line is not mistaken for a folder.</summary>
    [Fact]
    public void IgnoresNonListLines() => Assert.Null(ImapMailTransport.ParseListLine("* 12 EXISTS"));

    private static ImapMailTransport Transport(IImapConnection connection) =>
        new(() => connection, new ImapMailboxOptions
        {
            Host = "imap.example.test",
            Username = "ada@example.test",
            Password = "hunter2",
        });

    private static ScriptedImapConnection Connected() =>
        new ScriptedImapConnection()
            .Line("* OK ready")
            .Line("T0001 OK logged in")
            .Line("* OK [UIDVALIDITY 4242] uids valid")
            .Line("T0002 OK [READ-WRITE] selected");

    private static MailHeader Header() =>
        new("INBOX", "5", "4242", "Renewal", "ada@example.test", "bob@example.test");

    private static int Length(string value) => Encoding.Latin1.GetByteCount(value);
}
