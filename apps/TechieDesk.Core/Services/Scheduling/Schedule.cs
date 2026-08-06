namespace TechieDesk.Services.Scheduling;

/// <summary>
/// A saved recurring automation: what to run, when to run it, and what the user was shown when they
/// agreed to it (REQ-FN-028, BRD-93 / BRD-140).
/// </summary>
/// <remarks>
/// A mutable class rather than a record because Dapper materializes it by property setter, and
/// because the scheduler writes <see cref="LastRunUtc"/> / <see cref="NextRunUtc"/> back onto the
/// same instance it read.
/// </remarks>
public sealed class Schedule
{
    /// <summary>Gets or sets the surrogate key. Zero until the row is inserted.</summary>
    public long ScheduleId { get; set; }

    /// <summary>Gets or sets the user-facing job name, unique across schedules.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the handler key deciding what actually runs (see <see cref="IScheduledJobHandler.JobKind"/>).</summary>
    public string JobKind { get; set; } = string.Empty;

    /// <summary>Gets or sets the handler-specific payload, as JSON. Opaque to the scheduler.</summary>
    public string? JobPayload { get; set; }

    /// <summary>Gets or sets the one-line description of what the job does ("Email connector → Contracts").</summary>
    public string ActionSummary { get; set; } = string.Empty;

    /// <summary>Gets or sets the five-field cron expression. Never displayed outside the Advanced disclosure (BRD-140).</summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets the IANA/Windows time-zone id the cron expression's wall clock belongs to.</summary>
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    /// <summary>Gets or sets the plain-language schedule text the user confirmed ("Every weekday at 07:00").</summary>
    public string ScheduleText { get; set; } = string.Empty;

    /// <summary>Gets or sets the natural-language instruction this schedule was authored from, when it was.</summary>
    public string? SourceInstruction { get; set; }

    /// <summary>Gets or sets a value indicating whether the schedule fires. A paused schedule keeps its history.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether a run missed while the machine was asleep or the app
    /// was closed is run once at the next opportunity.
    /// </summary>
    /// <remarks>
    /// Missed occurrences are <b>coalesced into a single run</b>, never replayed one per occurrence.
    /// A laptop shut for a week would otherwise wake up and fire a half-hourly sync 336 times.
    /// </remarks>
    public bool CatchUpMissedRuns { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether a failed run raises a notification.</summary>
    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>Gets or sets when this schedule last started a run, in UTC.</summary>
    public DateTime? LastRunUtc { get; set; }

    /// <summary>Gets or sets the next instant this schedule is due, in UTC.</summary>
    public DateTime? NextRunUtc { get; set; }

    /// <summary>Gets or sets when the schedule was created, in UTC.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Gets or sets when the schedule was last edited, in UTC.</summary>
    public DateTime UpdatedUtc { get; set; }

    /// <summary>
    /// Resolves <see cref="TimeZoneId"/>, falling back to the machine's local zone when the stored id
    /// is unknown on this operating system.
    /// </summary>
    /// <returns>The resolved time zone.</returns>
    /// <remarks>
    /// The fallback is deliberate and not silent-in-spirit: a database written on macOS carries IANA
    /// ids, and the same file opened on Windows must still schedule something rather than throw at
    /// startup and take the whole scheduler down with it. .NET 6+ accepts both id families on both
    /// platforms, so this path is reached only for a genuinely unknown zone.
    /// </remarks>
    public TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
