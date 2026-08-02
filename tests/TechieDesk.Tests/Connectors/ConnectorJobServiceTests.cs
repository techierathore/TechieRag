using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Connectors;
using TechieDesk.Services.Data;
using TechieDesk.Services.Scheduling;
using TechieDesk.Tests.Support;
using TechieDesk.Tests.Workspaces;
using TechieDeskDb;
using TechieRag;
using TechieRag.Connectors;
using Xunit;

namespace TechieDesk.Tests.Connectors;

/// <summary>
/// REQ-FN-020 / BRD-65 end to end: a connector run started from the connector-hub API executes as a
/// real background job, is observable while it runs, is cancellable, and reads back as a report that
/// names every item and every reason.
/// </summary>
/// <remarks>
/// <para>Nothing in the path is stubbed except the source itself and the connector seam. The job goes
/// through the production <see cref="BackgroundJobService"/> on a real thread-pool thread, through the
/// production <see cref="JobRunner"/>, into a real SQLite application database migrated by the real
/// DbUp script, and each document is ingested into a real <see cref="ITechieRag"/> instance by the
/// production <see cref="RagConnectorDocumentSink"/>. What is asserted is what the DATABASE holds
/// afterwards and what the RAG catalogue holds afterwards — a service that returned a happy result
/// and stored nothing would leave the connector screen empty, and that is the failure this suite
/// exists to catch.</para>
/// <para>Waiting is event-driven, never timed: <see cref="SignallingRunRepositoryDecorator"/>
/// completes when the run row is actually written.</para>
/// </remarks>
public sealed class ConnectorJobServiceTests : IAsyncDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), "techiedesk-connector-jobs", Guid.NewGuid().ToString("N"));

    /// <summary>Removes the temporary database and vector store.</summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// A run started through the connector API is visible in the live list while it runs, and the
    /// documents it reports as ingested really are in the catalogue afterwards.
    /// </summary>
    [Fact]
    public async Task AStartedRunIsVisibleWhileItRunsAndItsDocumentsAreReallyIngested()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Connector
            .Add("1", "readme.md", "The project reads a repository and indexes its prose.", "sha-1")
            .Add("2", "guide.md", "A second document with different words in it.", "sha-2")
            .Add("3", "notes.md", "A third document, also readable.", "sha-3");

        var activeMidRun = 0;
        JobProgressSnapshot? snapshotMidRun = null;
        harness.Connector.BeforeFetch = item =>
        {
            if (item.Id == "3")
            {
                var active = harness.Service.ActiveRuns;
                activeMidRun = active.Count;
                snapshotMidRun = active.FirstOrDefault();
            }

            return Task.CompletedTask;
        };

        var runId = await harness.Service.StartAsync(harness.Payload);
        await harness.Runs.Completed;

        Assert.Equal(1, activeMidRun);
        Assert.NotNull(snapshotMidRun);
        Assert.Equal(2, snapshotMidRun!.Processed);
        Assert.Equal(ConnectorJobHandler.Kind, snapshotMidRun.JobKind);

        var report = await harness.Service.GetReportAsync(runId);
        Assert.NotNull(report);
        Assert.Equal(RunOutcome.Succeeded, report!.Outcome);
        Assert.Equal(3, report.Ingested.Count);
        Assert.Equal("Ingested 3 documents.", Summary(report));

        var catalogue = await harness.Rag.ListDocumentsAsync();
        Assert.Equal(3, catalogue.Count);
        Assert.Contains(catalogue, document => document.Name == "guide.md");
    }

    /// <summary>
    /// A mixed run persists a per-item row for every item it touched, each skip carrying a reason,
    /// and reports itself as partial rather than as a success.
    /// </summary>
    [Fact]
    public async Task AMixedRunPersistsEveryItemAndEveryReason()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Connector
            .Add("good", "readme.md", "Readable prose that will be indexed.", "sha-1")
            .Add("empty", "logo.png", "  ", "sha-2")
            .AddFailing("broken", "private.md", new UnauthorizedAccessException("the token was rejected"))
            .Add("huge", "dump.sql", "big", "sha-4", 9_000_000);

        var runId = await harness.Service.StartAsync(harness.Payload);
        await harness.Runs.Completed;

        var report = await harness.Service.GetReportAsync(runId);
        Assert.NotNull(report);

        Assert.Equal(RunOutcome.Partial, report!.Outcome);
        Assert.True(report.IsPartial);
        Assert.Equal(4, report.Items.Count);
        Assert.All(report.NotIngested, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason)));
        Assert.Equal("readme.md", Assert.Single(report.Ingested).ItemName);
        Assert.Equal("the token was rejected", Assert.Single(report.Failed).Reason);
        Assert.Equal(
            "Ingested 1 document; 1 item could not be read, 2 items skipped.", Summary(report));

        // Only the one readable document reached the catalogue: the counts are not a claim about
        // searchability that is false.
        Assert.Equal("readme.md", Assert.Single(await harness.Rag.ListDocumentsAsync()).Name);
    }

    /// <summary>
    /// Cancelling a run through the connector API stops it and leaves the documents it had already
    /// ingested in the catalogue.
    /// </summary>
    [Fact]
    public async Task CancellingThroughTheServiceKeepsAlreadyIngestedDocuments()
    {
        await using var harness = await Harness.CreateAsync(directory);
        for (var index = 1; index <= 6; index++)
        {
            harness.Connector.Add($"{index}", $"file-{index}.md", $"Document number {index}.", $"sha-{index}");
        }

        var cancelled = false;
        harness.Connector.BeforeFetch = item =>
        {
            if (item.Id == "4" && !cancelled)
            {
                cancelled = harness.Service.Cancel(harness.Service.ActiveRuns[0].ScheduleRunId);
            }

            return Task.CompletedTask;
        };

        var runId = await harness.Service.StartAsync(harness.Payload);
        await harness.Runs.Completed;

        Assert.True(cancelled);
        var report = await harness.Service.GetReportAsync(runId);
        Assert.NotNull(report);
        Assert.Equal(RunOutcome.Cancelled, report!.Outcome);
        Assert.Equal(3, report.Ingested.Count);
        Assert.Contains("were kept", Summary(report), StringComparison.Ordinal);
        Assert.Equal(3, (await harness.Rag.ListDocumentsAsync()).Count);
    }

    /// <summary>
    /// The connector screen only ever sees connector runs; a maintenance job running at the same time
    /// stays out of it.
    /// </summary>
    [Fact]
    public async Task TheLiveListShowsOnlyConnectorRuns()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Connector.Add("1", "readme.md", "prose", "sha-1");

        var kindsMidRun = Array.Empty<string>();
        harness.Connector.BeforeFetch = _ =>
        {
            kindsMidRun = harness.Service.ActiveRuns.Select(job => job.JobKind).ToArray();
            return Task.CompletedTask;
        };

        await harness.Service.StartAsync(harness.Payload);
        await harness.Runs.Completed;

        Assert.Equal([ConnectorJobHandler.Kind], kindsMidRun);
    }

    /// <summary>
    /// A request that names no connector is refused before a run row is opened, so the history has no
    /// row for a request that should never have been accepted.
    /// </summary>
    [Fact]
    public async Task AnUnusableRequestIsRefusedBeforeARunRowIsOpened()
    {
        await using var harness = await Harness.CreateAsync(directory);

        var rejected = await Assert.ThrowsAsync<ConnectorException>(
            () => harness.Service.StartAsync(new ConnectorJobPayload()));

        Assert.Equal("The run does not say which connector to read.", rejected.Message);
        Assert.Empty(await harness.Service.ListRecentAsync(10));
    }

    /// <summary>
    /// On a build with no connectors wired, the hub is told so plainly rather than being handed a
    /// missing-service exception.
    /// </summary>
    [Fact]
    public void ABuildWithNoConnectorsSaysSoRatherThanFailing()
    {
        var resolver = new NoConnectorsResolver();

        Assert.Empty(resolver.AvailableTypes);
        Assert.Equal(
            "No connector types are installed in this build of TechieDesk.",
            resolver.Validate(new ConnectorJobPayload { ConnectorId = "x", ConnectorType = "y" }));
    }

    /// <summary>Renders a run's summary in English, through the real localizer.</summary>
    /// <param name="report">The report to summarize.</param>
    /// <returns>The summary line the connector hub would show.</returns>
    /// <remarks>
    /// REQ-UI-055 / BRD-91: <c>SummaryText</c> composes the line out of resource KEYS, so these
    /// assertions resolve through <see cref="ResourceHarness"/>. The expected English is unchanged
    /// from what shipped — it simply no longer lives in the service layer.
    /// </remarks>
    private static string Summary(ConnectorRunReport report)
    {
        using var resources = new ResourceHarness("en");
        return report.SummaryText(resources.Localize);
    }

    /// <summary>
    /// The whole production path — a real SQLite application database, the real background job
    /// service, and a real TechieRag catalogue — with only the source itself scripted.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ITechieRag rag;

        private Harness(
            ITechieRag rag,
            SignallingRunRepositoryDecorator runs,
            IConnectorJobService service,
            FakeConnectorResolver resolver,
            FakeConnector connector)
        {
            this.rag = rag;
            Runs = runs;
            Service = service;
            Resolver = resolver;
            Connector = connector;
        }

        public SignallingRunRepositoryDecorator Runs { get; }

        public IConnectorJobService Service { get; }

        public FakeConnectorResolver Resolver { get; }

        public FakeConnector Connector { get; }

        public ITechieRag Rag => rag;

        public ConnectorJobPayload Payload { get; } = new()
        {
            ConnectorId = "fake-1",
            ConnectorType = "fake",
            DisplayName = "Fake repository",
            RequestDelayMs = 0,
        };

        public static async Task<Harness> CreateAsync(string directory)
        {
            var root = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(root, "techiedesk.db"),
            }.ToString();
            MigrationRunner.Run("Sqlite", connectionString);

            var runs = new SignallingRunRepositoryDecorator(new ScheduleRunRepository(
                new AppDbConnectionFactory(Options.Create(new AppDbOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = connectionString,
                }))));

            var rag = new TechieRagBuilder()
                .UseCustomEmbeddingProvider(() => new StubEmbeddingProvider())
                .UseVectorStore(VectorStoreType.SqliteVec, $"Data Source={Path.Combine(root, "vectors.db")}")
                .WithPersistence(StoreProvider.Sqlite, $"Data Source={Path.Combine(root, "rag.db")}")
                .Build();
            await rag.InitializeAsync();

            var connector = new FakeConnector();
            var resolver = new FakeConnectorResolver(connector);

            // The PRODUCTION sink, over a real RAG instance. No workspace linker: the scheduler helper
            // host runs without one, and a run there must still ingest and still say what it did.
            var services = new ServiceCollection();
            services.AddSingleton<IConnectorResolver>(resolver);
            services.AddSingleton<IConnectorDocumentSink>(
                new RagConnectorDocumentSink(rag, NullLogger<RagConnectorDocumentSink>.Instance));
            var provider = services.BuildServiceProvider();

            var handler = new ConnectorJobHandler(
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<ConnectorRunner>.Instance,
                TimeProvider.System,
                NullLogger<ConnectorJobHandler>.Instance);

            var jobRunner = new JobRunner(
                runs, [handler], TimeProvider.System, NullLogger<JobRunner>.Instance);
            var backgroundJobs = new BackgroundJobService(
                jobRunner, NullLogger<BackgroundJobService>.Instance);

            var service = new ConnectorJobService(
                backgroundJobs, runs, provider.GetRequiredService<IServiceScopeFactory>());

            return new Harness(rag, runs, service, resolver, connector);
        }

        public ValueTask DisposeAsync()
        {
            (rag as IDisposable)?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
