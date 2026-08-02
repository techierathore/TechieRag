namespace TechieDesk.Services.Scheduling;

/// <summary>
/// The single seam between the scheduler and the work it runs (REQ-FN-028, REQ-FN-020).
/// </summary>
/// <remarks>
/// <para><b>This is the connector seam.</b> REQ-FN-020 requires connector runs to execute as
/// background jobs with visible progress, per-item results and per-item failure reasons. Rather than
/// the scheduler knowing what a connector is, a connector implementation registers one of these under
/// its own <see cref="JobKind"/> and reports items through <see cref="IJobProgressReporter"/>. The
/// runner then supplies everything BRD-65 asks for — the run row, the live progress, the per-item
/// rows, the outcome classification and the failure reasons — for <i>any</i> handler, without a
/// connector-shaped special case anywhere in this namespace.</para>
/// <para><b>Handlers do not touch the database and do not decide their own outcome.</b> They report
/// what happened; <see cref="JobRunner"/> classifies it. That is what keeps "412 of 500" from being
/// recorded as a success by a handler that returns early on its own opinion.</para>
/// <para><b>Cancellation is cooperative and must be honoured.</b> A desktop user closing the window
/// mid-crawl expects the crawl to stop, and a handler that ignores the token turns a quit into a
/// hang.</para>
/// </remarks>
public interface IScheduledJobHandler
{
    /// <summary>
    /// Gets the stable key this handler answers to, stored on <see cref="Schedule.JobKind"/>.
    /// </summary>
    /// <remarks>Compared case-insensitively. Changing it orphans existing schedules, so treat it as persisted data.</remarks>
    string JobKind { get; }

    /// <summary>Gets the human-facing name of this kind of job, for the authoring dialog's action list.</summary>
    string DisplayName { get; }

    /// <summary>Gets a one-line description of what this handler does, shown when choosing an action.</summary>
    string Description { get; }

    /// <summary>
    /// Renders a payload as the one-line action summary shown in the schedules grid
    /// ("Email connector → Contracts").
    /// </summary>
    /// <param name="payload">The handler-specific payload, or <see langword="null"/>.</param>
    /// <returns>A plain-language summary. Never cron, never JSON.</returns>
    string DescribeAction(string? payload);

    /// <summary>
    /// Validates a payload before a schedule is saved.
    /// </summary>
    /// <param name="payload">The handler-specific payload, or <see langword="null"/>.</param>
    /// <returns><see langword="null"/> when the payload is usable, otherwise the reason it is not.</returns>
    /// <remarks>
    /// Validation happens at save time, never at first use — the standing architectural requirement
    /// from BRD-136. A natural-language draft that named a workspace which does not exist must be
    /// rejected in the confirm dialog, not at 07:00 three days later.
    /// </remarks>
    string? ValidatePayload(string? payload);

    /// <summary>Runs the job.</summary>
    /// <param name="context">What to run, and where to report progress.</param>
    /// <param name="cancellationToken">Cancels the run; must be honoured.</param>
    /// <returns>What the handler observed. The runner decides the outcome from it.</returns>
    Task<JobRunResult> RunAsync(JobRunContext context, CancellationToken cancellationToken);
}
