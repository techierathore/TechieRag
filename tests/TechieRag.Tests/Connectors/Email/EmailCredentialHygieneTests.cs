using System.Globalization;
using Microsoft.Extensions.Logging;
using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// REQ-RAG-049 / BRD-135: the password must not survive anywhere but the wire.
/// </summary>
/// <remarks>
/// <para><b>Why assert it rather than read for it.</b> A mailbox password is the one input in this
/// connector that is worth more than everything it reads, and the ways it escapes are all
/// incidental: a log line that echoes the command, an exception that repeats the server's own text
/// back, a failure message built by interpolating the options record. None of those look like a leak
/// while being written, and all of them are caught by driving every failure path and searching
/// everything that came out.</para>
/// <para><b>Both directions are checked.</b> Exception messages and their full
/// <see cref="Exception.ToString"/> — which carries inner exceptions — and every log line at every
/// level, because a diagnostic that is only emitted at Debug is still a diagnostic somebody turns
/// on.</para>
/// </remarks>
public sealed class EmailCredentialHygieneTests
{
    private const string Password = "correct-horse-battery-staple";

    /// <summary>A rejected credential is reported without the credential.</summary>
    [Fact]
    public async Task RejectedCredentialsAreReportedWithoutThePassword()
    {
        var logger = new RecordingLogger<ImapMailTransport>();
        var connection = new HostileImapConnection(
            ["* OK ready", $"T0001 NO [AUTHENTICATIONFAILED] password {Password} is wrong"]);

        var error = await Assert.ThrowsAsync<ConnectorException>(() => Transport(connection, logger).ListFoldersAsync());

        Assert.Equal(401, error.StatusCode);
        AssertNoLeak(error, logger);
    }

    /// <summary>A successful authentication is logged without the credential.</summary>
    [Fact]
    public async Task ASuccessfulLoginIsLoggedWithoutThePassword()
    {
        var logger = new RecordingLogger<ImapMailTransport>();
        var connection = new HostileImapConnection(
            ["* OK ready", "T0001 OK logged in", "* LIST (\\HasNoChildren) \"/\" \"INBOX\"", "T0002 OK done"]);

        await Transport(connection, logger).ListFoldersAsync();

        Assert.Contains(logger.Messages, m => m.Contains("Authenticated", StringComparison.Ordinal));
        AssertNoLeak(null, logger);
    }

    /// <summary>A connection that drops mid-LOGIN does not carry the credential into the failure.</summary>
    [Fact]
    public async Task AConnectionLostDuringLoginDoesNotEchoThePassword()
    {
        var logger = new RecordingLogger<ImapMailTransport>();
        var connection = new HostileImapConnection(["* OK ready"]);

        var error = await Assert.ThrowsAsync<ConnectorException>(() => Transport(connection, logger).ListFoldersAsync());

        AssertNoLeak(error, logger);
    }

    /// <summary>A refused credential value is refused without being repeated back.</summary>
    [Fact]
    public async Task AMalformedPasswordIsRefusedWithoutBeingEchoed()
    {
        var logger = new RecordingLogger<ImapMailTransport>();
        var connection = new HostileImapConnection(["* OK ready"]);
        var transport = new ImapMailTransport(
            () => connection,
            new ImapMailboxOptions { Host = "imap.example.test", Username = "ada", Password = Password + "\r\nT9 LOGOUT" },
            logger);

        var error = await Assert.ThrowsAsync<ConnectorException>(() => transport.ListFoldersAsync());

        AssertNoLeak(error, logger);
    }

    /// <summary>The options record does not render its own credential when it is formatted into text.</summary>
    [Fact]
    public void TheOptionsRecordDoesNotRenderItsCredential()
    {
        var options = new ImapMailboxOptions { Host = "imap.example.test", Username = "ada", Password = Password };

        Assert.DoesNotContain(Password, options.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Password,
            string.Create(CultureInfo.InvariantCulture, $"{options}"),
            StringComparison.Ordinal);
    }

    private static void AssertNoLeak(Exception? error, RecordingLogger<ImapMailTransport> logger)
    {
        if (error is not null)
        {
            Assert.DoesNotContain(Password, error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, error.ToString(), StringComparison.Ordinal);
        }

        Assert.All(logger.Messages, message => Assert.DoesNotContain(Password, message, StringComparison.Ordinal));
    }

    private static ImapMailTransport Transport(IImapConnection connection, ILogger<ImapMailTransport> logger) =>
        new(
            () => connection,
            new ImapMailboxOptions { Host = "imap.example.test", Username = "ada@example.test", Password = Password },
            logger);
}
