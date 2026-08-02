namespace TechieDesk.Services.Scheduling;

/// <summary>Why a schedule is being run right now.</summary>
public enum DueKind
{
    /// <summary>Not due yet.</summary>
    NotDue = 0,

    /// <summary>Due now, within the tolerance of the scheduler's poll interval.</summary>
    DueNow = 1,

    /// <summary>The occurrence passed while nothing was running to notice — asleep, or closed.</summary>
    Missed = 2
}

/// <summary>
/// The pure schedule arithmetic: when is this next due, and is a due occurrence a normal tick or one
/// that was missed while the app was not running (REQ-FN-028, BRD-139).
/// </summary>
/// <remarks>
/// Static and clock-free by design — every method takes the instant as an argument. That is what
/// makes DST transitions, month ends and week-long absences testable without waiting for one.
/// </remarks>
public static class ScheduleCalculator
{
    /// <summary>
    /// How late an occurrence may be before it counts as missed rather than simply due.
    /// </summary>
    /// <remarks>
    /// Comfortably wider than the scheduler's poll interval. Set too tight, an ordinary tick that
    /// arrived a few seconds late would be logged as a catch-up and the run history would be full of
    /// phantom outages.
    /// </remarks>
    public static readonly TimeSpan MissedRunThreshold = TimeSpan.FromMinutes(5);

    /// <summary>Computes when a schedule is next due after an instant.</summary>
    /// <param name="schedule">The schedule.</param>
    /// <param name="afterUtc">The instant to search from, exclusive.</param>
    /// <returns>The next occurrence in UTC, or <see langword="null"/> when the expression is unparseable or never fires again.</returns>
    public static DateTime? NextRunUtc(Schedule schedule, DateTime afterUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return CronExpression.TryParse(schedule.CronExpression, out var cron, out _)
            ? cron.GetNextOccurrenceUtc(afterUtc, schedule.ResolveTimeZone())
            : null;
    }

    /// <summary>Classifies whether a schedule is due, and whether the occurrence was missed.</summary>
    /// <param name="schedule">The schedule.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>The classification.</returns>
    /// <remarks>
    /// A disabled schedule is never due. Pausing must stop a schedule from firing the moment the app
    /// reopens after a week — the opposite behaviour would make "paused" mean "queued".
    /// </remarks>
    public static DueKind Classify(Schedule schedule, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!schedule.IsEnabled || schedule.NextRunUtc is not { } due)
        {
            return DueKind.NotDue;
        }

        if (due > nowUtc)
        {
            return DueKind.NotDue;
        }

        return nowUtc - due > MissedRunThreshold ? DueKind.Missed : DueKind.DueNow;
    }

    /// <summary>
    /// Counts the occurrences between two instants, so a caught-up run can say how many ticks it
    /// stands in for.
    /// </summary>
    /// <param name="schedule">The schedule.</param>
    /// <param name="fromUtc">Start of the window, exclusive.</param>
    /// <param name="toUtc">End of the window, inclusive.</param>
    /// <param name="cap">Stop counting at this many; the caller only ever needs "1, a few, or a lot".</param>
    /// <returns>The number of missed occurrences, capped.</returns>
    /// <remarks>
    /// This count is reported, never replayed. A laptop shut for a week would otherwise wake to 336
    /// back-to-back runs of a half-hourly sync — the run the user actually wants is one, now,
    /// covering everything that changed since.
    /// </remarks>
    public static int CountOccurrences(Schedule schedule, DateTime fromUtc, DateTime toUtc, int cap = 500)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!CronExpression.TryParse(schedule.CronExpression, out var cron, out _))
        {
            return 0;
        }

        var zone = schedule.ResolveTimeZone();
        var count = 0;
        var cursor = fromUtc;
        while (count < cap)
        {
            var next = cron.GetNextOccurrenceUtc(cursor, zone);
            if (next is null || next > toUtc)
            {
                break;
            }

            count++;
            cursor = next.Value;
        }

        return count;
    }
}
