using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

namespace TechieRag.Connectors.Email;

/// <summary>
/// A TLS socket to an IMAP server (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>TLS is not optional and there is no flag to make it so.</b> BRD-135 requires that a
/// plaintext connection be refused rather than warned about, and the way to guarantee that is to
/// have no code path that produces one: this type always negotiates TLS immediately on connect, and
/// <see cref="IsSecure"/> is only true once it has. IMAP authentication sends the password in the
/// clear inside the session, so an unencrypted session is not a weaker connection — it is a
/// disclosed credential.</para>
/// <para><b>There is no STARTTLS path, deliberately.</b> A session that begins in plaintext and asks
/// to be upgraded can have the upgrade offer stripped by anyone on the path, and the client then has
/// to decide whether to carry on unencrypted. Never starting in plaintext removes the decision.</para>
/// <para><b>Certificate validation is the platform's.</b> There is no remote-certificate validation
/// callback here, no accept-all, and no option to install one — the host name the caller configured
/// is the name the certificate is checked against, and that is the whole of this connector's defence
/// against being pointed somewhere else.</para>
/// <para><b>Timeouts are enforced, not merely configured.</b>
/// <see cref="TcpClient.ReceiveTimeout"/> has no effect on an asynchronous read, so every connect,
/// handshake, read and write below is bounded by an explicit deadline. Without one, a server that
/// accepts the connection and then says nothing holds the run open for as long as the caller is
/// willing to wait — which, on the default <see cref="CancellationToken"/>, is forever.</para>
/// </remarks>
public sealed class SocketImapConnection : IImapConnection
{
    /// <summary>The longest response line that will be accumulated before the server is judged hostile.</summary>
    /// <remarks>
    /// RFC 3501 caps a command line at 8192 octets and no real server's response line approaches
    /// this. A line beyond it is a server feeding bytes it never intends to terminate, which is
    /// answered by dropping the connection rather than by growing a buffer until the process dies.
    /// </remarks>
    public const int MaxLineBytes = 64 * 1024;

    private readonly string host;
    private readonly int port;
    private readonly TimeSpan timeout;
    private TcpClient? client;
    private SslStream? stream;
    private ImapByteReader? reader;

    /// <summary>Initializes a new instance of the <see cref="SocketImapConnection"/> class.</summary>
    /// <param name="host">Server host name. Also the name TLS validates against.</param>
    /// <param name="port">Server port; 993 for implicit TLS.</param>
    /// <param name="timeout">Connect, read and write timeout, applied to each operation individually.</param>
    public SocketImapConnection(string host, int port = 993, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        this.host = host;
        this.port = port;
        this.timeout = timeout is { } supplied && supplied > TimeSpan.Zero ? supplied : TimeSpan.FromSeconds(60);
    }

    /// <inheritdoc />
    public bool IsSecure => stream is { IsEncrypted: true };

    /// <inheritdoc />
    public async Task<string> OpenAsync(CancellationToken cancellationToken = default)
    {
        client = new TcpClient();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        try
        {
            await client.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false);
            stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);

            // Certificate validation is left at the platform default on purpose: there is no
            // callback here that could be talked into accepting a bad certificate, and no option to
            // add one.
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ConnectorException(
                "email",
                $"{host}:{port} did not complete a TLS session within {timeout.TotalSeconds:F0}s. Check the address and port, or raise ImapMailboxOptions.Timeout.");
        }
        catch (Exception ex) when (ex is SocketException or AuthenticationException or IOException)
        {
            throw new ConnectorException("email", $"{host}:{port} could not be reached over TLS: {ex.Message}", ex);
        }

        if (!IsSecure)
        {
            throw new ConnectorException("email", $"{host}:{port} did not establish an encrypted session. Plaintext IMAP is refused.");
        }

        reader = new ImapByteReader(stream, host, MaxLineBytes, timeout);

        var greeting = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return greeting ?? throw new ConnectorException("email", $"{host}:{port} closed the connection without a greeting.");
    }

    /// <inheritdoc />
    public Task WriteLineAsync(string line, CancellationToken cancellationToken = default) =>
        Pipe().WriteLineAsync(line, cancellationToken);

    /// <inheritdoc />
    public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
        Pipe().ReadLineAsync(cancellationToken);

    /// <inheritdoc />
    public Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken = default) =>
        Pipe().ReadExactAsync(count, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        stream?.Dispose();
        client?.Dispose();
        stream = null;
        client = null;
        reader = null;
    }

    private ImapByteReader Pipe() =>
        reader ?? throw new ConnectorException("email", "The IMAP connection is not open.");
}
