using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Connectors;
using TechieDesk.Services.Scheduling;
using TechieDesk.Tests.Support;
using TechieRag.Connectors;
using Xunit;

namespace TechieDesk.Tests.Connectors;

/// <summary>
/// REQ-FN-020 / BRD-65: a connector run is a background job with visible progress, a per-item result
/// for everything it touched, and an operator-facing reason for everything it did not ingest.
/// </summary>
/// <remarks>
/// <para>Everything under test is production code: the real <see cref="JobRunner"/>, the real
/// <see cref="ConnectorJobHandler"/>, the real <see cref="ObservedConnector"/> and the library's real
/// <see cref="ConnectorRunner"/>. Only the source (a scripted <see cref="FakeConnector"/>), the
/// connector seam and the document sink are doubles — none of them is behaviour under test.</para>
/// <para>There is no <c>Thread.Sleep</c> and no polling anywhere in this file. "Mid-run" is reached
/// through <see cref="FakeConnector.BeforeFetch"/>, which fires at a named item, so every assertion
/// about a run in flight happens at an exact point in the walk.</para>
/// </remarks>
public sealed class ConnectorJobHandlerTests
{
    /// <summary>
    /// Progress is observable WHILE the run is going, not only once it has finished — the difference
    /// between a screen that says "working" and one that cannot be told apart from a hang.
    /// </summary>
    [Fact]
    public async Task ProgressIsVisibleWhileTheRunIsStillGoing()
    {
        var harness = new Harness();
        for (var index = 1; index <= 5; index++)
        {
            harness.Connector.Add($"{index}", $"file-{index}.md", $"contents of file {index}");
        }

        var snapshotsAtItemFour = 0;
        var processedAtItemFour = 0;
        var ingestedAtItemFour = 0;
        harness.Connector.BeforeFetch = item =>
        {
            if (item.Id == "4")
            {
                snapshotsAtItemFour = harness.Snapshots.Count;
                processedAtItemFour = harness.Snapshots.Max(snapshot => snapshot.Processed);
                ingestedAtItemFour = harness.Sink.Ingested.Count;
            }

            return Task.CompletedTask;
        };

        var run = await harness.RunAsync();

        Assert.Equal(RunOutcome.Succeeded, run.Outcome);

        // Three items were already ingested and already reported by the time the fourth was reached.
        Assert.Equal(3, ingestedAtItemFour);
        Assert.Equal(3, processedAtItemFour);
        Assert.True(snapshotsAtItemFour > 3, $"only {snapshotsAtItemFour} progress reports had been raised mid-run");
    }

    /// <summary>
    /// The live progress carries a readable status line and a percentage derived from what has
    /// actually been listed — never a percentage invented before anything is known.
    /// </summary>
    [Fact]
    public async Task ProgressCarriesAStatusLineAndOnlyEverARealPercentage()
    {
        var harness = new Harness();
        harness.Connector.Add("1", "readme.md", "hello").Add("2", "guide.md", "world");

        await harness.RunAsync();

        Assert.All(harness.Snapshots, snapshot => Assert.False(string.IsNullOrWhiteSpace(snapshot.Message)));
        Assert.Null(harness.Snapshots[0].PercentComplete);
        Assert.Contains(harness.Snapshots, snapshot => snapshot.Message!.Contains("Listing items", StringComparison.Ordinal));
        Assert.Contains(harness.Snapshots, snapshot => snapshot.Message!.Contains("Ingested readme.md", StringComparison.Ordinal));
        Assert.Equal(100d, harness.Snapshots[^1].PercentComplete);
    }

    /// <summary>One bad item costs that item and nothing else — the run walks on to the end.</summary>
    [Fact]
    public async Task OneFailingItemDoesNotAbortTheRun()
    {
        var harness = new Harness();
        harness.Connector
            .Add("1", "a.md", "alpha")
            .Add("2", "b.md", "bravo")
            .AddFailing("3", "c.md", new InvalidOperationException("the source returned 403"))
            .Add("4", "d.md", "delta")
            .Add("5", "e.md", "echo");

        var run = await harness.RunAsync();
        var report = harness.Report(run);

        Assert.Equal(4, report.Ingested.Count);
        Assert.Equal(["1", "2", "4", "5"], report.Ingested.Select(item => item.ItemId));
        Assert.Equal("c.md", Assert.Single(report.Failed).ItemName);
    }

    /// <summary>
    /// A run that lost items is Partial, never Succeeded, and its summary says so — "47 ingested"
    /// while twelve items were dropped is the failure mode BRD-65 exists to prevent.
    /// </summary>
    [Fact]
    public async Task APartialRunIsNeverReportedAsASuccess()
    {
        var harness = new Harness();
        harness.Connector
            .Add("1", "a.md", "alpha")
            .AddFailing("2", "b.md", new IOException("the connection was reset"));

        var run = await harness.RunAsync();
        var report = harness.Report(run);

        Assert.Equal(RunOutcome.Partial, run.Outcome);
        Assert.True(report.IsPartial);
        Assert.Equal("Ingested 1 document; 1 item could not be read.", Summary(report));
    }

    /// <summary>
    /// Every per-item failure is named, with the reason the source gave, so a retry can act on it.
    /// </summary>
    /// <remarks>
    /// This is the behaviour-removal canary for BRD-65's per-item reason requirement: delete the
    /// reason from <see cref="ObservedConnector"/>'s failure path and this assertion goes red
    /// immediately, because the recorded reason becomes null.
    /// </remarks>
    [Fact]
    public async Task EveryFailureIsNamedWithTheReasonTheSourceGave()
    {
        var harness = new Harness();
        harness.Connector
            .Add("1", "a.md", "alpha")
            .AddFailing("2", "renewal.eml", new InvalidOperationException("attachment too large"));

        var run = await harness.RunAsync();
        var failure = Assert.Single(harness.Report(run).Failed);

        Assert.Equal("2", failure.ItemId);
        Assert.Equal("renewal.eml", failure.ItemName);
        Assert.Equal("attachment too large", failure.Reason);
    }

    /// <summary>
    /// A run where every single item fails ingests nothing, names all of them, and says plainly that
    /// nothing was ingested.
    /// </summary>
    [Fact]
    public async Task ARunWhereEveryItemFailsNamesThemAllAndClaimsNothing()
    {
        var harness = new Harness();
        for (var index = 1; index <= 4; index++)
        {
            harness.Connector.AddFailing(
                $"{index}", $"file-{index}.md", new HttpRequestException($"host refused item {index}"));
        }

        var run = await harness.RunAsync();
        var report = harness.Report(run);

        Assert.Empty(report.Ingested);
        Assert.Equal(4, report.Failed.Count);
        Assert.Equal(4, harness.Connector.FetchedIds.Count);
        Assert.All(report.Failed, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason)));
        Assert.Equal("Nothing was ingested — 4 items could not be read.", Summary(report));
    }

    /// <summary>
    /// Stopping a run mid-walk keeps every document already ingested, records them, and says so —
    /// it does not throw the finished work away and it does not report a success.
    /// </summary>
    [Fact]
    public async Task CancellingMidRunKeepsWhatWasAlreadyIngested()
    {
        var harness = new Harness();
        for (var index = 1; index <= 6; index++)
        {
            harness.Connector.Add($"{index}", $"file-{index}.md", $"contents {index}", $"v{index}");
        }

        using var cancellation = new CancellationTokenSource();
        harness.Connector.BeforeFetch = item =>
        {
            if (item.Id == "4")
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        };

        var run = await harness.RunAsync(cancellation.Token);
        var report = harness.Report(run);

        Assert.Equal(RunOutcome.Cancelled, run.Outcome);
        Assert.Equal(3, harness.Sink.Ingested.Count);
        Assert.Equal(3, report.Ingested.Count);
        Assert.Equal(["1", "2", "3"], report.Ingested.Select(item => item.ItemId));
        Assert.Empty(report.Failed);
        Assert.StartsWith(
            "Stopped by you — 3 documents had already been ingested and were kept",
            Summary(report),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A cancelled run keeps the sync state for what it ingested, so the next run does not
    /// re-download documents the user can already see.
    /// </summary>
    [Fact]
    public async Task CancellingKeepsTheSyncStateForWhatWasIngested()
    {
        var harness = new Harness();
        for (var index = 1; index <= 5; index++)
        {
            harness.Connector.Add($"{index}", $"file-{index}.md", $"contents {index}", $"v{index}");
        }

        using var cancellation = new CancellationTokenSource();
        harness.Connector.BeforeFetch = item =>
        {
            if (item.Id == "3")
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        };

        await harness.RunAsync(cancellation.Token);
        var saved = Assert.Single(harness.Resolver.SavedSync);

        Assert.Equal(["1", "2"], saved.ItemVersions.Keys.Order());
        Assert.Equal("v2", saved.ItemVersions["2"]);
    }

    /// <summary>
    /// Every item the run did not ingest carries a reason a person can act on — whether it was
    /// unreadable, unchanged, oversized or empty.
    /// </summary>
    /// <remarks>
    /// The single assertion BRD-65 reduces to. Any code path that stops recording a reason — the
    /// decorator's skip branch, the sink's empty-text branch, or the reconciliation of the library's
    /// own unchanged and oversize lists — reddens this test.
    /// </remarks>
    [Fact]
    public async Task EveryItemNotIngestedCarriesAnOperatorFacingReason()
    {
        var harness = new Harness();
        harness.Resolver.PreviousSync = new ConnectorSyncState
        {
            ItemVersions = { ["unchanged"] = "v1" },
        };

        harness.Connector
            .Add("ingested", "guide.md", "readable prose", "v9")
            .Add("unchanged", "stable.md", "same as last time", "v1")
            .Add("oversized", "dump.sql", "huge", "v2", 5_000_000)
            .Add("empty", "logo.png", "   ", "v3")
            .AddFailing("broken", "secret.md", new UnauthorizedAccessException("the token was rejected"));

        var run = await harness.RunAsync();
        var report = harness.Report(run);

        Assert.Equal(4, report.NotIngested.Count);
        Assert.All(report.NotIngested, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason)));

        Assert.Equal("the token was rejected", Find(report, "broken").Reason);
        Assert.Equal("Unchanged since the previous run (version v1).", Find(report, "unchanged").Reason);
        Assert.Equal("The item held no readable text.", Find(report, "empty").Reason);
        Assert.Contains("exceeds", Find(report, "oversized").Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A skip is counted as a skip and a failure as a failure, so a run that only passed over an
    /// oversized file is not smeared into "partial" alongside genuine breakage.
    /// </summary>
    [Fact]
    public async Task ADeliberateSkipIsNotCountedAsAFailure()
    {
        var harness = new Harness();
        harness.Connector
            .Add("1", "readme.md", "prose", "v1")
            .Add("2", "dump.sql", "huge", "v2", 9_000_000);

        var run = await harness.RunAsync();
        var report = harness.Report(run);

        Assert.Equal(RunOutcome.Succeeded, run.Outcome);
        Assert.Empty(report.Failed);
        Assert.Equal("dump.sql", Assert.Single(report.Skipped).ItemName);
        Assert.Equal("Ingested 1 document; 1 item skipped.", Summary(report));
    }

    /// <summary>
    /// A listing that comes back partial reports the items it could not list, at the moment it
    /// happens, rather than discarding them.
    /// </summary>
    [Fact]
    public async Task ListingFailuresAreRecordedToo()
    {
        var harness = new Harness();
        harness.Connector.Add("1", "a.md", "alpha");
        harness.Connector.ListingFailures.Add(
            new ConnectorItemFailure("tree", "src/", "The repository tree was truncated by the host."));

        var run = await harness.RunAsync();
        var report = harness.Report(run);

        Assert.Single(report.Ingested);
        Assert.Equal("The repository tree was truncated by the host.", Assert.Single(report.Failed).Reason);
    }

    /// <summary>
    /// Items are listed across several pages and every page is walked — the run does not stop at the
    /// first cursor.
    /// </summary>
    [Fact]
    public async Task EveryListingPageIsWalked()
    {
        var harness = new Harness();
        harness.Connector.PageSize = 2;
        for (var index = 1; index <= 7; index++)
        {
            harness.Connector.Add($"{index}", $"file-{index}.md", $"contents {index}");
        }

        var run = await harness.RunAsync();

        Assert.Equal(RunOutcome.Succeeded, run.Outcome);
        Assert.Equal(7, harness.Report(run).Ingested.Count);
    }

    /// <summary>
    /// A run whose budget cut the walk short says the source was not fully read, rather than
    /// implying it was.
    /// </summary>
    [Fact]
    public async Task ARunStoppedByItsBudgetSaysTheSourceWasNotFullyRead()
    {
        var harness = new Harness();
        harness.Payload = harness.Payload with { MaxItems = 2 };
        for (var index = 1; index <= 5; index++)
        {
            harness.Connector.Add($"{index}", $"file-{index}.md", $"contents {index}");
        }

        var run = await harness.RunAsync();

        Assert.Equal(2, harness.Sink.Ingested.Count);
        Assert.Contains("not fully read", run.Detail!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A run whose configuration cannot be read fails with a named reason, and does not escape as an
    /// exception onto a background timer.
    /// </summary>
    [Fact]
    public async Task AnUnreadableConfigurationIsANamedRunFailure()
    {
        var harness = new Harness();

        var run = await harness.Runner.RunOnceAsync(
            "Broken connector", ConnectorJobHandler.Kind, "{ not json", harness.Snapshots.Add, default);

        Assert.Equal(RunOutcome.Failed, run.Outcome);
        Assert.Equal("This connector run has no readable configuration.", run.FailureReason);
    }

    /// <summary>
    /// A connector that cannot be opened at all — deleted, or its credential gone — is a run failure
    /// naming the source problem, not a run that quietly reports zero items.
    /// </summary>
    [Fact]
    public async Task AConnectorThatCannotBeOpenedIsAFailedRun()
    {
        var harness = new Harness();
        harness.Resolver.ResolveFailure =
            new ConnectorException("fake", "fake/source: the stored credential was rejected (401).");

        var run = await harness.RunAsync();

        Assert.Equal(RunOutcome.Failed, run.Outcome);
        Assert.Equal("fake/source: the stored credential was rejected (401).", run.FailureReason);
        Assert.Empty(harness.Report(run).Ingested);
    }

    /// <summary>
    /// A source that dies part-way through keeps everything it had already ingested, records it, and
    /// still reports the run as failed with the source-level reason.
    /// </summary>
    [Fact]
    public async Task ASourceThatDiesPartWayKeepsWhatItIngested()
    {
        var harness = new Harness();
        harness.Connector
            .Add("1", "a.md", "alpha", "v1")
            .Add("2", "b.md", "bravo", "v2")
            .AddFailing("3", "c.md", new ConnectorException("fake", "the rate limit is exhausted"));

        var run = await harness.RunAsync();
        var report = harness.Report(run);

        Assert.Equal(RunOutcome.Failed, run.Outcome);
        Assert.Equal("the rate limit is exhausted", run.FailureReason);
        Assert.Equal(2, report.Ingested.Count);
        Assert.Equal(2, Assert.Single(harness.Resolver.SavedSync).ItemVersions.Count);
        Assert.StartsWith("2 documents ingested before the run stopped", Summary(report), StringComparison.Ordinal);
    }

    /// <summary>A clean run hands the next run the state that makes it cheap.</summary>
    [Fact]
    public async Task ACleanRunSavesTheSyncStateForNextTime()
    {
        var harness = new Harness();
        harness.Connector.Add("1", "a.md", "alpha", "sha-a").Add("2", "b.md", "bravo", "sha-b");

        await harness.RunAsync();
        var saved = Assert.Single(harness.Resolver.SavedSync);

        Assert.Equal("sha-a", saved.ItemVersions["1"]);
        Assert.Equal("sha-b", saved.ItemVersions["2"]);
    }

    /// <summary>The handler describes its action in plain language, never as JSON.</summary>
    [Fact]
    public void TheActionIsDescribedInPlainLanguage()
    {
        var harness = new Harness();
        var payload = harness.Payload with { WorkspaceId = "ws-7" };

        var described = harness.Handler.DescribeAction(payload.ToJson());

        Assert.Equal("Sync 'Fake repo' into workspace ws-7", described);
        Assert.DoesNotContain('{', described);
    }

    /// <summary>A payload that names no connector is refused at save time, not at 07:00 three days later.</summary>
    [Fact]
    public void AnEmptyPayloadIsRefusedWhenTheScheduleIsSaved()
    {
        var harness = new Harness();

        Assert.Equal(
            "The run does not say which connector to read.",
            harness.Handler.ValidatePayload(new ConnectorJobPayload().ToJson()));
    }

    /// <summary>Renders a run's summary in English, through the real localizer.</summary>
    /// <param name="report">The report to summarize.</param>
    /// <returns>The summary line the connector hub would show.</returns>
    /// <remarks>
    /// REQ-UI-055 / BRD-91: <c>SummaryText</c> now composes the line out of resource KEYS, so these
    /// assertions go through <see cref="ResourceHarness"/> rather than reading an English literal off
    /// the service. The expected strings are unchanged, which is the point — the English wording is
    /// still exactly what shipped, it just no longer lives in the service layer.
    /// </remarks>
    private static string Summary(ConnectorRunReport report)
    {
        using var resources = new ResourceHarness("en");
        return report.SummaryText(resources.Localize);
    }

    /// <summary>Finds one recorded item by id.</summary>
    /// <param name="report">The report to search.</param>
    /// <param name="itemId">The item id.</param>
    /// <returns>The recorded item.</returns>
    private static ConnectorRunItem Find(ConnectorRunReport report, string itemId) =>
        report.Items.Single(item => item.ItemId == itemId);

    /// <summary>
    /// The production job runner, the production connector handler and the library's own connector
    /// runner, wired together over a scripted source.
    /// </summary>
    private sealed class Harness
    {
        public Harness()
        {
            Resolver = new FakeConnectorResolver(Connector);

            var services = new ServiceCollection();
            services.AddSingleton<IConnectorResolver>(Resolver);
            services.AddSingleton<IConnectorDocumentSink>(Sink);
            var provider = services.BuildServiceProvider();

            Handler = new ConnectorJobHandler(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ConnectorRunner>.Instance,
                TimeProvider.System,
                NullLogger<ConnectorJobHandler>.Instance);

            Runner = new JobRunner(
                Runs, [Handler], TimeProvider.System, NullLogger<JobRunner>.Instance);
        }

        public FakeConnector Connector { get; } = new();

        public FakeConnectorResolver Resolver { get; }

        public RecordingDocumentSink Sink { get; } = new();

        public SignallingRunRepository Runs { get; } = new();

        public List<JobProgressSnapshot> Snapshots { get; } = [];

        public IJobRunner Runner { get; }

        public ConnectorJobHandler Handler { get; }

        /// <summary>
        /// Gets or sets what the run is asked to do. The politeness delay is zero so the walk is
        /// bounded by the test's own scripting rather than by a clock.
        /// </summary>
        public ConnectorJobPayload Payload { get; set; } = new()
        {
            ConnectorId = "fake-1",
            ConnectorType = "fake",
            DisplayName = "Fake repo",
            RequestDelayMs = 0,
        };

        public Task<ScheduleRun> RunAsync(CancellationToken cancellationToken = default) =>
            Runner.RunOnceAsync(
                Payload.DisplayName,
                ConnectorJobHandler.Kind,
                Payload.ToJson(),
                Snapshots.Add,
                cancellationToken);

        public ConnectorRunReport Report(ScheduleRun run) => ConnectorRunReport.From(run, Runs.Items);
    }
}
