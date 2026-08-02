using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Connectors;
using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;
using Xunit;

namespace TechieDesk.Tests.Connectors;

/// <summary>
/// REQ-FN-020 / REQ-RAG-019: two hand-started runs of the same connector cannot overlap.
/// </summary>
/// <remarks>
/// <para><b>The defect these tests pin down.</b> <see cref="BackgroundJobService.IsRunning(long)"/>
/// matches on <c>ScheduleId</c>, and a run started from a screen has none — so <c>StartAsync</c> had
/// no in-flight guard at all. Double-clicking "Sync now" walked the source twice at once: two
/// listings, two sets of fetches against one rate limit, every changed item ingested twice, and two
/// runs each saving sync state neither could see.</para>
/// <para><b>Deterministic, not timed.</b> The second start happens at an exact point in the first run
/// — inside the fetch of a known item — because <see cref="FakeConnector.BeforeFetch"/> puts the test
/// there. Waiting for a run to be finished AND released waits on the service's own <c>Changed</c>
/// event, which is raised after the in-flight claim is dropped. Nothing sleeps and nothing polls.
/// </para>
/// </remarks>
public sealed class ConnectorDoubleStartTests
{
    /// <summary>
    /// A second start while the first is still walking joins that run instead of opening another.
    /// </summary>
    /// <remarks>
    /// Reddens without the guard: the second call returns a DIFFERENT run id, a second run row is
    /// opened, and the sink receives each document twice.
    /// </remarks>
    [Fact]
    public async Task ASecondStartWhileTheFirstIsRunningJoinsItInsteadOfWalkingTheSourceTwice()
    {
        var harness = Harness.Create();
        harness.Connector
            .Add("1", "readme.md", "The first file.", "sha-1")
            .Add("2", "guide.md", "The second file.", "sha-2");

        var reachedFirstFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Connector.BeforeFetch = async item =>
        {
            if (item.Id == "1")
            {
                reachedFirstFetch.TrySetResult();
                await release.Task.ConfigureAwait(false);
            }
        };

        var firstStart = harness.Service.StartAsync(harness.Payload);
        await reachedFirstFetch.Task;

        // The first run is mid-walk, holding the connector. This is the second click.
        var secondRunId = await harness.Service.StartAsync(harness.Payload);
        var firstRunId = await firstStart;

        release.TrySetResult();
        await harness.WaitUntilIdleAsync(1);

        Assert.Equal(firstRunId, secondRunId);
        Assert.Single(harness.Runs.Runs);
        Assert.Equal(2, harness.Sink.Ingested.Count);
        Assert.Equal(["1", "2"], harness.Connector.FetchedIds);
    }

    /// <summary>
    /// The guard is released when the run ends, so the next sync really does start a new run.
    /// </summary>
    /// <remarks>
    /// The failure mode on the other side of this fix is a guard that never lets go — a connector
    /// that syncs once and then silently refuses forever is worse than one that syncs twice.
    /// </remarks>
    [Fact]
    public async Task AfterTheRunFinishesTheSameConnectorCanBeStartedAgain()
    {
        var harness = Harness.Create();
        harness.Connector.Add("1", "readme.md", "The only file.", "sha-1");

        var firstRunId = await harness.Service.StartAsync(harness.Payload);
        await harness.WaitUntilIdleAsync(1);

        var secondRunId = await harness.Service.StartAsync(harness.Payload);
        await harness.WaitUntilIdleAsync(2);

        Assert.NotEqual(firstRunId, secondRunId);
        Assert.Equal(2, harness.Runs.Runs.Count);
    }

    /// <summary>
    /// Two DIFFERENT connectors are not the same job, so they run side by side.
    /// </summary>
    /// <remarks>
    /// The guard keys on the connector, not on the job kind. Keying on the kind would have made a
    /// repository sync block an unrelated wiki sync, which is a self-inflicted serialization of the
    /// one screen the user watches.
    /// </remarks>
    [Fact]
    public async Task TwoDifferentConnectorsAreNotBlockedByEachOther()
    {
        var harness = Harness.Create();
        harness.Connector.Add("1", "readme.md", "The only file.", "sha-1");

        var first = await harness.Service.StartAsync(harness.Payload);
        var second = await harness.Service.StartAsync(harness.Payload with { ConnectorId = "fake-2" });
        await harness.WaitUntilIdleAsync(2);

        Assert.NotEqual(first, second);
        Assert.Equal(2, harness.Runs.Runs.Count);
    }

    /// <summary>The wiring under test: the production job service, with fakes for the source only.</summary>
    private sealed class Harness
    {
        private Harness(
            IConnectorJobService service,
            FakeConnector connector,
            RecordingDocumentSink sink,
            CountingRunRepository runs)
        {
            Service = service;
            Connector = connector;
            Sink = sink;
            Runs = runs;
        }

        public IConnectorJobService Service { get; }

        public FakeConnector Connector { get; }

        public RecordingDocumentSink Sink { get; }

        public CountingRunRepository Runs { get; }

        public ConnectorJobPayload Payload { get; } = new()
        {
            ConnectorId = "fake-1",
            ConnectorType = "fake",
            DisplayName = "Fake repository",
            RequestDelayMs = 0,
        };

        public static Harness Create()
        {
            var connector = new FakeConnector();
            var sink = new RecordingDocumentSink();
            var runs = new CountingRunRepository();

            var services = new ServiceCollection();
            services.AddSingleton<IConnectorResolver>(new FakeConnectorResolver(connector));
            services.AddSingleton<IConnectorDocumentSink>(sink);
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

            return new Harness(service, connector, sink, runs);
        }

        /// <summary>
        /// Waits until the given number of runs have closed AND no run is still in flight.
        /// </summary>
        /// <param name="expectedRuns">How many closed runs to wait for.</param>
        /// <returns>A task that completes when the service is idle at that count.</returns>
        /// <remarks>
        /// Waiting on the run row alone is not enough: the run row is written while the job's own
        /// completion path is still on the stack, so a start issued at that instant would legitimately
        /// join the finishing run. <c>Changed</c> is raised after the in-flight claim is dropped, which
        /// is the moment "the previous sync is over" actually becomes true.
        /// </remarks>
        public async Task WaitUntilIdleAsync(int expectedRuns)
        {
            var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnChanged()
            {
                if (Runs.CompletedCount >= expectedRuns && Service.ActiveRuns.Count == 0)
                {
                    idle.TrySetResult();
                }
            }

            Service.Changed += OnChanged;
            try
            {
                OnChanged();
                await idle.Task.ConfigureAwait(false);
            }
            finally
            {
                Service.Changed -= OnChanged;
            }
        }
    }
}

/// <summary>
/// An in-memory <see cref="IScheduleRunRepository"/> that counts closed runs.
/// </summary>
/// <remarks>
/// <see cref="SignallingRunRepository"/> signals exactly once, which is right for a single-run test
/// and not enough for these: they start a second sync after the first has finished and released.
/// </remarks>
public sealed class CountingRunRepository : IScheduleRunRepository
{
    private readonly object gate = new();

    private long nextId = 1;
    private int completedCount;

    /// <summary>Gets the runs opened, in order.</summary>
    public List<ScheduleRun> Runs { get; } = [];

    /// <summary>Gets the per-item rows written, in order.</summary>
    public List<ScheduleRunItem> Items { get; } = [];

    /// <summary>Gets how many runs have been closed.</summary>
    public int CompletedCount { get { lock (gate) { return completedCount; } } }

    /// <inheritdoc />
    public Task<long> StartAsync(ScheduleRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (gate)
        {
            run.ScheduleRunId = nextId++;
            Runs.Add(run);
        }

        return Task.FromResult(run.ScheduleRunId);
    }

    /// <inheritdoc />
    public Task CompleteAsync(ScheduleRun run)
    {
        lock (gate)
        {
            completedCount++;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AddItemsAsync(long scheduleRunId, IReadOnlyList<ScheduleRunItem> items)
    {
        lock (gate)
        {
            Items.AddRange(items);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRun>> ListRecentAsync(int limit)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<ScheduleRun>>(
                Runs.AsEnumerable().Reverse().Take(limit).ToList());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRun>> ListForScheduleAsync(long scheduleId, int limit)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<ScheduleRun>>(Runs
                .Where(run => run.ScheduleId == scheduleId)
                .Reverse()
                .Take(limit)
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRunItem>> ListItemsAsync(long scheduleRunId)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<ScheduleRunItem>>(Items
                .Where(item => item.ScheduleRunId == scheduleRunId)
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task<int> CloseAbandonedRunsAsync(string reason, DateTime asOfUtc) => Task.FromResult(0);
}
