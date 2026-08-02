using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// Default <see cref="IConnectorJobService"/>: a connector-shaped view over the one background job
/// service the app already has (REQ-FN-020, BRD-65, ADR-009).
/// </summary>
/// <remarks>
/// <para><b>It owns no scheduling of its own.</b> Every method here delegates to
/// <see cref="IBackgroundJobService"/> and <see cref="IScheduleRunRepository"/>. There is one
/// in-flight registry, one cancellation token per run and one run-history table in this process, and
/// this class exists to filter and reshape them for one screen — not to duplicate them.</para>
/// </remarks>
public sealed class ConnectorJobService : IConnectorJobService
{
    /// <summary>
    /// How far back recent history is scanned when looking for connector runs.
    /// </summary>
    /// <remarks>
    /// <see cref="IScheduleRunRepository"/> can list recent runs across all kinds but cannot query by
    /// kind or by id, so a connector run is found by scanning a bounded window. The window is large
    /// enough that a desktop install will not lose a run the user just started, and bounded so the
    /// connector screen cannot read the whole history table.
    /// </remarks>
    public const int HistoryScanDepth = 200;

    private readonly IBackgroundJobService backgroundJobs;
    private readonly IScheduleRunRepository runs;
    private readonly IServiceScopeFactory scopeFactory;

    /// <summary>Initializes a new instance of the <see cref="ConnectorJobService"/> class.</summary>
    /// <param name="backgroundJobs">The one background job service (ADR-009).</param>
    /// <param name="runs">Run history and per-item results.</param>
    /// <param name="scopeFactory">Opens a scope to ask the resolver which connector types exist.</param>
    public ConnectorJobService(
        IBackgroundJobService backgroundJobs,
        IScheduleRunRepository runs,
        IServiceScopeFactory scopeFactory)
    {
        this.backgroundJobs = backgroundJobs ?? throw new ArgumentNullException(nameof(backgroundJobs));
        this.runs = runs ?? throw new ArgumentNullException(nameof(runs));
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <inheritdoc />
    public event Action? Changed
    {
        add => backgroundJobs.Changed += value;
        remove => backgroundJobs.Changed -= value;
    }

    /// <inheritdoc />
    public IReadOnlyList<ConnectorTypeDescriptor> AvailableTypes
    {
        get
        {
            using var scope = scopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IConnectorResolver>().AvailableTypes;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<JobProgressSnapshot> ActiveRuns => backgroundJobs.ActiveJobs
        .Where(job => job.JobKind.Equals(ConnectorJobHandler.Kind, StringComparison.OrdinalIgnoreCase))
        .ToList();

    /// <inheritdoc />
    public string? Validate(ConnectorJobPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var invalid = payload.Validate();
        if (invalid is not null)
        {
            return invalid;
        }

        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IConnectorResolver>().Validate(payload);
    }

    /// <inheritdoc />
    public Task<long> StartAsync(ConnectorJobPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Checked before the run row is opened. Opening a run only to close it as failed would put a
        // row in the history for a request that never should have been accepted.
        var invalid = Validate(payload);
        if (invalid is not null)
        {
            throw new ConnectorException(payload.ConnectorType, invalid);
        }

        var name = string.IsNullOrWhiteSpace(payload.DisplayName) ? payload.ConnectorId : payload.DisplayName;

        // Keyed on the CONNECTOR, not on the run's name or its payload. Two requests to sync the same
        // source are the same job however they were phrased, and letting both walk it concurrently is
        // what turned a double-clicked "Sync now" into two listings, two sets of fetches against one
        // rate limit, and every changed item ingested twice.
        return backgroundJobs.StartAsync(
            name, ConnectorJobHandler.Kind, payload.ToJson(), RunKeyFor(payload.ConnectorId));
    }

    /// <summary>Builds the in-flight key that makes two runs of one connector the same job.</summary>
    /// <param name="connectorId">The saved connector's key.</param>
    /// <returns>The run key handed to the background job service.</returns>
    public static string RunKeyFor(string connectorId) =>
        $"{ConnectorJobHandler.Kind}:{connectorId}";

    /// <inheritdoc />
    public bool Cancel(long runId) => backgroundJobs.Cancel(runId);

    /// <inheritdoc />
    public async Task<ConnectorRunReport?> GetReportAsync(long runId)
    {
        var run = (await runs.ListRecentAsync(HistoryScanDepth).ConfigureAwait(false))
            .FirstOrDefault(candidate => candidate.ScheduleRunId == runId);

        if (run is null)
        {
            return null;
        }

        var items = await runs.ListItemsAsync(runId).ConfigureAwait(false);
        return ConnectorRunReport.From(run, items);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConnectorRunReport>> ListRecentAsync(int limit)
    {
        var recent = await runs
            .ListRecentAsync(Math.Max(limit, HistoryScanDepth))
            .ConfigureAwait(false);

        var reports = new List<ConnectorRunReport>();
        foreach (var run in recent.Where(IsConnectorRun).Take(Math.Max(0, limit)))
        {
            var items = await runs.ListItemsAsync(run.ScheduleRunId).ConfigureAwait(false);
            reports.Add(ConnectorRunReport.From(run, items));
        }

        return reports;
    }

    /// <summary>Determines whether a recorded run was a connector run.</summary>
    /// <param name="run">The run row.</param>
    /// <returns><see langword="true"/> when it was.</returns>
    private static bool IsConnectorRun(ScheduleRun run) =>
        run.JobKind.Equals(ConnectorJobHandler.Kind, StringComparison.OrdinalIgnoreCase);
}
