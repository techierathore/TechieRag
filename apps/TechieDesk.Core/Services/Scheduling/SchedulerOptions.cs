namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Configuration for <see cref="SchedulerService"/>, bound from the <c>Scheduler</c> section.
/// </summary>
public sealed class SchedulerOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Scheduler";

    /// <summary>
    /// Gets or sets how often the scheduler looks for due work, in seconds.
    /// </summary>
    /// <remarks>
    /// Thirty seconds, not one. Cron's finest granularity is a minute, so polling faster buys nothing
    /// and costs a wake-up on a battery-powered machine — the same machine BRD-139's mains-power run
    /// condition exists to be careful with.
    /// </remarks>
    public int PollSeconds { get; set; } = 30;

    /// <summary>Gets or sets a value indicating whether the in-app scheduler loop starts with the window.</summary>
    /// <remarks>
    /// Independent of the background helper. With the helper installed the app process still runs
    /// schedules while it is open; both host the same <see cref="SchedulerService"/> and the
    /// per-schedule in-flight guard is what keeps them from doubling up.
    /// </remarks>
    public bool RunWhileAppIsOpen { get; set; } = true;

    /// <summary>Gets the poll interval as a timespan, floored at five seconds.</summary>
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(5, PollSeconds));
}
