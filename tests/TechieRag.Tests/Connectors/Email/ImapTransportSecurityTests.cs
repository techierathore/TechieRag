using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// REQ-RAG-049 / BRD-135: TLS is required, and the target is the one the operator named.
/// </summary>
/// <remarks>
/// <para><b>The loopback server is the point.</b> Reading the code shows there is no STARTTLS branch
/// and no certificate callback; only speaking to a socket shows what happens when a server answers
/// in plaintext, which is the case BRD-135 says must be refused rather than warned about. The server
/// below listens on loopback, so this test needs no network and reaches nothing.</para>
/// <para><b>Blanket SSRF blocking would be wrong here, and this says why.</b> Unlike the HTTP
/// connectors, a mail host is named directly by the operator and is legitimately arbitrary —
/// corporate IMAP lives on private addresses far more often than not, so refusing RFC 1918 targets
/// would break the ordinary case while protecting nothing. The property that matters instead is that
/// the target cannot be changed by anything the <i>server</i> says: there is no redirect, no
/// response-supplied host, and no second connection. That is what
/// <see cref="ConnectsOnlyToTheConfiguredHost"/> pins.</para>
/// </remarks>
public sealed class ImapTransportSecurityTests
{
    /// <summary>
    /// A server that answers in plaintext gets no credential and no session. This is a real IMAP
    /// greeting on a real socket — exactly what port 143 sends — and the connection is refused at
    /// the handshake rather than downgraded to it.
    /// </summary>
    [Fact]
    public async Task RefusesAPlaintextImapServer()
    {
        using var server = new LoopbackServer("* OK [CAPABILITY IMAP4rev1 STARTTLS] ready\r\n");
        using var connection = new SocketImapConnection("127.0.0.1", server.Port, TimeSpan.FromSeconds(10));

        var error = await Assert.ThrowsAsync<ConnectorException>(() => connection.OpenAsync(CancellationToken.None));

        Assert.Contains("TLS", error.Message, StringComparison.Ordinal);
        Assert.False(connection.IsSecure);

        // The client sends a TLS ClientHello and nothing else. What it must never send is protocol
        // text: a STARTTLS negotiation, or worse a LOGIN, to a server that answered in the clear.
        var sent = Encoding.ASCII.GetString([.. server.Received]);
        Assert.DoesNotContain("LOGIN", sent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("STARTTLS", sent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CAPABILITY", sent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A server that accepts the connection and then says nothing fails the configured timeout
    /// rather than holding the run open. The transport's own methods default their cancellation
    /// token to none, so nothing else would ever free it.
    /// </summary>
    [Fact]
    public async Task StopsWaitingOnASilentServer()
    {
        using var server = new LoopbackServer(greeting: null);
        using var connection = new SocketImapConnection("127.0.0.1", server.Port, TimeSpan.FromMilliseconds(500));

        // The rescue token exists only so that this test terminates when the deadline is removed.
        using var rescue = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var error = await Assert.ThrowsAsync<ConnectorException>(() => connection.OpenAsync(rescue.Token));

        Assert.Contains("within", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// There is no supported way to accept a certificate the platform rejected. A hand-rolled client
    /// that grows an "ignore certificate errors" switch has given up the only thing that ties the
    /// credential to the host the operator named.
    /// </summary>
    [Fact]
    public void ExposesNoWayToDisableCertificateValidation()
    {
        var surface = typeof(SocketImapConnection).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(SocketImapConnection).Namespace && t.IsPublic)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .ToList();

        Assert.DoesNotContain(surface, p => p.PropertyType == typeof(RemoteCertificateValidationCallback));
        Assert.DoesNotContain(surface, p => p.PropertyType == typeof(SslClientAuthenticationOptions));
        Assert.DoesNotContain(
            surface,
            p => p.PropertyType == typeof(bool)
                 && (p.Name.Contains("Ssl", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Tls", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Certificate", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Insecure", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Nothing the server says moves the client to a different host. A whole run — authenticate,
    /// list, select, search, fetch — opens exactly one connection, and the responses below are full
    /// of addresses that a redirect-following client would chase.
    /// </summary>
    [Fact]
    public async Task ConnectsOnlyToTheConfiguredHost()
    {
        const string headerBlock = "From: Ada <ada@example.test>\r\nSubject: Renewal\r\n\r\n";
        var opened = 0;
        var connection = new HostileImapConnection(
        [
            "* OK [REFERRAL imap://evil.example.invalid/] ready",
            "T0001 OK [REFERRAL imap://evil.example.invalid/] logged in",
            "* LIST (\\HasNoChildren) \"/\" \"INBOX\"",
            "T0002 OK done",
            "* OK [UIDVALIDITY 1] uids valid",
            "T0003 OK selected",
            "* SEARCH 5",
            "T0004 OK done",
            $"* 1 FETCH (UID 5 BODY[HEADER] {{{Encoding.Latin1.GetByteCount(headerBlock)}}}",
            "T0005 OK done",
        ]);

        var transport = new ImapMailTransport(
            () =>
            {
                opened++;
                return connection;
            },
            new ImapMailboxOptions { Host = "imap.example.test", Username = "ada", Password = "hunter2" });

        var folders = await transport.ListFoldersAsync(CancellationToken.None);
        await Record.ExceptionAsync(() => transport.SearchAsync("INBOX", new MailSearchCriteria(), 0, 10));

        Assert.Equal(["INBOX"], folders);
        Assert.Equal(1, opened);
        Assert.Equal("imap.example.test", transport.MailboxName);
    }

    /// <summary>A loopback TCP server that optionally sends one greeting and never speaks TLS.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource shutdown = new();

        public LoopbackServer(string? greeting)
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _ = ServeAsync(greeting);
        }

        public int Port { get; }

        /// <summary>Gets the bytes the client sent. A refused handshake must contain no credential.</summary>
        public List<byte> Received { get; } = [];

        public void Dispose()
        {
            shutdown.Cancel();
            listener.Stop();
            shutdown.Dispose();
        }

        private async Task ServeAsync(string? greeting)
        {
            try
            {
                using var accepted = await listener.AcceptTcpClientAsync(shutdown.Token).ConfigureAwait(false);
                var stream = accepted.GetStream();

                if (greeting is not null)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(greeting), shutdown.Token).ConfigureAwait(false);
                }

                var buffer = new byte[4096];
                while (!shutdown.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, shutdown.Token).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        return;
                    }

                    Received.AddRange(buffer[..read]);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
            {
                // The test finished, or the client dropped the connection. Both are the expected end.
            }
        }
    }
}
