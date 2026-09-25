using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Connectors;

/// <summary>
/// Drives an <see cref="IDataConnector"/> through a bounded, resumable, per-item-tolerant run
/// (REQ-RAG-032 / BRD-113, BRD-65).
/// </summary>
/// <remarks>
/// <para><b>This is where the framework earns its keep.</b> Paging, budgets, the incremental-sync
/// comparison, the per-item failure model and the circuit breaker are identical for a repository, a
/// wiki and a mailbox, and writing them once means the three connectors are each only the part that
/// is genuinely source-specific. It also means all five behaviours are tested once, against a fake
/// connector, with no network anywhere.</para>
/// <para><b>Two ways to receive documents.</b> The first overload collects every fetched document
/// into <see cref="ConnectorRunResult.Documents"/>, so the whole outcome is one value the caller
/// cannot miss, and <see cref="ConnectorRunOptions.MaxTotalBytes"/> bounds what that costs. The
/// overload taking a document handler hands each document over the moment it is fetched instead
/// (REQ-RAG-083 / BRD-127, TR-RAG-020/022): a run cancelled or failed part-way has already delivered
/// everything it fetched, and the documents are not held in memory. Failures, the unchanged list and
/// the sync state still come back on the one result value either way.</para>
/// </remarks>
public sealed class ConnectorRunner
{
    private readonly ILogger<ConnectorRunner> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="ConnectorRunner"/> class.</summary>
    /// <param name="logger">Diagnostics. Item names are logged; item contents and credentials are not.</param>
    /// <param name="timeProvider">Clock, so the inter-request delay and the sync timestamp are testable without real waiting.</param>
    public ConnectorRunner(ILogger<ConnectorRunner>? logger = null, TimeProvider? timeProvider = null)
    {
        this.logger = logger ?? NullLogger<ConnectorRunner>.Instance;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Runs a connector to completion or to its budget, whichever comes first.</summary>
    /// <param name="connector">The connector to drive.</param>
    /// <param name="previousSync">State returned by the previous run, or null for a first, full run.</param>
    /// <param name="options">Run bounds; defaults are conservative.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Documents fetched, items skipped, items failed, and the state for the next run.</returns>
    /// <exception cref="ConnectorException">The source could not be read at all, or too many items failed in a row.</exception>
    public Task<ConnectorRunResult> RunAsync(
        IDataConnector connector,
        ConnectorSyncState? previousSync = null,
        ConnectorRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connector);
        return RunCoreAsync(connector, previousSync, options ?? new ConnectorRunOptions(), null, cancellationToken);
    }

    /// <summary>
    /// Runs a connector and hands each fetched document to <paramref name="onDocument"/> as it
    /// arrives, instead of collecting them (REQ-RAG-083 / BRD-127).
    /// </summary>
    /// <param name="connector">The connector to drive.</param>
    /// <param name="previousSync">State returned by the previous run, or null for a first, full run.</param>
    /// <param name="options">Run bounds; defaults are conservative.</param>
    /// <param name="onDocument">
    /// Receives each document the moment it is fetched, before the next item is fetched. An item's
    /// version is recorded in the sync state only after this returns, so a document the handler
    /// failed on is fetched again next run. An exception from the handler ends the run.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Items skipped, items failed, and the state for the next run. <see cref="ConnectorRunResult.Documents"/>
    /// is empty: every document has already been handed to <paramref name="onDocument"/>.
    /// </returns>
    /// <exception cref="ConnectorException">The source could not be read at all, or too many items failed in a row.</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled; every document fetched before that has already been handed over.</exception>
    public Task<ConnectorRunResult> RunAsync(
        IDataConnector connector,
        ConnectorSyncState? previousSync,
        ConnectorRunOptions? options,
        Func<ConnectorDocument, CancellationToken, Task> onDocument,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(onDocument);
        return RunCoreAsync(connector, previousSync, options ?? new ConnectorRunOptions(), onDocument, cancellationToken);
    }

    /// <summary>The one walk both overloads share.</summary>
    /// <param name="connector">The connector to drive.</param>
    /// <param name="previousSync">State from the previous run, or null.</param>
    /// <param name="options">Run bounds.</param>
    /// <param name="onDocument">Per-document handler, or null to collect documents into the result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run result.</returns>
    private async Task<ConnectorRunResult> RunCoreAsync(
        IDataConnector connector,
        ConnectorSyncState? previousSync,
        ConnectorRunOptions options,
        Func<ConnectorDocument, CancellationToken, Task>? onDocument,
        CancellationToken cancellationToken)
    {
        var documents = new List<ConnectorDocument>();
        var fetchedCount = 0;
        var unchanged = new List<ConnectorItem>();
        var failures = new List<ConnectorItemFailure>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        // Start from what the previous run knew and overwrite as items are seen. Starting empty
        // would be simpler but would make a truncated run re-fetch everything it had already done,
        // so a source larger than the budget could never converge. Stale entries are pruned below,
        // but only when the run actually reached the end of the source.
        var sync = new ConnectorSyncState
        {
            ItemVersions = previousSync is null
                ? []
                : new Dictionary<string, string>(previousSync.ItemVersions, StringComparer.Ordinal),
        };

        var cursor = (string?)null;
        var pages = 0;
        var consecutiveFailures = 0;
        var unchangedCount = 0;
        var totalBytes = 0L;
        var reachedLimit = false;
        var limitCode = (string?)null;
        var isFirstFetch = true;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (pages >= options.MaxPages)
            {
                logger.LogWarning(
                    "{Source} run stopped at the {Pages}-page listing limit", connector.SourceName, options.MaxPages);
                reachedLimit = true;
                limitCode = ConnectorErrorCodes.RunPageLimitReached;
                break;
            }

            var page = await connector
                .ListAsync(new ConnectorListRequest(cursor, previousSync), cancellationToken)
                .ConfigureAwait(false);

            pages++;

            if (page.Failures is { Count: > 0 })
            {
                failures.AddRange(page.Failures);
            }

            foreach (var item in page.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (fetchedCount >= options.MaxItems)
                {
                    reachedLimit = true;
                    limitCode = ConnectorErrorCodes.RunItemLimitReached;
                    break;
                }

                if (!seenIds.Add(item.Id))
                {
                    continue;
                }

                if (previousSync is not null && previousSync.IsUnchanged(item))
                {
                    sync.ItemVersions[item.Id] = item.Version!;
                    unchangedCount++;
                    if (options.ReportUnchanged)
                    {
                        unchanged.Add(item);
                    }

                    continue;
                }

                // Size is checked from the LISTING, so an oversized item costs nothing to reject.
                // Checking after the fetch would mean downloading the 400 MB file to discover it is
                // 400 MB, which is the cost the cap exists to avoid.
                if (item.SizeBytes is { } size && size > options.MaxItemBytes)
                {
                    failures.Add(new ConnectorItemFailure(
                        item.Id,
                        item.Name,
                        $"Skipped: {size:N0} bytes exceeds the {options.MaxItemBytes:N0}-byte limit for one item."));
                    continue;
                }

                if (!isFirstFetch && options.RequestDelay > TimeSpan.Zero)
                {
                    await Task.Delay(options.RequestDelay, timeProvider, cancellationToken).ConfigureAwait(false);
                }

                isFirstFetch = false;

                ConnectorDocument document;
                try
                {
                    document = await connector.FetchAsync(item, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ConnectorException)
                {
                    // The connector is saying the run itself is over — revoked token, dead host.
                    // Recording it as one item's problem would hide that from the caller.
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{Source} skipped {Item}", connector.SourceName, item.Name);
                    failures.Add(new ConnectorItemFailure(item.Id, item.Name, ex.Message));
                    consecutiveFailures++;

                    if (options.MaxConsecutiveFailures > 0 && consecutiveFailures >= options.MaxConsecutiveFailures)
                    {
                        throw new ConnectorException(
                            connector.SourceType,
                            $"{connector.SourceName}: {consecutiveFailures} items failed in a row, most recently '{item.Name}' ({ex.Message}). The source or the credential is broken, not the items.",
                            ex);
                    }

                    continue;
                }

                consecutiveFailures = 0;
                fetchedCount++;

                // Handed over before anything else is fetched, so a run stopped after this point has
                // already delivered this document (REQ-RAG-083). Without a handler it is collected.
                if (onDocument is null)
                {
                    documents.Add(document);
                }
                else
                {
                    await onDocument(document, cancellationToken).ConfigureAwait(false);
                }

                // Measured in real UTF-8 bytes rather than characters, so the number the option
                // promises is the number enforced for text that is not ASCII.
                totalBytes += System.Text.Encoding.UTF8.GetByteCount(document.Text);

                // Recorded only on success. An item whose version is stored after a failure is an
                // item that silently never gets ingested, because every later run calls it unchanged.
                if (item.Version is not null)
                {
                    sync.ItemVersions[item.Id] = item.Version;
                }

                // Checked after the item is kept, not before: the document just fetched is already
                // paid for, and discarding it would re-fetch it on the next run forever.
                if (totalBytes >= options.MaxTotalBytes)
                {
                    logger.LogWarning(
                        "{Source} run stopped at the {Bytes:N0}-byte budget",
                        connector.SourceName,
                        options.MaxTotalBytes);
                    reachedLimit = true;
                    limitCode = ConnectorErrorCodes.RunByteBudgetReached;
                    break;
                }
            }

            if (reachedLimit || page.NextCursor is null)
            {
                break;
            }

            cursor = page.NextCursor;
        }

        // Only a complete walk of the whole source can tell deletion from "not reached yet" — and a
        // connector that lists changes only never performs one. See IDataConnector.ListsEntireSource.
        if (!reachedLimit && connector.ListsEntireSource)
        {
            foreach (var stale in sync.ItemVersions.Keys.Where(id => !seenIds.Contains(id)).ToList())
            {
                sync.ItemVersions.Remove(stale);
            }
        }

        sync.LastRunUtc = timeProvider.GetUtcNow();

        logger.LogInformation(
            "{Source} run fetched {Fetched}, skipped {Unchanged} unchanged, {Failed} failed",
            connector.SourceName,
            fetchedCount,
            unchangedCount,
            failures.Count);

        return new ConnectorRunResult(documents, unchanged, failures, sync, reachedLimit)
        {
            LimitCode = limitCode,
        };
    }
}
