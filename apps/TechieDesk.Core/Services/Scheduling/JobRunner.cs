namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Runs one job: opens the run row, hands the handler a progress reporter, persists the per-item
/// results, and classifies the outcome (REQ-FN-028, REQ-FN-020, BRD-65).
/// </summary>
/// <remarks>
/// <para><b>The runner classifies, the handler reports.</b> A handler that decided its own outcome
/// could report "succeeded" while 88 items failed — the exact shape of silent data loss BRD-65 was
/// written against. <see cref="RunOutcome.Partial"/> is therefore derived here from the failure
/// count, and a handler cannot opt out of it.</para>
/// <para><b>A thrown handler is a failed run, not a crashed app.</b> This runs on a background timer
/// inside a desktop process; an escaping exception would take down the window with it.</para>
/// </remarks>
public sealed class JobRunner : IJobRunner
{
    private readonly IScheduleRunRepository runRepository;
    private readonly IReadOnlyList<IScheduledJobHandler> handlers;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<JobRunner> logger;

    /// <summary>Initializes the runner.</summary>
    /// <param name="runRepository">Run-history persistence.</param>
    /// <param name="handlers">Every job handler registered in this process.</param>
    /// <param name="timeProvider">Clock. Injected so run timing is testable without sleeping.</param>
    /// <param name="logger">Logger.</param>
    public JobRunner(
        IScheduleRunRepository runRepository,
        IEnumerable<IScheduledJobHandler> handlers,
        TimeProvider timeProvider,
        ILogger<JobRunner> logger)
    {
        this.runRepository = runRepository;
        this.handlers = handlers.ToList();
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<IScheduledJobHandler> AvailableHandlers => handlers;

    /// <inheritdoc />
    public IScheduledJobHandler? FindHandler(string? jobKind) =>
        string.IsNullOrWhiteSpace(jobKind)
            ? null
            : handlers.FirstOrDefault(
                handler => handler.JobKind.Equals(jobKind, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public Task<ScheduleRun> RunScheduleAsync(
        Schedule schedule,
        RunTrigger trigger,
        Action<JobProgressSnapshot>? onProgress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return ExecuteAsync(
            schedule.ScheduleId, schedule.Name, schedule.JobKind, schedule.JobPayload,
            trigger, onProgress, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ScheduleRun> RunOnceAsync(
        string jobName,
        string jobKind,
        string? payload,
        Action<JobProgressSnapshot>? onProgress,
        CancellationToken cancellationToken) =>
        ExecuteAsync(null, jobName, jobKind, payload, RunTrigger.Background, onProgress, cancellationToken);

    private async Task<ScheduleRun> ExecuteAsync(
        long? scheduleId,
        string jobName,
        string jobKind,
        string? payload,
        RunTrigger trigger,
        Action<JobProgressSnapshot>? onProgress,
        CancellationToken cancellationToken)
    {
        var startedUtc = timeProvider.GetUtcNow().UtcDateTime;
        var run = new ScheduleRun
        {
            ScheduleId = scheduleId,
            JobName = jobName,
            JobKind = jobKind,
            TriggerKind = trigger,
            StartedUtc = startedUtc,
            Outcome = RunOutcome.Running
        };
        await runRepository.StartAsync(run).ConfigureAwait(false);

        var handler = FindHandler(jobKind);
        if (handler is null)
        {
            // Naming the missing kind matters: this is what a schedule whose connector was removed
            // looks like, and "no handler" without the kind sends the user to the wrong problem
            // (REQ-NFR-010).
            return await CloseAsync(
                run,
                RunOutcome.Failed,
                null,
                $"No installed component handles jobs of kind '{jobKind}'.",
                []).ConfigureAwait(false);
        }

        var collector = new JobProgressCollector(
            run.ScheduleRunId, scheduleId, jobName, jobKind, startedUtc, timeProvider, onProgress);
        var context = new JobRunContext(
            run.ScheduleRunId, scheduleId, jobName, jobKind, payload, trigger, collector);

        JobRunResult result;
        RunOutcome outcome;
        string? failureReason = null;

        try
        {
            result = await handler.RunAsync(context, cancellationToken).ConfigureAwait(false);
            failureReason = result.FailureReason;
            outcome = failureReason is not null
                ? RunOutcome.Failed
                : collector.FailedCount > 0 ? RunOutcome.Partial : RunOutcome.Succeeded;
        }
        catch (OperationCanceledException)
        {
            result = JobRunResult.Completed;
            outcome = RunOutcome.Cancelled;
            failureReason = "The run was cancelled.";
            logger.LogInformation("Job {JobName} ({JobKind}) was cancelled", jobName, jobKind);
        }
        catch (Exception exception)
        {
            result = JobRunResult.Completed;
            outcome = RunOutcome.Failed;
            failureReason = exception.Message;
            logger.LogError(exception, "Job {JobName} ({JobKind}) failed", jobName, jobKind);
        }

        var items = collector.DrainItems();
        var detail = result.Detail ?? ComposeDetail(collector);
        if (collector.WasItemListSampled)
        {
            detail += $" · item list capped at {JobProgressCollector.SuccessItemCap} successes (all failures kept)";
        }

        run.ItemsProcessed = collector.Processed;
        run.ItemsFailed = collector.FailedCount;
        run.ItemsSkipped = collector.SkippedCount;
        return await CloseAsync(run, outcome, detail, failureReason, items).ConfigureAwait(false);
    }

    private async Task<ScheduleRun> CloseAsync(
        ScheduleRun run,
        RunOutcome outcome,
        string? detail,
        string? failureReason,
        IReadOnlyList<ScheduleRunItem> items)
    {
        run.Outcome = outcome;
        run.Detail = detail;
        run.FailureReason = failureReason;
        run.CompletedUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (items.Count > 0)
        {
            await runRepository.AddItemsAsync(run.ScheduleRunId, items).ConfigureAwait(false);
        }

        await runRepository.CompleteAsync(run).ConfigureAwait(false);
        logger.LogInformation(
            "Job {JobName} ({JobKind}) finished as {Outcome}: {Processed} processed, {Failed} failed, {Skipped} skipped",
            run.JobName, run.JobKind, outcome, run.ItemsProcessed, run.ItemsFailed, run.ItemsSkipped);
        return run;
    }

    private static string ComposeDetail(JobProgressCollector collector)
    {
        var parts = new List<string> { $"{collector.Processed} processed" };
        if (collector.FailedCount > 0)
        {
            parts.Add($"{collector.FailedCount} failed");
        }

        if (collector.SkippedCount > 0)
        {
            parts.Add($"{collector.SkippedCount} skipped");
        }

        return string.Join(" · ", parts);
    }
}
