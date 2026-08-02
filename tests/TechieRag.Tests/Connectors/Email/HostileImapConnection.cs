using TechieRag.Connectors.Email;

namespace TechieRag.Tests.Connectors.Email;

/// <summary>
/// A mail server that is not on the operator's side (REQ-RAG-049 / BRD-135).
/// </summary>
/// <remarks>
/// <para><b>Why the fake has to be hostile.</b> A scripted connection that replays a well-formed
/// conversation proves the parser handles a cooperative server, which is the case that was never in
/// doubt. Everything dangerous about a hand-written protocol client is what the <i>server</i>
/// controls: how long a line is, how large it says a literal will be, how many untagged responses it
/// sends before completing a command. Those are the bytes this fake owns.</para>
/// <para><b>It refuses rather than obliges.</b> <see cref="ReadExactAsync"/> records the size asked
/// for and then throws instead of allocating it. A real socket would allocate — which is the defect
/// — so obliging here would take the test host down instead of reporting the finding.</para>
/// </remarks>
internal sealed class HostileImapConnection : IImapConnection
{
    private readonly Queue<string> lines;
    private readonly Func<long, string?>? endless;

    /// <summary>Initializes a new instance of the <see cref="HostileImapConnection"/> class.</summary>
    /// <param name="scripted">The lines the server will send, in order.</param>
    /// <param name="endless">Called once the script runs out; returns a further line, or null to close.</param>
    public HostileImapConnection(IEnumerable<string> scripted, Func<long, string?>? endless = null)
    {
        lines = new Queue<string>(scripted);
        this.endless = endless;
    }

    /// <inheritdoc />
    public bool IsSecure => true;

    /// <summary>Gets the largest byte count the server persuaded the client to ask for.</summary>
    public long LargestRead { get; private set; }

    /// <summary>Gets how many response lines the client consumed.</summary>
    public long LinesRead { get; private set; }

    /// <summary>Gets the command lines the client put on the wire.</summary>
    public List<string> Written { get; } = [];

    /// <inheritdoc />
    public Task<string> OpenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(lines.Count > 0 ? lines.Dequeue() : "* OK ready");

    /// <inheritdoc />
    public Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        Written.Add(line);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        LinesRead++;

        if (lines.Count > 0)
        {
            return Task.FromResult<string?>(lines.Dequeue());
        }

        return Task.FromResult(endless?.Invoke(LinesRead));
    }

    /// <inheritdoc />
    public Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken = default)
    {
        LargestRead = Math.Max(LargestRead, count);
        throw new InvalidOperationException($"the server talked this client into allocating {count:N0} bytes");
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
