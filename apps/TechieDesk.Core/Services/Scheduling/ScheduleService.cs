using TechieDesk.Services.Localization;
using TechieDesk.Services.Scheduling.Authoring;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Default <see cref="IScheduleService"/> (REQ-FN-028, REQ-UI-046).
/// </summary>
/// <remarks>
/// <b>The confirmation gate lives here, not in the page.</b> A guard implemented in a Razor component
/// protects one caller; implemented here it protects every caller, including a future flow builder
/// and anything a later release adds. ADR-010's "nothing saves without an explicit confirm" is a
/// property of saving, not of one dialog.
/// </remarks>
public sealed class ScheduleService : IScheduleService
{
    private readonly IScheduleRepository scheduleRepository;
    private readonly IScheduleRunRepository runRepository;
    private readonly IBackgroundJobService backgroundJobs;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ScheduleService> logger;
    private readonly LocalizeText localize;

    /// <summary>Initializes the service.</summary>
    /// <param name="scheduleRepository">Schedule persistence.</param>
    /// <param name="runRepository">Run-history persistence.</param>
    /// <param name="backgroundJobs">Runs jobs on demand.</param>
    /// <param name="timeProvider">Clock.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    public ScheduleService(
        IScheduleRepository scheduleRepository,
        IScheduleRunRepository runRepository,
        IBackgroundJobService backgroundJobs,
        TimeProvider timeProvider,
        ILogger<ScheduleService> logger,
        LocalizeText localize)
    {
        this.scheduleRepository = scheduleRepository;
        this.runRepository = runRepository;
        this.backgroundJobs = backgroundJobs;
        this.timeProvider = timeProvider;
        this.logger = logger;
        this.localize = localize;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Schedule>> ListAsync() => scheduleRepository.ListAsync();

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRun>> ListRecentRunsAsync(int limit = 50) =>
        runRepository.ListRecentAsync(limit);

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRunItem>> ListRunItemsAsync(long scheduleRunId) =>
        runRepository.ListItemsAsync(scheduleRunId);

    /// <inheritdoc />
    public async Task<Schedule> CreateAsync(ScheduleDraft draft, ScheduleConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(confirmation);

        if (!string.Equals(confirmation.ReviewedScheduleText, draft.ScheduleText, StringComparison.Ordinal))
        {
            throw new ScheduleNotConfirmedException(localize(
                "ScheduleNotConfirmedTextChanged",
                confirmation.ReviewedScheduleText,
                draft.ScheduleText));
        }

        if (!string.Equals(confirmation.ReviewedActionSummary, draft.ActionSummary, StringComparison.Ordinal))
        {
            throw new ScheduleNotConfirmedException(localize(
                "ScheduleNotConfirmedActionChanged",
                confirmation.ReviewedActionSummary,
                draft.ActionSummary));
        }

        if (!draft.IsSavable)
        {
            throw new ScheduleNotConfirmedException(localize("ScheduleNotConfirmedNotValid"));
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var schedule = new Schedule
        {
            Name = draft.Name,
            JobKind = draft.JobKind,
            JobPayload = draft.JobPayload,
            ActionSummary = draft.ActionSummary,
            CronExpression = draft.CronExpression,
            TimeZoneId = draft.TimeZoneId,
            ScheduleText = draft.ScheduleText,
            SourceInstruction = draft.Instruction,
            IsEnabled = true,
            CatchUpMissedRuns = draft.CatchUpMissedRuns,
            NotifyOnFailure = draft.NotifyOnFailure,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        };
        schedule.NextRunUtc = ScheduleCalculator.NextRunUtc(schedule, nowUtc);

        await scheduleRepository.CreateAsync(schedule).ConfigureAwait(false);
        logger.LogInformation(
            "Created schedule {Name}: {ScheduleText} ({Action})",
            schedule.Name, schedule.ScheduleText, schedule.ActionSummary);
        return schedule;
    }

    /// <inheritdoc />
    public async Task SetEnabledAsync(long scheduleId, bool isEnabled)
    {
        var schedule = await scheduleRepository.GetAsync(scheduleId).ConfigureAwait(false);
        if (schedule is null)
        {
            return;
        }

        // Resuming recomputes from now rather than restoring the stored instant: a schedule paused
        // for a fortnight would otherwise come back due, and fire the moment it is un-paused.
        var nextRunUtc = isEnabled
            ? ScheduleCalculator.NextRunUtc(schedule, timeProvider.GetUtcNow().UtcDateTime)
            : null;
        await scheduleRepository.SetEnabledAsync(scheduleId, isEnabled, nextRunUtc).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DeleteAsync(long scheduleId) => scheduleRepository.DeleteAsync(scheduleId);

    /// <inheritdoc />
    public async Task<ScheduleRun?> RunNowAsync(long scheduleId)
    {
        var schedule = await scheduleRepository.GetAsync(scheduleId).ConfigureAwait(false);
        if (schedule is null)
        {
            return null;
        }

        // A manual run does not disturb the schedule: the next occurrence stays where it was, because
        // pressing Run now is not a request to move tomorrow's 07:00.
        return await backgroundJobs.RunScheduleAsync(schedule, RunTrigger.Manual).ConfigureAwait(false);
    }
}
