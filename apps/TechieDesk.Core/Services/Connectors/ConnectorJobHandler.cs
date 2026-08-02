using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// Runs one connector as a background job, with live progress, per-item results and a per-item
/// reason for everything that was not ingested (REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <para><b>No second scheduler.</b> This is an <see cref="IScheduledJobHandler"/> — the seam
/// <c>AddTechieDeskScheduling</c> already defines (ADR-009). A connector sync started by hand from
/// the connector hub and one started by a schedule are the same code on the same runner, writing the
/// same run row and the same per-item rows; only <see cref="RunTrigger"/> differs. Standing up a
/// parallel job mechanism for connectors would have meant two in-flight guards, two progress
/// vocabularies and two run histories inside one desktop process.</para>
/// <para><b>The handler does not decide its own outcome.</b> It reports what it saw;
/// <see cref="JobRunner"/> classifies. That is what stops "412 of 500" being recorded as a
/// success.</para>
/// <para><b>Resolved per run, in its own scope.</b> A job handler is a singleton — it is held by the
/// scheduler for the life of the process and, in the helper host, for the life of a machine session.
/// The connector, its credential and the document sink are per-run concerns, so each run opens a
/// scope and lets it go again.</para>
/// </remarks>
public sealed class ConnectorJobHandler : IScheduledJobHandler
{
    /// <summary>The handler key stored on schedules that sync a connector.</summary>
    public const string Kind = "Connector";

    /// <summary>
    /// The prefix the library puts on a per-item failure that is really a policy skip.
    /// </summary>
    /// <remarks>
    /// <c>ConnectorRunner</c> reports an oversized item through <c>ConnectorItemFailure</c>, the same
    /// type it uses for a genuine fetch failure, so the only thing separating "we chose not to read
    /// this" from "we could not read this" is this prefix. Matching on it keeps a deliberate skip out
    /// of the failure count — a run that skipped one 400 MB binary is not a partial run. Logged
    /// upstream as TR-RAG-023.
    /// </remarks>
    private const string LibrarySkipPrefix = "Skipped:";

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ConnectorRunner runner;
    private readonly ILogger<ConnectorJobHandler> logger;

    /// <summary>Initializes a new instance of the <see cref="ConnectorJobHandler"/> class.</summary>
    /// <param name="scopeFactory">Opens a per-run scope for the resolver and the document sink.</param>
    /// <param name="runnerLogger">Diagnostics for the library's connector runner.</param>
    /// <param name="timeProvider">Clock, so the politeness delay is testable without real waiting.</param>
    /// <param name="logger">Diagnostics.</param>
    public ConnectorJobHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<ConnectorRunner> runnerLogger,
        TimeProvider timeProvider,
        ILogger<ConnectorJobHandler> logger)
    {
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        runner = new ConnectorRunner(runnerLogger, timeProvider);
    }

    /// <inheritdoc />
    public string JobKind => Kind;

    /// <inheritdoc />
    public string DisplayName => "Sync a connector";

    /// <inheritdoc />
    public string Description =>
        "Reads a repository, wiki or mailbox and adds what changed to the document library.";

    /// <inheritdoc />
    public string DescribeAction(string? payload) =>
        ConnectorJobPayload.TryParse(payload)?.Describe() ?? "Sync a connector (configuration missing)";

    /// <inheritdoc />
    public string? ValidatePayload(string? payload)
    {
        var parsed = ConnectorJobPayload.TryParse(payload);
        if (parsed is null)
        {
            return "This connector run has no readable configuration.";
        }

        var invalid = parsed.Validate();
        if (invalid is not null)
        {
            return invalid;
        }

        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IConnectorResolver>().Validate(parsed);
    }

    /// <inheritdoc />
    public async Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var payload = ConnectorJobPayload.TryParse(context.Payload);
        if (payload is null)
        {
            return JobRunResult.Failed("This connector run has no readable configuration.");
        }

        var invalid = payload.Validate();
        if (invalid is not null)
        {
            return JobRunResult.Failed(invalid);
        }

        using var scope = scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IConnectorResolver>();
        var sink = scope.ServiceProvider.GetRequiredService<IConnectorDocumentSink>();

        var rejected = resolver.Validate(payload);
        if (rejected is not null)
        {
            return JobRunResult.Failed(rejected);
        }

        ResolvedConnector resolved;
        try
        {
            resolved = await resolver.ResolveAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (ConnectorException exception)
        {
            // Nothing has been attempted, so this is a run failure and not a partial result.
            logger.LogError(exception, "Connector {ConnectorId} could not be opened", payload.ConnectorId);
            return JobRunResult.Failed(exception.Message);
        }

        return await WalkAsync(context, payload, resolver, sink, resolved, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Walks the connector, recording every item and keeping what it ingested.</summary>
    /// <param name="context">The run context.</param>
    /// <param name="payload">What to read.</param>
    /// <param name="resolver">The connector seam, used again to persist sync state.</param>
    /// <param name="sink">Where each fetched document goes.</param>
    /// <param name="resolved">The live connector and the previous run's state.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>What the handler observed.</returns>
    private async Task<JobRunResult> WalkAsync(
        JobRunContext context,
        ConnectorJobPayload payload,
        IConnectorResolver resolver,
        IConnectorDocumentSink sink,
        ResolvedConnector resolved,
        CancellationToken cancellationToken)
    {
        var observed = new ObservedConnector(resolved.Connector, context.Progress, sink, payload);

        try
        {
            var result = await runner
                .RunAsync(observed, resolved.PreviousSync, payload.ToRunOptions(), cancellationToken)
                .ConfigureAwait(false);

            Reconcile(context.Progress, observed, result);
            await resolver.SaveSyncAsync(payload, result.Sync, cancellationToken).ConfigureAwait(false);
            return new JobRunResult(ComposeDetail(observed, result.ReachedLimit));
        }
        catch (OperationCanceledException)
        {
            // Cancelling keeps what was already ingested — and keeps the sync state that goes with it,
            // or the next run would re-download every document the user can already see.
            await KeepPartialSyncAsync(resolver, payload, resolved, observed).ConfigureAwait(false);
            logger.LogInformation(
                "Connector run '{JobName}' was cancelled after ingesting {Ingested} item(s), which were kept",
                context.JobName,
                observed.IngestedCount);
            throw;
        }
        catch (ConnectorException exception)
        {
            // The source died part-way. Whatever reached the library stays there and stays recorded;
            // the run itself is Failed, and the reason names the source-level problem rather than
            // blaming the last item.
            await KeepPartialSyncAsync(resolver, payload, resolved, observed).ConfigureAwait(false);
            logger.LogError(exception, "Connector run '{JobName}' stopped", context.JobName);
            return JobRunResult.Failed(exception.Message);
        }
    }

    /// <summary>
    /// Records the items the runner saw but the decorator never did — unchanged items and items
    /// rejected on size before any fetch.
    /// </summary>
    /// <param name="progress">Where per-item results go.</param>
    /// <param name="observed">The decorator, which knows what it already recorded.</param>
    /// <param name="result">What the library's runner reported.</param>
    /// <remarks>
    /// Deliberately additive and deduplicated by item id. Recording an item twice would double every
    /// count on the run row, which is a subtler lie than an omission but a lie all the same.
    /// </remarks>
    private static void Reconcile(
        IJobProgressReporter progress, ObservedConnector observed, ConnectorRunResult result)
    {
        var recorded = new HashSet<string>(observed.RecordedItemIds, StringComparer.Ordinal);

        foreach (var failure in result.Failures.Where(failure => recorded.Add(failure.ItemId)))
        {
            var status = failure.Reason.StartsWith(LibrarySkipPrefix, StringComparison.Ordinal)
                ? RunItemStatus.Skipped
                : RunItemStatus.Failed;
            progress.RecordItem(status, failure.ItemId, failure.ItemName, failure.Reason);
        }

        foreach (var item in result.Unchanged.Where(item => recorded.Add(item.Id)))
        {
            progress.RecordItem(
                RunItemStatus.Skipped,
                item.Id,
                item.Name,
                $"Unchanged since the previous run (version {item.Version ?? "unknown"}).");
        }
    }

    /// <summary>Persists sync state for the items a stopped run did manage to ingest.</summary>
    /// <param name="resolver">The connector seam.</param>
    /// <param name="payload">The run's payload.</param>
    /// <param name="resolved">The connector and the state the run started from.</param>
    /// <param name="observed">The decorator, which knows what was actually ingested.</param>
    /// <returns>A task that completes when the state is stored, or immediately when there is none to store.</returns>
    /// <remarks>
    /// Merged onto the previous state, never pruned against it. Only a complete walk can tell "deleted
    /// at the source" from "not reached yet", and a run that was stopped is by definition not a
    /// complete walk.
    /// </remarks>
    private static async Task KeepPartialSyncAsync(
        IConnectorResolver resolver,
        ConnectorJobPayload payload,
        ResolvedConnector resolved,
        ObservedConnector observed)
    {
        var ingested = observed.IngestedVersions;
        if (ingested.Count == 0)
        {
            return;
        }

        var sync = new ConnectorSyncState
        {
            LastRunUtc = resolved.PreviousSync?.LastRunUtc,
            ItemVersions = resolved.PreviousSync is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(resolved.PreviousSync.ItemVersions, StringComparer.Ordinal),
        };

        foreach (var pair in ingested)
        {
            sync.ItemVersions[pair.Key] = pair.Value;
        }

        // CancellationToken.None on purpose: this runs *because* the run was stopped, and passing the
        // cancelled token would abandon the very state the stop is supposed to preserve.
        await resolver.SaveSyncAsync(payload, sync, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Composes the run-history detail line.</summary>
    /// <param name="observed">The decorator, which counted what was ingested.</param>
    /// <param name="reachedLimit">Whether a run budget stopped the walk early.</param>
    /// <returns>A one-line summary for the run history.</returns>
    private static string ComposeDetail(ObservedConnector observed, bool reachedLimit)
    {
        var detail = $"{observed.IngestedCount} ingested of {observed.ListedCount} listed";
        return reachedLimit
            ? $"{detail} · stopped at this run's budget, so the source was not fully read"
            : detail;
    }
}
