using TechieRag.Connectors;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-032 / BRD-113: the behaviour every connector inherits — paging, budgets, incremental
/// sync, and a failure model that costs one item rather than the run.
/// </summary>
public sealed class ConnectorRunnerTests
{
    /// <summary>Every listing page is walked, not just the first.</summary>
    [Fact]
    public async Task WalksEveryPage()
    {
        var connector = new FakeDataConnector()
            .Page(Item("a"), Item("b"))
            .Page(Item("c"));

        var result = await RunAsync(connector);

        Assert.Equal(3, result.Documents.Count);
        Assert.Equal(["a", "b", "c"], connector.Fetched);
    }

    /// <summary>The item budget is a hard cap, and the run says it stopped short.</summary>
    [Fact]
    public async Task StopsAtMaxItems()
    {
        var connector = new FakeDataConnector().Page(Item("a"), Item("b"), Item("c"));

        var result = await RunAsync(connector, options: new ConnectorRunOptions { MaxItems = 2 });

        Assert.Equal(2, result.Documents.Count);
        Assert.True(result.ReachedLimit);
    }

    /// <summary>
    /// The page budget stops a source whose cursor never terminates. Without it a paging bug loops
    /// forever having fetched nothing, so the item budget is never reached.
    /// </summary>
    [Fact]
    public async Task StopsAtMaxPages()
    {
        var connector = new FakeDataConnector().Page(Item("a")).Page(Item("b")).Page(Item("c"));

        var result = await RunAsync(connector, options: new ConnectorRunOptions { MaxPages = 2 });

        Assert.Equal(2, result.Documents.Count);
        Assert.True(result.ReachedLimit);
    }

    /// <summary>One item that will not fetch is recorded and the run carries on.</summary>
    [Fact]
    public async Task RecordsItemFailureAndContinues()
    {
        var connector = new FakeDataConnector()
            .Page(Item("a"), Item("bad"), Item("c"))
            .WithFailure("bad", new InvalidOperationException("corrupt blob"));

        var result = await RunAsync(connector);

        Assert.Equal(2, result.Documents.Count);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("bad", failure.ItemId);
        Assert.Contains("corrupt blob", failure.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A long unbroken streak of failures is a broken source, not a partial result, and ends the run
    /// rather than producing thousands of individual failures.
    /// </summary>
    [Fact]
    public async Task AbortsAfterConsecutiveFailures()
    {
        var connector = new FakeDataConnector();
        var items = Enumerable.Range(0, 10).Select(i => Item($"x{i}")).ToArray();
        connector.Page(items);
        foreach (var item in items)
        {
            connector.WithFailure(item.Id, new InvalidOperationException("gone"));
        }

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => RunAsync(connector, options: new ConnectorRunOptions { MaxConsecutiveFailures = 3 }));

        Assert.Contains("in a row", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, connector.Fetched.Count);
    }

    /// <summary>
    /// The streak resets on success, so a source that fails one item in three still runs to the end.
    /// </summary>
    [Fact]
    public async Task ResetsFailureStreakOnSuccess()
    {
        var connector = new FakeDataConnector();
        var items = Enumerable.Range(0, 12).Select(i => Item($"x{i}")).ToArray();
        connector.Page(items);
        foreach (var item in items.Where((_, i) => i % 3 == 0))
        {
            connector.WithFailure(item.Id, new InvalidOperationException("gone"));
        }

        var result = await RunAsync(connector, options: new ConnectorRunOptions { MaxConsecutiveFailures = 3 });

        Assert.Equal(8, result.Documents.Count);
        Assert.Equal(4, result.Failures.Count);
    }

    /// <summary>An item whose version matches the previous run is never fetched.</summary>
    [Fact]
    public async Task SkipsUnchangedItems()
    {
        var connector = new FakeDataConnector().Page(Item("a", "v1"), Item("b", "v1"));
        var previous = Sync(("a", "v1"), ("b", "v1"));

        var result = await RunAsync(connector, previous);

        Assert.Empty(result.Documents);
        Assert.Empty(connector.Fetched);
    }

    /// <summary>A changed version is fetched again.</summary>
    [Fact]
    public async Task RefetchesWhenVersionChanges()
    {
        var connector = new FakeDataConnector().Page(Item("a", "v2"), Item("b", "v1"));
        var previous = Sync(("a", "v1"), ("b", "v1"));

        var result = await RunAsync(connector, previous);

        Assert.Equal(["a"], connector.Fetched);
        Assert.Single(result.Documents);
    }

    /// <summary>
    /// An item the source cannot version is always fetched. Treating "I cannot tell" as "unchanged"
    /// would freeze it at whatever the first run happened to see.
    /// </summary>
    [Fact]
    public async Task RefetchesWhenVersionIsUnknown()
    {
        var connector = new FakeDataConnector().Page(Item("a", version: null));
        var previous = Sync(("a", "v1"));

        await RunAsync(connector, previous);

        Assert.Equal(["a"], connector.Fetched);
    }

    /// <summary>An item deleted at the source stops being tracked after a full walk.</summary>
    [Fact]
    public async Task PrunesDeletedItemsFromSyncState()
    {
        var connector = new FakeDataConnector().Page(Item("a", "v1"));
        var previous = Sync(("a", "v1"), ("deleted", "v9"));

        var result = await RunAsync(connector, previous);

        Assert.True(result.Sync.ItemVersions.ContainsKey("a"));
        Assert.False(result.Sync.ItemVersions.ContainsKey("deleted"));
    }

    /// <summary>
    /// A connector that lists changes only never sees the whole source, so pruning against its
    /// listing would discard the state that made the run incremental in the first place.
    /// </summary>
    [Fact]
    public async Task KeepsSyncStateWhenConnectorListsChangesOnly()
    {
        var connector = new FakeDataConnector { ListsEntireSource = false };
        connector.Page(Item("new", "v1"));
        var previous = Sync(("old", "v1"));

        var result = await RunAsync(connector, previous);

        Assert.True(result.Sync.ItemVersions.ContainsKey("old"));
    }

    /// <summary>
    /// A run stopped by its budget carries forward what it already knew, so a source larger than one
    /// run's budget converges instead of restarting from nothing every time.
    /// </summary>
    [Fact]
    public async Task KeepsSyncStateWhenRunWasTruncated()
    {
        var connector = new FakeDataConnector().Page(Item("a", "v1"), Item("b", "v1"));
        var previous = Sync(("elsewhere", "v1"));

        var result = await RunAsync(connector, previous, new ConnectorRunOptions { MaxItems = 1 });

        Assert.True(result.ReachedLimit);
        Assert.True(result.Sync.ItemVersions.ContainsKey("elsewhere"));
    }

    /// <summary>An item the listing reports as oversized is refused without being downloaded.</summary>
    [Fact]
    public async Task SkipsOversizedItemsWithoutFetching()
    {
        var connector = new FakeDataConnector()
            .Page(new ConnectorItem("huge", "huge.bin", "", "v1", null, 50_000_000), Item("small"));

        var result = await RunAsync(connector, options: new ConnectorRunOptions { MaxItemBytes = 1000 });

        Assert.Equal(["small"], connector.Fetched);
        Assert.Contains(result.Failures, f => f.ItemId == "huge" && f.Reason.Contains("exceeds", StringComparison.Ordinal));
    }

    /// <summary>
    /// A failed item's version is not recorded. Recording it would call the item unchanged on every
    /// later run, so a single transient failure would mean it is never ingested at all.
    /// </summary>
    [Fact]
    public async Task DoesNotRecordVersionForFailedItem()
    {
        var connector = new FakeDataConnector()
            .Page(Item("bad", "v1"))
            .WithFailure("bad", new InvalidOperationException("transient"));

        var result = await RunAsync(connector);

        Assert.False(result.Sync.ItemVersions.ContainsKey("bad"));
    }

    /// <summary>A connector saying the whole run is over is not demoted to one item's failure.</summary>
    [Fact]
    public async Task RunLevelFailureFromFetchAbortsTheRun()
    {
        var connector = new FakeDataConnector()
            .Page(Item("a"), Item("b"))
            .WithFailure("a", new ConnectorException("fake", "the token was revoked"));

        var error = await Assert.ThrowsAsync<ConnectorException>(() => RunAsync(connector));

        Assert.Contains("revoked", error.Message, StringComparison.Ordinal);
        Assert.Single(connector.Fetched);
    }

    /// <summary>Failures raised while listing reach the caller alongside the items.</summary>
    [Fact]
    public async Task ReportsListingFailures()
    {
        var connector = new FakeDataConnector().Page(Item("a")).WithListFailure("the tree was truncated");

        var result = await RunAsync(connector);

        Assert.Single(result.Documents);
        Assert.Contains(result.Failures, f => f.Reason.Contains("truncated", StringComparison.Ordinal));
    }

    /// <summary>Unchanged items are listed only when asked for, because normally they are nearly everything.</summary>
    [Fact]
    public async Task ReportsUnchangedOnlyWhenAsked()
    {
        var connector = new FakeDataConnector().Page(Item("a", "v1"));
        var previous = Sync(("a", "v1"));

        var quiet = await RunAsync(connector, previous);
        var loud = await RunAsync(
            new FakeDataConnector().Page(Item("a", "v1")),
            previous,
            new ConnectorRunOptions { ReportUnchanged = true });

        Assert.Empty(quiet.Unchanged);
        Assert.Single(loud.Unchanged);
    }

    /// <summary>The same id appearing in two pages is fetched once.</summary>
    [Fact]
    public async Task FetchesEachItemOnce()
    {
        var connector = new FakeDataConnector().Page(Item("a")).Page(Item("a"), Item("b"));

        await RunAsync(connector);

        Assert.Equal(["a", "b"], connector.Fetched);
    }

    /// <summary>The previous run's state is handed to the connector so it can filter at the source.</summary>
    [Fact]
    public async Task PassesPreviousSyncToTheConnector()
    {
        var connector = new FakeDataConnector().Page(Item("a", "v2"));
        var previous = Sync(("a", "v1"));

        await RunAsync(connector, previous);

        Assert.Same(previous, connector.ListRequests[0].PreviousSync);
    }

    /// <summary>The byte budget stops a run whose items are each small and enormous in total.</summary>
    /// <remarks>
    /// The cap that actually bounds memory. Every item here is far below
    /// <see cref="ConnectorRunOptions.MaxItemBytes"/> and well within
    /// <see cref="ConnectorRunOptions.MaxItems"/>, which is exactly the shape a docs repository has:
    /// thousands of individually reasonable files that no per-item limit can add up.
    /// </remarks>
    [Fact]
    public async Task StopsAtMaxTotalBytes()
    {
        var connector = new FakeDataConnector()
            .Page(Item("a"), Item("b"), Item("c"))
            .WithText("a", new string('x', 400))
            .WithText("b", new string('x', 400))
            .WithText("c", new string('x', 400));

        var result = await RunAsync(connector, options: new ConnectorRunOptions
        {
            MaxTotalBytes = 700,
            MaxItems = 1000,
            MaxItemBytes = 1024 * 1024,
        });

        Assert.Equal(2, result.Documents.Count);
        Assert.True(result.ReachedLimit);
        Assert.Equal(["a", "b"], connector.Fetched);
    }

    /// <summary>The item that tipped the budget is kept, not discarded.</summary>
    /// <remarks>
    /// It has already been paid for. Dropping it would leave its version unrecorded and re-fetch it
    /// on every subsequent run, so a source larger than the budget could never converge.
    /// </remarks>
    [Fact]
    public async Task KeepsTheItemThatReachedTheByteBudget()
    {
        var connector = new FakeDataConnector()
            .Page(Item("a"), Item("b"))
            .WithText("a", new string('x', 900));

        var result = await RunAsync(connector, options: new ConnectorRunOptions { MaxTotalBytes = 100 });

        Assert.Single(result.Documents);
        Assert.Equal("a", result.Documents[0].Item.Id);
        Assert.Equal("v1", result.Sync.ItemVersions["a"]);
    }

    /// <summary>The budget is counted in real UTF-8 bytes, not characters.</summary>
    /// <remarks>
    /// A cap named in bytes that counts characters under-reports by a factor of three on the
    /// non-Latin text a wiki is full of, which is precisely when the bound is needed.
    /// </remarks>
    [Fact]
    public async Task CountsMultiByteTextByItsBytes()
    {
        // Each character is three bytes in UTF-8, so 100 characters is 300 bytes and one item alone
        // exceeds a 250-byte budget.
        var connector = new FakeDataConnector()
            .Page(Item("a"), Item("b"))
            .WithText("a", new string('中', 100));

        var result = await RunAsync(connector, options: new ConnectorRunOptions { MaxTotalBytes = 250 });

        Assert.Single(result.Documents);
        Assert.True(result.ReachedLimit);
    }

    /// <summary>A run under the budget is not marked as truncated.</summary>
    /// <remarks>
    /// <see cref="ConnectorRunResult.ReachedLimit"/> suppresses sync-state pruning, so setting it
    /// spuriously would keep deleted items tracked forever.
    /// </remarks>
    [Fact]
    public async Task DoesNotReportALimitWhenTheBudgetWasNotReached()
    {
        var connector = new FakeDataConnector().Page(Item("a"), Item("b"));

        var result = await RunAsync(connector, options: new ConnectorRunOptions { MaxTotalBytes = 1024 * 1024 });

        Assert.Equal(2, result.Documents.Count);
        Assert.False(result.ReachedLimit);
    }

    /// <summary>Cancelling a run stops it, and stops it as a cancellation.</summary>
    /// <remarks>
    /// A background job that is told to stop must not report the source as broken. The runner
    /// distinguishes cancellation by exception TYPE, so this also pins that a connector's own
    /// cancellation is not swallowed into the per-item failure list.
    /// </remarks>
    [Fact]
    public async Task CancellationStopsTheRunAsACancellation()
    {
        var connector = new FakeDataConnector().Page(Item("a"), Item("b"), Item("c"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ConnectorRunner().RunAsync(
                connector,
                previousSync: null,
                new ConnectorRunOptions { RequestDelay = TimeSpan.Zero },
                cancellation.Token));

        Assert.Empty(connector.Fetched);
    }

    /// <summary>
    /// A connector that throws <see cref="OperationCanceledException"/> mid-fetch ends the run
    /// rather than being recorded as one item's problem.
    /// </summary>
    [Fact]
    public async Task CancellationDuringAFetchIsNotRecordedAsAnItemFailure()
    {
        var connector = new FakeDataConnector()
            .Page(Item("a"), Item("b"))
            .WithFailure("a", new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(connector));

        Assert.Equal(["a"], connector.Fetched);
    }

    private static Task<ConnectorRunResult> RunAsync(
        FakeDataConnector connector,
        ConnectorSyncState? previous = null,
        ConnectorRunOptions? options = null)
    {
        options ??= new ConnectorRunOptions();

        // The politeness delay is real time; these tests set it to zero so they stay instant.
        options.RequestDelay = TimeSpan.Zero;
        return new ConnectorRunner().RunAsync(connector, previous, options);
    }

    private static ConnectorItem Item(string id, string? version = "v1") =>
        new(id, id, $"https://example.test/{id}", version);

    private static ConnectorSyncState Sync(params (string Id, string Version)[] versions) =>
        new()
        {
            LastRunUtc = DateTimeOffset.UnixEpoch,
            ItemVersions = versions.ToDictionary(v => v.Id, v => v.Version, StringComparer.Ordinal),
        };
}
