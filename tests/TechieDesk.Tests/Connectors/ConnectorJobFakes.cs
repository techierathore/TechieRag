using System.Globalization;
using TechieDesk.Services.Connectors;
using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;

namespace TechieDesk.Tests.Connectors;

/// <summary>
/// A connector whose listing, contents and failures the test writes by hand.
/// </summary>
/// <remarks>
/// The seam the connector framework documents for exactly this purpose: paging, budgets, the
/// per-item failure model and the circuit breaker are all exercised with no network anywhere. Every
/// hook here is synchronous and deterministic — there is no timing in this file, so no test that uses
/// it can race.
/// </remarks>
public sealed class FakeConnector : IDataConnector
{
    private readonly List<ConnectorItem> items = [];
    private readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Exception> fetchFailures = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string SourceType { get; set; } = "fake";

    /// <inheritdoc />
    public string SourceName { get; set; } = "fake/source";

    /// <inheritdoc />
    public bool ListsEntireSource { get; set; } = true;

    /// <summary>Gets or sets how many items one listing page returns.</summary>
    public int PageSize { get; set; } = int.MaxValue;

    /// <summary>Gets the failures reported on the first listing page.</summary>
    public List<ConnectorItemFailure> ListingFailures { get; } = [];

    /// <summary>Gets or sets a hook run immediately before each fetch, in fetch order.</summary>
    /// <remarks>
    /// How a test reaches "mid-run" without sleeping: the hook fires at a known item, so cancellation
    /// and live-progress assertions happen at an exact point in the walk rather than after a delay
    /// somebody hoped was long enough.
    /// </remarks>
    public Func<ConnectorItem, Task>? BeforeFetch { get; set; }

    /// <summary>Gets the ids of every item actually fetched, in order.</summary>
    public List<string> FetchedIds { get; } = [];

    /// <summary>Adds an item that fetches successfully.</summary>
    /// <param name="id">The item id.</param>
    /// <param name="name">The item name.</param>
    /// <param name="text">The text the fetch returns.</param>
    /// <param name="version">The content version, or <see langword="null"/>.</param>
    /// <param name="sizeBytes">The size the listing reports, or <see langword="null"/>.</param>
    /// <returns>The same connector, for chaining.</returns>
    public FakeConnector Add(
        string id, string name, string text, string? version = null, long? sizeBytes = null)
    {
        items.Add(new ConnectorItem(id, name, $"https://fake.test/{id}", version, null, sizeBytes));
        texts[id] = text;
        return this;
    }

    /// <summary>Adds an item whose fetch throws.</summary>
    /// <param name="id">The item id.</param>
    /// <param name="name">The item name.</param>
    /// <param name="exception">What the fetch throws.</param>
    /// <param name="version">The content version, or <see langword="null"/>.</param>
    /// <returns>The same connector, for chaining.</returns>
    public FakeConnector AddFailing(string id, string name, Exception exception, string? version = null)
    {
        items.Add(new ConnectorItem(id, name, $"https://fake.test/{id}", version));
        fetchFailures[id] = exception;
        return this;
    }

    /// <inheritdoc />
    public Task<ConnectorPage> ListAsync(
        ConnectorListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var offset = request.Cursor is null
            ? 0
            : int.Parse(request.Cursor, CultureInfo.InvariantCulture);

        var page = items.Skip(offset).Take(PageSize).ToList();
        var next = offset + page.Count;
        var hasMore = next < items.Count;

        return Task.FromResult(new ConnectorPage(
            page,
            hasMore ? next.ToString(CultureInfo.InvariantCulture) : null,
            offset == 0 ? ListingFailures : []));
    }

    /// <inheritdoc />
    public async Task<ConnectorDocument> FetchAsync(
        ConnectorItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (BeforeFetch is not null)
        {
            await BeforeFetch(item).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        FetchedIds.Add(item.Id);

        return fetchFailures.TryGetValue(item.Id, out var failure)
            ? throw failure
            : new ConnectorDocument(item, texts[item.Id]);
    }
}

/// <summary>
/// An <see cref="IConnectorResolver"/> that hands over a prepared connector and remembers every sync
/// state saved against it.
/// </summary>
public sealed class FakeConnectorResolver : IConnectorResolver
{
    /// <summary>Initializes the resolver.</summary>
    /// <param name="connector">The connector every payload resolves to.</param>
    public FakeConnectorResolver(IDataConnector connector) => Connector = connector;

    /// <summary>Gets or sets the connector every payload resolves to.</summary>
    public IDataConnector Connector { get; set; }

    /// <summary>Gets or sets what the previous run saw.</summary>
    public ConnectorSyncState? PreviousSync { get; set; }

    /// <summary>Gets or sets the validation error to report, or <see langword="null"/> to accept.</summary>
    public string? ValidationError { get; set; }

    /// <summary>Gets or sets an exception <see cref="ResolveAsync"/> throws instead of resolving.</summary>
    public Exception? ResolveFailure { get; set; }

    /// <summary>Gets every sync state handed back for persistence, in order.</summary>
    public List<ConnectorSyncState> SavedSync { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<ConnectorTypeDescriptor> AvailableTypes { get; set; } =
        [new ConnectorTypeDescriptor("fake", "ConnectorTypeRepositoryName", "ConnectorTypeRepositoryDescription")];

    /// <inheritdoc />
    public string? Validate(ConnectorJobPayload payload) => ValidationError;

    /// <inheritdoc />
    public Task<ResolvedConnector> ResolveAsync(
        ConnectorJobPayload payload, CancellationToken cancellationToken) =>
        ResolveFailure is not null
            ? Task.FromException<ResolvedConnector>(ResolveFailure)
            : Task.FromResult(new ResolvedConnector(Connector, PreviousSync));

    /// <inheritdoc />
    public Task SaveSyncAsync(
        ConnectorJobPayload payload, ConnectorSyncState sync, CancellationToken cancellationToken)
    {
        SavedSync.Add(sync);
        return Task.CompletedTask;
    }
}

/// <summary>
/// An <see cref="IConnectorDocumentSink"/> that records what it was given, in order.
/// </summary>
/// <remarks>
/// The list it accumulates is what makes "cancelling keeps what was already ingested" assertable:
/// after a cancelled run, everything in <see cref="Ingested"/> is what a real sink would have written
/// to the catalogue before the stop, and the test checks that the run's own item ledger agrees.
/// </remarks>
public sealed class RecordingDocumentSink : IConnectorDocumentSink
{
    /// <summary>Gets the documents accepted, in order.</summary>
    public List<ConnectorDocument> Ingested { get; } = [];

    /// <summary>Gets the exceptions this sink throws, keyed by item id.</summary>
    public Dictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<ConnectorIngestOutcome> IngestAsync(
        IDataConnector connector,
        ConnectorDocument document,
        ConnectorJobPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (Failures.TryGetValue(document.Item.Id, out var failure))
        {
            throw failure;
        }

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            return Task.FromResult(
                ConnectorIngestOutcome.Skipped("The item held no readable text."));
        }

        Ingested.Add(document);
        return Task.FromResult(ConnectorIngestOutcome.Ingested(
            $"doc-{document.Item.Id}", "Added to the document library."));
    }
}

/// <summary>
/// An in-memory <see cref="IScheduleRunRepository"/> that signals when a run is closed.
/// </summary>
/// <remarks>
/// The signal is what keeps the background-job tests free of polling. A run started through
/// <see cref="BackgroundJobService"/> finishes on a thread-pool thread; awaiting
/// <see cref="Completed"/> waits for the actual write rather than for a duration somebody guessed.
/// </remarks>
public sealed class SignallingRunRepository : IScheduleRunRepository
{
    private readonly TaskCompletionSource<ScheduleRun> completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private long nextId = 1;

    /// <summary>Gets the runs opened, in order.</summary>
    public List<ScheduleRun> Runs { get; } = [];

    /// <summary>Gets the per-item rows written, in order.</summary>
    public List<ScheduleRunItem> Items { get; } = [];

    /// <summary>Gets a task that completes when the first run is closed.</summary>
    public Task<ScheduleRun> Completed => completed.Task;

    /// <inheritdoc />
    public Task<long> StartAsync(ScheduleRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.ScheduleRunId = nextId++;
        Runs.Add(run);
        return Task.FromResult(run.ScheduleRunId);
    }

    /// <inheritdoc />
    public Task CompleteAsync(ScheduleRun run)
    {
        completed.TrySetResult(run);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AddItemsAsync(long scheduleRunId, IReadOnlyList<ScheduleRunItem> items)
    {
        Items.AddRange(items);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRun>> ListRecentAsync(int limit) =>
        Task.FromResult<IReadOnlyList<ScheduleRun>>(Runs.AsEnumerable().Reverse().Take(limit).ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRun>> ListForScheduleAsync(long scheduleId, int limit) =>
        Task.FromResult<IReadOnlyList<ScheduleRun>>(Runs
            .Where(run => run.ScheduleId == scheduleId)
            .Reverse()
            .Take(limit)
            .ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRunItem>> ListItemsAsync(long scheduleRunId) =>
        Task.FromResult<IReadOnlyList<ScheduleRunItem>>(Items
            .Where(item => item.ScheduleRunId == scheduleRunId)
            .ToList());

    /// <inheritdoc />
    public Task<int> CloseAbandonedRunsAsync(string reason, DateTime asOfUtc) => Task.FromResult(0);
}

/// <summary>
/// Wraps a real <see cref="IScheduleRunRepository"/> and signals when a run is closed.
/// </summary>
/// <remarks>
/// A background job finishes on a thread-pool thread, so a test that wants to read the finished run
/// has to wait for something. Waiting on the actual write is deterministic; waiting on a duration is
/// a race that passes on a fast machine and fails on a loaded one.
/// </remarks>
public sealed class SignallingRunRepositoryDecorator : IScheduleRunRepository
{
    private readonly IScheduleRunRepository inner;
    private readonly TaskCompletionSource<ScheduleRun> completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Initializes the decorator.</summary>
    /// <param name="inner">The repository that does the real work.</param>
    public SignallingRunRepositoryDecorator(IScheduleRunRepository inner) => this.inner = inner;

    /// <summary>Gets a task that completes when the first run is closed.</summary>
    public Task<ScheduleRun> Completed => completed.Task;

    /// <inheritdoc />
    public Task<long> StartAsync(ScheduleRun run) => inner.StartAsync(run);

    /// <inheritdoc />
    public async Task CompleteAsync(ScheduleRun run)
    {
        await inner.CompleteAsync(run).ConfigureAwait(false);
        completed.TrySetResult(run);
    }

    /// <inheritdoc />
    public Task AddItemsAsync(long scheduleRunId, IReadOnlyList<ScheduleRunItem> items) =>
        inner.AddItemsAsync(scheduleRunId, items);

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRun>> ListRecentAsync(int limit) => inner.ListRecentAsync(limit);

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRun>> ListForScheduleAsync(long scheduleId, int limit) =>
        inner.ListForScheduleAsync(scheduleId, limit);

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRunItem>> ListItemsAsync(long scheduleRunId) =>
        inner.ListItemsAsync(scheduleRunId);

    /// <inheritdoc />
    public Task<int> CloseAbandonedRunsAsync(string reason, DateTime asOfUtc) =>
        inner.CloseAbandonedRunsAsync(reason, asOfUtc);
}
