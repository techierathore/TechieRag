using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// Wraps a connector so every listing and every item is reported while the run is happening, and so
/// each fetched document is ingested the moment it arrives (REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <para><b>Why a decorator.</b> <c>ConnectorRunner.RunAsync</c> returns once, at the end. The only
/// things that happen per item are a call to <see cref="IDataConnector.ListAsync"/> and a call to
/// <see cref="IDataConnector.FetchAsync"/>, so decorating the connector is the seam where "reading
/// 41 of 260" becomes observable — the identical trick
/// <see cref="Web.ProgressReportingWebContentFetcher"/> plays on the crawler, for the identical
/// reason. A repository or Confluence import takes minutes, and a spinner cannot be told apart from
/// a hang.</para>
/// <para><b>Why ingestion happens here too.</b> Putting it after the walk — which is what the
/// library's own <c>IngestConnectorAsync</c> does — means a run cancelled at minute nine of ten
/// ingests nothing, and the eight minutes of downloading are simply thrown away. Ingesting per item
/// makes cancellation honest: what the run says it ingested is already in the library and stays
/// there. Logged upstream as TR-RAG-022, which also names what a streaming library API would have to
/// yield for this decorator to become unnecessary.</para>
/// <para><b>Failures are recorded AND rethrown.</b> Swallowing one here would look like progress
/// reporting while in fact deleting the item from <c>ConnectorRunResult.Failures</c> and from the
/// runner's consecutive-failure circuit breaker — the run would then report every item as ingested
/// with items silently missing, the exact dishonesty BRD-65 exists to prevent.</para>
/// </remarks>
public sealed class ObservedConnector : IDataConnector
{
    private readonly IDataConnector inner;
    private readonly IJobProgressReporter progress;
    private readonly IConnectorDocumentSink sink;
    private readonly ConnectorJobPayload payload;
    private readonly object gate = new();
    private readonly HashSet<string> recordedIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> ingestedVersions = new(StringComparer.Ordinal);

    private int listed;
    private int ingested;

    /// <summary>Initializes a new instance of the <see cref="ObservedConnector"/> class.</summary>
    /// <param name="inner">The connector that does the real work.</param>
    /// <param name="progress">Where per-item results and live progress go.</param>
    /// <param name="sink">Where each fetched document is ingested, as it arrives.</param>
    /// <param name="payload">The run's payload, carrying the workspace and pinning choice.</param>
    public ObservedConnector(
        IDataConnector inner,
        IJobProgressReporter progress,
        IConnectorDocumentSink sink,
        ConnectorJobPayload payload)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.progress = progress ?? throw new ArgumentNullException(nameof(progress));
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    /// <inheritdoc />
    public string SourceType => inner.SourceType;

    /// <inheritdoc />
    public string SourceName => inner.SourceName;

    /// <inheritdoc />
    public bool ListsEntireSource => inner.ListsEntireSource;

    /// <summary>Gets how many items the listings have produced so far.</summary>
    public int ListedCount { get { lock (gate) { return listed; } } }

    /// <summary>Gets how many documents have been ingested so far.</summary>
    /// <remarks>
    /// The number that is true even if the process is killed in the next instant, because these
    /// documents are already in the catalogue.
    /// </remarks>
    public int IngestedCount { get { lock (gate) { return ingested; } } }

    /// <summary>Gets the ids of every item this decorator has already recorded a result for.</summary>
    /// <remarks>
    /// The run's reconciliation uses it to add the items only <c>ConnectorRunner</c> saw — items
    /// rejected on size before any fetch, and items skipped as unchanged — without recording anything
    /// twice.
    /// </remarks>
    public IReadOnlyCollection<string> RecordedItemIds
    {
        get { lock (gate) { return recordedIds.ToList(); } }
    }

    /// <summary>Gets the source version of each item successfully ingested by this run.</summary>
    /// <remarks>
    /// Kept so a cancelled run can still persist sync state for the items it did ingest. Without it,
    /// pressing Stop would leave the documents in the library but re-download every one of them on the
    /// next run.
    /// </remarks>
    public IReadOnlyDictionary<string, string> IngestedVersions
    {
        get { lock (gate) { return new Dictionary<string, string>(ingestedVersions, StringComparer.Ordinal); } }
    }

    /// <inheritdoc />
    public async Task<ConnectorPage> ListAsync(
        ConnectorListRequest request, CancellationToken cancellationToken = default)
    {
        Report($"Listing items in {SourceName}…");

        var page = await inner.ListAsync(request, cancellationToken).ConfigureAwait(false);

        lock (gate)
        {
            listed += page.Items.Count;
        }

        // A listing failure is a per-item result too — a truncated repository tree, a mail folder that
        // could not be selected. ConnectorRunner carries them to the end of the run; recording them
        // here puts them on screen at the moment they happen.
        foreach (var failure in page.Failures ?? [])
        {
            Record(RunItemStatus.Failed, failure.ItemId, failure.ItemName, failure.Reason);
        }

        Report($"Listed {ListedCount} item(s) in {SourceName}");
        return page;
    }

    /// <inheritdoc />
    public async Task<ConnectorDocument> FetchAsync(
        ConnectorItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        Report($"Reading {item.Name}");

        var document = await FetchInnerAsync(item, cancellationToken).ConfigureAwait(false);
        await IngestAsync(item, document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    /// <summary>Fetches one item, recording a per-item failure before letting the error through.</summary>
    /// <param name="item">The item to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fetched document.</returns>
    private async Task<ConnectorDocument> FetchInnerAsync(
        ConnectorItem item, CancellationToken cancellationToken)
    {
        try
        {
            return await inner.FetchAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The operator's decision, not the item's fault. Counting it as a failure would blame the
            // source for the Stop button and leave a run history nobody can trust.
            throw;
        }
        catch (ConnectorException)
        {
            // The connector is saying the whole run is over. Recording it against one item would hide
            // a revoked token behind a single bad file.
            throw;
        }
        catch (Exception exception)
        {
            Record(RunItemStatus.Failed, item.Id, item.Name, exception.Message);
            throw;
        }
    }

    /// <summary>Ingests one fetched document and records what became of it.</summary>
    /// <param name="item">The item the document came from.</param>
    /// <param name="document">The fetched document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the item's result is recorded.</returns>
    private async Task IngestAsync(
        ConnectorItem item, ConnectorDocument document, CancellationToken cancellationToken)
    {
        ConnectorIngestOutcome outcome;
        try
        {
            outcome = await sink
                .IngestAsync(inner, document, payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A sink that cannot take this one document costs this one document. Rethrowing keeps the
            // runner's own failure list and circuit breaker in step with what the screen was told.
            Record(RunItemStatus.Failed, item.Id, item.Name, exception.Message);
            throw;
        }

        if (!outcome.WasIngested)
        {
            Record(RunItemStatus.Skipped, item.Id, item.Name, outcome.Reason);
            return;
        }

        lock (gate)
        {
            ingested++;
            if (item.Version is not null)
            {
                ingestedVersions[item.Id] = item.Version;
            }
        }

        Record(RunItemStatus.Processed, item.Id, item.Name, outcome.Reason);
        Report($"Ingested {item.Name} ({IngestedCount} of {ListedCount} listed)");
    }

    /// <summary>Records one item's result, once.</summary>
    /// <param name="status">What happened to it.</param>
    /// <param name="itemId">The source's identifier for the item.</param>
    /// <param name="itemName">The human-facing name.</param>
    /// <param name="reason">Why, in operator terms.</param>
    private void Record(RunItemStatus status, string itemId, string itemName, string? reason)
    {
        lock (gate)
        {
            if (!recordedIds.Add(itemId))
            {
                return;
            }
        }

        progress.RecordItem(status, itemId, itemName, reason);
    }

    /// <summary>Pushes the current counts and a status line to whoever is watching.</summary>
    /// <param name="message">What is happening right now, in plain language.</param>
    private void Report(string message)
    {
        int done;
        int total;
        lock (gate)
        {
            done = ingested;
            total = listed;
        }

        // Total is the count LISTED so far, not a guess. It grows as pages are walked and the bar
        // moves with it; a percentage invented before anything has been listed is a bar that jumps
        // backwards, and one run of that teaches the user to stop believing it.
        progress.Report(done, total > 0 ? total : null, message);
    }
}
