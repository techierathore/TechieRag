using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using TechieDesk.Resources;
using TechieDesk.Services.Localization;
using TechieDesk.Services.Scheduling;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// The REAL <see cref="LocalizeText"/> the scheduling cluster takes, bound to the app's own resource
/// set (REQ-UI-055 / BRD-91).
/// </summary>
/// <remarks>
/// <para>
/// Not a stub that echoes the key back. Every one of these tests would still pass against a stub
/// while a Hindi install rendered <c>CronDaysEveryWeekday</c> on screen — that is the class of defect
/// REQ-UI-051 was raised for, and a fake localizer would rebuild it inside the test suite. Going
/// through <c>ResourceManagerStringLocalizer</c> means a key that is not in the .resx resolves to its
/// own name and the assertion fails.
/// </para>
/// <para>
/// It resolves in whatever <c>CurrentUICulture</c> is ambient, so a test that wants a specific
/// language uses <c>ResourceHarness</c> and its own <c>Localize</c> instead.
/// </para>
/// </remarks>
public static class SchedulingText
{
    private static readonly IStringLocalizer<AppStrings> Localizer =
        new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<AppStrings>>();

    /// <summary>Gets the delegate the scheduling services take.</summary>
    public static LocalizeText Localize { get; } = (key, arguments) =>
        arguments.Length == 0 ? Localizer[key].Value : Localizer[key, arguments!].Value;
}

/// <summary>
/// A clock the test moves by hand.
/// </summary>
/// <remarks>
/// The whole scheduling cluster takes <see cref="TimeProvider"/> rather than calling
/// <c>DateTime.UtcNow</c>, which is what makes a DST transition or a week-long absence something a
/// test can assert instead of wait for.
/// </remarks>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now;

    /// <summary>Initializes the clock.</summary>
    /// <param name="startUtc">The instant the clock starts at.</param>
    public TestClock(DateTime startUtc)
    {
        now = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc));
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="amount">How far.</param>
    public void Advance(TimeSpan amount) => now = now.Add(amount);

    /// <summary>Moves the clock to an instant.</summary>
    /// <param name="instantUtc">The new instant.</param>
    public void MoveTo(DateTime instantUtc) =>
        now = new DateTimeOffset(DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc));
}

/// <summary>In-memory <see cref="IScheduleRepository"/>.</summary>
public sealed class FakeScheduleRepository : IScheduleRepository
{
    private long nextId = 1;

    /// <summary>Gets the schedules held, in insertion order.</summary>
    public List<Schedule> Items { get; } = [];

    /// <summary>
    /// Gets the arguments of every <see cref="RecordRunAsync"/> call, in order.
    /// </summary>
    /// <remarks>
    /// Recorded separately from <see cref="Items"/> because the fake hands back the same object the
    /// caller inserted: a scheduler that only mutated the in-memory instance and never wrote would be
    /// indistinguishable from one that persisted, and a test reading <see cref="Items"/> would pass
    /// either way. This list is what makes "the next run was WRITTEN before the job started"
    /// assertable.
    /// </remarks>
    public List<(long ScheduleId, DateTime LastRunUtc, DateTime? NextRunUtc)> RecordedRuns { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<Schedule>> ListAsync() =>
        Task.FromResult<IReadOnlyList<Schedule>>(Items.ToList());

    /// <inheritdoc />
    public Task<Schedule?> GetAsync(long scheduleId) =>
        Task.FromResult(Items.FirstOrDefault(item => item.ScheduleId == scheduleId));

    /// <inheritdoc />
    public Task<long> CreateAsync(Schedule schedule)
    {
        schedule.ScheduleId = nextId++;
        Items.Add(schedule);
        return Task.FromResult(schedule.ScheduleId);
    }

    /// <inheritdoc />
    public Task UpdateAsync(Schedule schedule) => Task.CompletedTask;

    /// <inheritdoc />
    public Task DeleteAsync(long scheduleId)
    {
        Items.RemoveAll(item => item.ScheduleId == scheduleId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetEnabledAsync(long scheduleId, bool isEnabled, DateTime? nextRunUtc)
    {
        var schedule = Items.FirstOrDefault(item => item.ScheduleId == scheduleId);
        if (schedule is not null)
        {
            schedule.IsEnabled = isEnabled;
            schedule.NextRunUtc = nextRunUtc;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Schedule>> ListDueAsync(DateTime asOfUtc) =>
        Task.FromResult<IReadOnlyList<Schedule>>(Items
            .Where(item => item.IsEnabled && item.NextRunUtc is not null && item.NextRunUtc <= asOfUtc)
            .OrderBy(item => item.NextRunUtc)
            .ToList());

    /// <inheritdoc />
    public Task RecordRunAsync(long scheduleId, DateTime lastRunUtc, DateTime? nextRunUtc)
    {
        RecordedRuns.Add((scheduleId, lastRunUtc, nextRunUtc));
        var schedule = Items.FirstOrDefault(item => item.ScheduleId == scheduleId);
        if (schedule is not null)
        {
            schedule.LastRunUtc = lastRunUtc;
            schedule.NextRunUtc = nextRunUtc;
        }

        return Task.CompletedTask;
    }
}

/// <summary>In-memory <see cref="IScheduleRunRepository"/>.</summary>
public sealed class FakeScheduleRunRepository : IScheduleRunRepository
{
    private long nextId = 1;

    /// <summary>Gets the runs recorded, in insertion order.</summary>
    public List<ScheduleRun> Runs { get; } = [];

    /// <summary>Gets the per-item rows recorded, in insertion order.</summary>
    public List<ScheduleRunItem> Items { get; } = [];

    /// <inheritdoc />
    public Task<long> StartAsync(ScheduleRun run)
    {
        run.ScheduleRunId = nextId++;
        Runs.Add(run);
        return Task.FromResult(run.ScheduleRunId);
    }

    /// <inheritdoc />
    public Task CompleteAsync(ScheduleRun run) => Task.CompletedTask;

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
    public Task<int> CloseAbandonedRunsAsync(JobMessage reason, DateTime asOfUtc)
    {
        var closed = 0;
        foreach (var run in Runs.Where(run => run.Outcome == RunOutcome.Running))
        {
            run.Outcome = RunOutcome.Failed;
            run.FailureReason = reason.ToInvariantString();
            run.FailureReasonJson = reason.ToStorage();
            run.CompletedUtc = asOfUtc;
            closed++;
        }

        return Task.FromResult(closed);
    }
}

/// <summary>A handler whose behaviour each test sets.</summary>
public sealed class FakeJobHandler : IScheduledJobHandler
{
    /// <summary>Gets or sets what the handler does when it runs.</summary>
    public Func<JobRunContext, CancellationToken, Task<JobRunResult>> Behaviour { get; set; } =
        (_, _) => Task.FromResult(JobRunResult.Completed);

    /// <summary>Gets or sets the payload validation result.</summary>
    public JobMessage? PayloadError { get; set; }

    /// <summary>Gets how many times the handler ran.</summary>
    public int RunCount { get; private set; }

    /// <inheritdoc />
    public string JobKind { get; set; } = "Test";

    /// <inheritdoc />
    public string DisplayNameKey => "JobKindMaintenanceName";

    /// <inheritdoc />
    public string DescriptionKey => "JobKindMaintenanceDescription";

    /// <inheritdoc />
    public JobMessage DescribeAction(string? payload) =>
        JobMessage.Of("JobKindMaintenanceAction", payload ?? string.Empty);

    /// <inheritdoc />
    public JobMessage? ValidatePayload(string? payload) => PayloadError;

    /// <inheritdoc />
    public Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        RunCount++;
        return Behaviour(context, cancellationToken);
    }
}

/// <summary>An <see cref="IRunEnvironmentProbe"/> whose answers the test sets.</summary>
/// <param name="Power">The power state to report.</param>
/// <param name="Network">The network name to report.</param>
public sealed record FakeRunEnvironmentProbe(PowerState Power, string? Network) : IRunEnvironmentProbe
{
    /// <inheritdoc />
    public PowerState GetPowerState() => Power;

    /// <inheritdoc />
    public string? GetCurrentNetworkName() => Network;
}

/// <summary>An <see cref="ISchedulerPreferencesStore"/> backed by a field.</summary>
public sealed class FakeSchedulerPreferencesStore : ISchedulerPreferencesStore
{
    /// <summary>Gets or sets the current preferences.</summary>
    public SchedulerPreferences Current { get; set; } = SchedulerPreferences.Default;

    /// <inheritdoc />
    public Task<SchedulerPreferences> LoadAsync() => Task.FromResult(Current);

    /// <inheritdoc />
    public Task SaveAsync(SchedulerPreferences preferences)
    {
        Current = preferences;
        return Task.CompletedTask;
    }
}

/// <summary>A locator that reports whatever path the test gives it.</summary>
/// <param name="Path">The path to report, or null for "no helper in this build".</param>
public sealed record FakeHelperLocator(string? Path) : ISchedulerHelperLocator
{
    /// <inheritdoc />
    public string ExecutableName => "TechieDeskScheduler";

    /// <inheritdoc />
    public string? Locate() => Path;
}
