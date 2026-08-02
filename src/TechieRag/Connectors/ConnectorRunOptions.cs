namespace TechieRag.Connectors;

/// <summary>
/// Bounds on a single connector run (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// Like a crawl, a connector run can generate unbounded work from one click — a monorepo, a wiki
/// with forty thousand pages, a decade-old mailbox. Every default here is the conservative one, and
/// widening it is an explicit act by someone who knows what they pointed it at.
/// </remarks>
public sealed class ConnectorRunOptions
{
    /// <summary>Gets or sets the hard cap on items fetched in one run.</summary>
    /// <remarks>
    /// Also the memory bound: <see cref="ConnectorRunResult.Documents"/> holds the fetched text, so
    /// this number times <see cref="MaxItemBytes"/> is the worst case a run can occupy.
    /// </remarks>
    public int MaxItems { get; set; } = 500;

    /// <summary>Gets or sets the hard cap on listing pages walked in one run.</summary>
    /// <remarks>
    /// A second, independent stop. A source whose cursor never terminates — a paging bug, a folder
    /// that reports one more page forever — would otherwise loop until cancelled, having fetched
    /// nothing and therefore never reaching <see cref="MaxItems"/>.
    /// </remarks>
    public int MaxPages { get; set; } = 200;

    /// <summary>Gets or sets the largest item accepted, in bytes.</summary>
    /// <remarks>
    /// Applied from the listing where the source reports a size, so oversized items are skipped
    /// before they are downloaded. 2 MB is far beyond any file that is prose; a minified bundle or a
    /// checked-in database is not something anyone means to embed.
    /// </remarks>
    public long MaxItemBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Gets or sets the total bytes of item text a run may accumulate.</summary>
    /// <remarks>
    /// <para>The cap that actually bounds memory. <see cref="MaxItems"/> times
    /// <see cref="MaxItemBytes"/> is a worst case of a gigabyte at the defaults, and every source
    /// these connectors read can hit it: a docs repository is thousands of files that are each well
    /// under the per-item limit and enormous in aggregate. A per-item cap alone stops one bad file,
    /// never the sum of good ones.</para>
    /// <para>Enforced on the text actually fetched, because that is the only number that is true —
    /// a source's declared size is advisory where it is reported at all. Reaching it stops the run
    /// and sets <see cref="ConnectorRunResult.ReachedLimit"/>, so the sync state is kept and the next
    /// run resumes rather than starting over.</para>
    /// </remarks>
    public long MaxTotalBytes { get; set; } = 64 * 1024 * 1024;

    /// <summary>Gets or sets the pause between item fetches.</summary>
    /// <remarks>
    /// Politeness, and the cheapest rate-limit avoidance there is: a run that never sleeps will find
    /// the host's limiter for it. Zero in tests, where the clock is fake.
    /// </remarks>
    public TimeSpan RequestDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets or sets how many consecutive item failures end the run.</summary>
    /// <remarks>
    /// <para>Per-item failure not aborting the run is the point of this framework, but taken without
    /// limit it produces the worst outcome available: a run that spends an hour and the whole rate
    /// limit discovering that the token was revoked, then reports five thousand individual failures.
    /// A long unbroken streak is not a partial result, it is a broken source, and the run ends with a
    /// <see cref="ConnectorException"/> that says so.</para>
    /// <para>The counter resets on every success, so a source that fails one item in ten runs to
    /// completion however large it is. Set to 0 to disable.</para>
    /// </remarks>
    public int MaxConsecutiveFailures { get; set; } = 25;

    /// <summary>Gets or sets whether items skipped as unchanged are listed in the result.</summary>
    /// <remarks>Off by default: on an incremental run of a large source this list is nearly everything.</remarks>
    public bool ReportUnchanged { get; set; }
}
