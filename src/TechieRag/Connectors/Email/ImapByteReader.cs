using System.Text;

namespace TechieRag.Connectors.Email;

/// <summary>
/// The buffered byte pipe underneath <see cref="SocketImapConnection"/> (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>Why this is its own type.</b> Everything that makes an IMAP socket dangerous lives here
/// rather than in the TLS handshake: a line the server never terminates, a read that never returns,
/// a frame boundary lost between a text line and a counted literal. Those are the behaviours a
/// hostile server controls, and they are only testable if they can be driven over a stream the test
/// owns. <see cref="SocketImapConnection"/> keeps the TLS invariant — no code path produces a
/// plaintext session — and delegates the byte handling to this, which knows nothing about TLS and so
/// cannot weaken it.</para>
/// <para><b>Its own buffer, not a StreamReader.</b> The protocol interleaves text lines with counted
/// binary literals, so a reader that may buffer ahead by an arbitrary amount will consume the start
/// of a message body while looking for a newline and permanently lose the frame. This hands out
/// lines and exact byte counts from the same position.</para>
/// <para><b>Every read is bounded twice.</b> By length, because a server that sends an endless line
/// with no terminator would otherwise be answered by growing a buffer until the process dies; and by
/// time, because <see cref="System.Net.Sockets.TcpClient.ReceiveTimeout"/> has no effect on an
/// asynchronous read and a server that accepts a connection and then says nothing would otherwise
/// hang the run for as long as the caller was willing to wait.</para>
/// </remarks>
internal sealed class ImapByteReader
{
    private readonly Stream stream;
    private readonly string host;
    private readonly int maxLineBytes;
    private readonly TimeSpan timeout;
    private readonly byte[] buffer = new byte[16384];
    private int start;
    private int end;

    /// <summary>Initializes a new instance of the <see cref="ImapByteReader"/> class.</summary>
    /// <param name="stream">The already-negotiated transport stream.</param>
    /// <param name="host">Server name, used only in failure text.</param>
    /// <param name="maxLineBytes">The longest response line that will be accumulated.</param>
    /// <param name="timeout">How long any single read or write may block before the server is declared unresponsive.</param>
    public ImapByteReader(Stream stream, string host, int maxLineBytes, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(stream);
        this.stream = stream;
        this.host = host;
        this.maxLineBytes = maxLineBytes;
        this.timeout = timeout;
    }

    /// <summary>Writes one command line, appending the protocol's line terminator.</summary>
    /// <param name="line">The command, without a trailing newline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the line has been written.</returns>
    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");

        using var deadline = Deadline(cancellationToken);
        try
        {
            await stream.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
            await stream.FlushAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unresponsive("accept a command");
        }
    }

    /// <summary>Reads one line of response, without its terminator.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The line, or null when the server closed the connection.</returns>
    /// <exception cref="ConnectorException">The line exceeded the length bound, or the server stopped responding.</exception>
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new List<byte>(256);

        while (true)
        {
            if (start >= end && !await FillAsync(cancellationToken).ConfigureAwait(false))
            {
                return line.Count > 0 ? Decode(line) : null;
            }

            while (start < end)
            {
                var current = buffer[start++];
                if (current == (byte)'\n')
                {
                    if (line.Count > 0 && line[^1] == (byte)'\r')
                    {
                        line.RemoveAt(line.Count - 1);
                    }

                    return Decode(line);
                }

                line.Add(current);

                // A line this long is not a response any server produces; it is a server holding the
                // connection open and feeding bytes that will never be terminated.
                if (line.Count > maxLineBytes)
                {
                    throw new ConnectorException(
                        "email",
                        $"{host} sent a response line longer than {maxLineBytes} bytes without terminating it. The connection was dropped.");
                }
            }
        }
    }

    /// <summary>Reads exactly the requested number of bytes.</summary>
    /// <param name="count">How many bytes to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bytes read.</returns>
    /// <exception cref="ConnectorException">The connection ended, or stalled, before the count was satisfied.</exception>
    public async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var result = new byte[count];
        var written = 0;

        while (written < count)
        {
            if (start >= end && !await FillAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new ConnectorException(
                    "email", $"{host} closed the connection after {written} of {count} expected bytes.");
            }

            var take = Math.Min(end - start, count - written);
            Array.Copy(buffer, start, result, written, take);
            start += take;
            written += take;
        }

        return result;
    }

    private async Task<bool> FillAsync(CancellationToken cancellationToken)
    {
        start = 0;

        using var deadline = Deadline(cancellationToken);
        try
        {
            end = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unresponsive("send a response");
        }

        return end > 0;
    }

    private CancellationTokenSource Deadline(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        return deadline;
    }

    private ConnectorException Unresponsive(string what) => new(
        "email",
        $"{host} did not {what} within {timeout.TotalSeconds:F0}s. Raise ImapMailboxOptions.Timeout if the server is genuinely this slow.");

    // Latin-1 keeps every byte addressable as a char, so a header carrying an undeclared encoding
    // survives to MimeParser intact instead of being replaced with question marks here.
    private static string Decode(List<byte> line) => Encoding.Latin1.GetString([.. line]);
}
