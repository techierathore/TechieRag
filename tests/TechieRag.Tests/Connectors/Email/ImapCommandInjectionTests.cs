using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// REQ-RAG-049 / BRD-135: nothing a caller supplies may become a second IMAP command.
/// </summary>
/// <remarks>
/// <para><b>Why this is the highest-value area of the connector.</b> IMAP frames commands by line.
/// Quoting a value escapes <c>\</c> and <c>"</c> so the quoted string stays well-formed, and does
/// nothing whatever about a carriage return — so a folder named
/// <c>INBOX\r\nT9 STORE 1:* +FLAGS (\Deleted)</c> is not a folder with an odd name, it is a delete
/// executed against the mailbox with this account's credentials. The connector's read-only promise
/// rests entirely on only ever sending <c>BODY.PEEK</c>, and an injected line is not sent by the
/// connector.</para>
/// <para><b>The values are attacker-reachable.</b> Folder names, the sender and subject filters and
/// the account name are connector configuration — supplied by whoever fills in the connector form,
/// not by the mailbox owner and not by the server.</para>
/// <para><b>The assertion is the invariant, not the symptom.</b> Each test asserts that no line put
/// on the wire contains a line break, which is what "one command per command" means. Asserting the
/// absence of the word STORE would pass against a payload spelled differently.</para>
/// </remarks>
public sealed class ImapCommandInjectionTests
{
    private const string Payload = "\r\nT0099 STORE 1:* +FLAGS (\\Deleted)";

    /// <summary>A folder name carrying a line break is refused, and nothing is sent for it.</summary>
    [Fact]
    public async Task RefusesAFolderNameThatWouldInjectACommand()
    {
        var connection = Connected();
        var transport = Transport(connection);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => transport.SearchAsync("INBOX" + Payload, new MailSearchCriteria(), 0, 10));

        Assert.Contains("control character", error.Message, StringComparison.OrdinalIgnoreCase);
        AssertOneCommandPerLine(connection);
    }

    /// <summary>A sender filter carrying a line break is refused before the search is issued.</summary>
    [Fact]
    public async Task RefusesASenderFilterThatWouldInjectACommand()
    {
        var connection = Connected();

        await Assert.ThrowsAsync<ConnectorException>(
            () => Transport(connection).SearchAsync("INBOX", new MailSearchCriteria(null, "legal@" + Payload), 0, 10));

        AssertOneCommandPerLine(connection);
    }

    /// <summary>A subject filter carrying a line break is refused before the search is issued.</summary>
    [Fact]
    public async Task RefusesASubjectFilterThatWouldInjectACommand()
    {
        var connection = Connected();

        await Assert.ThrowsAsync<ConnectorException>(
            () => Transport(connection).SearchAsync("INBOX", new MailSearchCriteria(null, null, "renewal" + Payload), 0, 10));

        AssertOneCommandPerLine(connection);
    }

    /// <summary>An account name carrying a line break never reaches the LOGIN command.</summary>
    [Fact]
    public async Task RefusesAnAccountNameThatWouldInjectACommand()
    {
        var connection = Connected();
        var transport = new ImapMailTransport(
            () => connection,
            new ImapMailboxOptions { Host = "imap.example.test", Username = "ada" + Payload, Password = "hunter2" });

        await Assert.ThrowsAsync<ConnectorException>(() => transport.ListFoldersAsync());

        AssertOneCommandPerLine(connection);
    }

    /// <summary>
    /// A U+0001 in the account name cannot forge a field inside the XOAUTH2 payload, where that
    /// character is the separator the server splits the decoded blob on.
    /// </summary>
    [Fact]
    public async Task RefusesAnAccountNameThatWouldForgeTheOAuthPayload()
    {
        var connection = Connected();
        var transport = new ImapMailTransport(
            () => connection,
            new ImapMailboxOptions
            {
                Host = "imap.example.test",
                Username = "ada\u0001auth=Bearer stolen",
                Password = "token",
                UseOAuthBearer = true,
            });

        await Assert.ThrowsAsync<ConnectorException>(() => transport.ListFoldersAsync());

        Assert.DoesNotContain(connection.Written, w => w.Contains("AUTHENTICATE", StringComparison.Ordinal));
    }

    /// <summary>
    /// A message identifier that is not a bare UID cannot be fetched. UIDs are unquoted numbers in
    /// the protocol, so no escaping would make an arbitrary string safe in that position — and not
    /// every payload there needs a line break to do damage. <c>1:*</c> is a valid IMAP sequence set
    /// meaning "every message in the folder", so a header carrying it turns one fetch into a download
    /// of the whole mailbox, past every scope filter that decided what to fetch.
    /// </summary>
    /// <param name="uid">The identifier a caller supplied on the header.</param>
    [Theory]
    [InlineData("5\r\nT0099 LOGOUT")]
    [InlineData("1:*")]
    [InlineData("5,6,7")]
    [InlineData("<message-id@example.test>")]
    public async Task RefusesAMessageIdentifierThatIsNotAUid(string uid)
    {
        var connection = Connected();
        var header = new MailHeader("INBOX", uid, "1", "Renewal", "ada@x", "bob@x");

        await Assert.ThrowsAsync<ConnectorException>(() => Transport(connection).FetchAsync(header));

        AssertOneCommandPerLine(connection);
        Assert.DoesNotContain(connection.Written, w => w.Contains("FETCH", StringComparison.Ordinal));
    }

    /// <summary>An ordinary folder name is still quoted and sent, so the guard is not simply refusing everything.</summary>
    [Fact]
    public async Task StillSendsAnOrdinaryFolderName()
    {
        var connection = Connected();

        await Transport(connection).SearchAsync("Archive/2026 Renewals", new MailSearchCriteria(), 0, 10);

        Assert.Contains(connection.Written, w => w.Contains("SELECT \"Archive/2026 Renewals\"", StringComparison.Ordinal));
        AssertOneCommandPerLine(connection);
    }

    private static void AssertOneCommandPerLine(HostileImapConnection connection) =>
        Assert.All(connection.Written, written =>
        {
            Assert.DoesNotContain('\r', written);
            Assert.DoesNotContain('\n', written);
        });

    private static ImapMailTransport Transport(IImapConnection connection) =>
        new(
            () => connection,
            new ImapMailboxOptions { Host = "imap.example.test", Username = "ada@example.test", Password = "hunter2" });

    private static HostileImapConnection Connected() =>
        new(
            [
                "* OK ready",
                "T0001 OK logged in",
                "* OK [UIDVALIDITY 4242] uids valid",
                "T0002 OK [READ-WRITE] selected",
                "* SEARCH",
                "T0003 OK done",
                "T0004 OK done",
            ]);
}
