namespace TechieDesk.Services.Scheduling;

/// <summary>
/// The background-service settings from the Automations screen's Background service dialog
/// (BRD-139 / REQ-FN-042).
/// </summary>
/// <param name="BackgroundServiceEnabled">Whether the user asked for schedules to run with the window closed.</param>
/// <param name="MainsPowerOnly">Only run while on mains power.</param>
/// <param name="NamedNetworksOnly">Only run while joined to one of <paramref name="AllowedNetworks"/>.</param>
/// <param name="AllowedNetworks">Network names background runs are permitted on.</param>
/// <param name="WakeForRun">Ask the OS to wake the machine for a scheduled run.</param>
/// <param name="ShowMenuBarItem">Show a menu-bar/tray indicator while the helper is running.</param>
/// <remarks>
/// <see cref="BackgroundServiceEnabled"/> is the user's <i>intent</i> and is not the same thing as the
/// helper being installed — installing is an OS action that can fail or be refused. The screen must
/// read the installed state from <see cref="ISchedulerHelper"/> and never infer it from this flag,
/// because "you asked for it" and "it is running" differ exactly when it matters.
/// </remarks>
public sealed record SchedulerPreferences(
    bool BackgroundServiceEnabled = false,
    bool MainsPowerOnly = false,
    bool NamedNetworksOnly = false,
    IReadOnlyList<string>? AllowedNetworks = null,
    bool WakeForRun = false,
    bool ShowMenuBarItem = false)
{
    /// <summary>Gets the defaults for a fresh install: nothing installed, nothing restricted.</summary>
    public static SchedulerPreferences Default { get; } = new();

    /// <summary>Projects the run-condition subset the scheduler tests each poll.</summary>
    /// <returns>The run conditions.</returns>
    public RunConditions ToRunConditions() =>
        new(MainsPowerOnly, NamedNetworksOnly, AllowedNetworks);
}
