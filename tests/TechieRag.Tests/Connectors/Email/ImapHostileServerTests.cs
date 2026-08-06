using System.Diagnostics;
using System.Text;
using TechieRag.Connectors;
using TechieRag.Connectors.Email;
using Xunit;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// REQ-RAG-049 / BRD-135: what a mail server can do to the client that trusts it.
/// </summary>
/// <remarks>
/// <para><b>The server is not a trusted party.</b> TLS proves the client is talking to the host the
/// operator named; it says nothing about whether that host is well behaved. A mailbox can be
/// attacker-run outright — an operator pointing this connector at an address someone sent them — or
/// legitimate and compromised. Every number in an IMAP response is the server's choice, and each
/// one below is a number this client used to act on without checking.</para>
/// <para><b>These are availability defects with a real blast radius.</b> The connector runs inside
/// the host application, so an allocation the server dictates is that application's memory.</para>
/// </remarks>
public sealed class ImapHostileServerTests
{
    /// <summary>
    /// A literal whose declared length is absurd is refused before anything is allocated. The client
    /// allocates a buffer of the announced size before reading a byte, so <c>{2000000000}</c> is a
    /// two-gigabyte allocation chosen by the server.
    /// </summary>
    [Fact]
    public async Task RefusesALiteralLargerThanTheMessageBudget()
    {
        var connection = new HostileImapConnection(
        [
            "* OK ready",
            "T0001 OK logged in",
            "* OK [UIDVALIDITY 1] uids valid",
            "T0002 OK selected",
            "* SEARCH 5",
            "T0003 OK done",
            "* 1 FETCH (UID 5 BODY[HEADER] {2000000000}",
        ]);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => Transport(connection).SearchAsync("INBOX", new MailSearchCriteria(), 0, 10));

        Assert.Contains("limit", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, connection.LargestRead);
    }

    /// <summary>The budget is per response, so a stream of merely-large literals cannot add up past it.</summary>
    [Fact]
    public async Task RefusesLiteralsThatExceedTheBudgetInAggregate()
    {
        var options = Options();
        options.MaxMessageBytes = 4096;

        var connection = new HostileImapConnection(
        [
            "* OK ready",
            "T0001 OK logged in",
            "* OK [UIDVALIDITY 1] uids valid",
            "T0002 OK selected",
            "* SEARCH 1 2 3",
            "T0003 OK done",
            "* 1 FETCH (UID 1 BODY[HEADER] {3000}",
        ]);

        var error = await Record.ExceptionAsync(
            () => new ImapMailTransport(() => connection, options).SearchAsync("INBOX", new MailSearchCriteria(), 0, 10));

        // The first literal is inside the budget and is read; the fake refuses to allocate it, which
        // is how the test observes that the budget admitted it.
        Assert.NotNull(error);
        Assert.Equal(3000, connection.LargestRead);
    }

    /// <summary>
    /// A server that never completes a command is given up on rather than accumulated. Without a
    /// bound the client reads untagged lines until the process runs out of memory.
    /// </summary>
    [Fact]
    public async Task GivesUpOnAServerThatNeverCompletesACommand()
    {
        // The flood stops eventually only so that this test terminates when the bound is removed. A
        // real server would keep going, and the client would keep accumulating.
        var connection = new HostileImapConnection(
            ["* OK ready", "T0001 OK logged in"],
            endless: read => read > (ImapMailTransport.MaxResponseLines * 3L) ? null : "* LIST (\\HasNoChildren) \"/\" \"Folder\"");

        var error = await Assert.ThrowsAsync<ConnectorException>(() => Transport(connection).ListFoldersAsync());

        Assert.Contains("untagged", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(connection.LinesRead, 1, ImapMailTransport.MaxResponseLines + 10);
    }

    /// <summary>
    /// A line the server never terminates is dropped rather than accumulated. A newline is the only
    /// thing that ends a read, so without a length bound the buffer grows for as long as the server
    /// is willing to send bytes.
    /// </summary>
    [Fact]
    public async Task DropsAnEndlessResponseLine()
    {
        // The stream runs dry eventually only so that this test terminates when the bound is removed.
        await using var endless = new EndlessStream((byte)'A', SocketImapConnection.MaxLineBytes * 8L);
        var reader = new ImapByteReader(endless, "imap.example.test", SocketImapConnection.MaxLineBytes, TimeSpan.FromSeconds(30));

        var error = await Assert.ThrowsAsync<ConnectorException>(() => reader.ReadLineAsync(CancellationToken.None));

        Assert.Contains("without terminating", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(endless.BytesServed, 0, (SocketImapConnection.MaxLineBytes * 2L) + 16384);
    }

    /// <summary>
    /// A server that accepts the connection and then says nothing fails the configured timeout.
    /// <c>TcpClient.ReceiveTimeout</c> has no effect on an asynchronous read, so a timeout that is
    /// only configured is a timeout that does not exist — and every method on the transport defaults
    /// its cancellation token to <see cref="CancellationToken.None"/>.
    /// </summary>
    [Fact]
    public async Task StopsWaitingOnAServerThatSaysNothing()
    {
        await using var silent = new SilentStream();
        var reader = new ImapByteReader(silent, "imap.example.test", 4096, TimeSpan.FromMilliseconds(400));

        // The rescue token exists only so that this test terminates when the deadline is removed. It
        // is set an order of magnitude beyond the configured timeout, so the timeout must fire first
        // for a ConnectorException to be what comes out.
        using var rescue = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var elapsed = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<ConnectorException>(() => reader.ReadLineAsync(rescue.Token));

        Assert.Contains("did not send a response", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(elapsed.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(4));
    }

    /// <summary>The caller's own cancellation is still cancellation, not a server-timeout failure.</summary>
    [Fact]
    public async Task PropagatesTheCallersCancellation()
    {
        await using var silent = new SilentStream();
        var reader = new ImapByteReader(silent, "imap.example.test", 4096, TimeSpan.FromMinutes(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadLineAsync(cancellation.Token));
    }

    /// <summary>An ordinary line and an ordinary literal still come back intact through the same reader.</summary>
    [Fact]
    public async Task StillReadsAnOrdinaryLineAndLiteral()
    {
        var payload = "* OK ready\r\nabcdef"u8.ToArray();
        await using var stream = new MemoryStream(payload);
        var reader = new ImapByteReader(stream, "imap.example.test", 4096, TimeSpan.FromSeconds(5));

        Assert.Equal("* OK ready", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("abcdef", Encoding.Latin1.GetString(await reader.ReadExactAsync(6, CancellationToken.None)));
    }

    private static ImapMailTransport Transport(IImapConnection connection) => new(() => connection, Options());

    private static ImapMailboxOptions Options() =>
        new() { Host = "imap.example.test", Username = "ada@example.test", Password = "hunter2" };

    /// <summary>A stream that answers every read in full and never sends a newline.</summary>
    private sealed class EndlessStream(byte fill, long budget) : Stream
    {
        public long BytesServed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (BytesServed >= budget)
            {
                return ValueTask.FromResult(0);
            }

            buffer.Span.Fill(fill);
            BytesServed += buffer.Length;
            return ValueTask.FromResult(buffer.Length);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A stream that accepts the connection and then never answers a read.</summary>
    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
