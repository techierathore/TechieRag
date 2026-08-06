namespace TechieDesk.Services.Scheduling;

/// <summary>Whether the background scheduler helper is installed (BRD-139).</summary>
public enum SchedulerHelperStatus
{
    /// <summary>This platform has no helper mechanism implemented.</summary>
    NotSupported = 0,

    /// <summary>The mechanism exists but nothing is installed.</summary>
    NotInstalled = 1,

    /// <summary>The helper is installed and registered with the OS.</summary>
    Installed = 2,

    /// <summary>
    /// The helper cannot be installed on this build — the helper executable is not present.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="NotInstalled"/> on purpose. "You have not turned it on" and "this
    /// build cannot turn it on" are different answers, and offering a toggle that silently does
    /// nothing is worse than saying which one it is (REQ-NFR-010's standing rule).
    /// </remarks>
    Unavailable = 3
}

/// <summary>
/// What the app knows about the background scheduler helper on this machine (BRD-139 / REQ-FN-042).
/// </summary>
/// <param name="Status">Installed state.</param>
/// <param name="MechanismName">What is installed, named explicitly — "launchd user agent".</param>
/// <param name="MechanismLocation">Where it is installed, named explicitly — the plist path or task name.</param>
/// <param name="HelperExecutablePath">The executable the mechanism points at, or <see langword="null"/> when none was found.</param>
/// <param name="Reason">Why the status is what it is, when that needs saying.</param>
public sealed record SchedulerHelperState(
    SchedulerHelperStatus Status,
    string MechanismName,
    string MechanismLocation,
    string? HelperExecutablePath = null,
    string? Reason = null)
{
    /// <summary>Gets a value indicating whether schedules can run with the main window closed.</summary>
    public bool RunsWithWindowClosed => Status == SchedulerHelperStatus.Installed;
}

/// <summary>The outcome of an install or uninstall attempt.</summary>
/// <param name="Succeeded">Whether the OS action completed.</param>
/// <param name="Message">What happened, in plain language, for a toast or an alert.</param>
public sealed record SchedulerHelperResult(bool Succeeded, string Message);

/// <summary>
/// Installs and removes the per-user background scheduler helper (BRD-139 / REQ-FN-042, ADR-009).
/// </summary>
/// <remarks>
/// <b>Installing is a user-visible OS action.</b> The UI design requires the toggle to name what it
/// installs and where, and requires uninstall to genuinely remove it — which is why
/// <see cref="GetState"/> reports the mechanism and its location rather than a bare boolean.
/// </remarks>
public interface ISchedulerHelper
{
    /// <summary>Reads the current installed state from the operating system.</summary>
    /// <returns>The state, including the mechanism name and location to show the user.</returns>
    SchedulerHelperState GetState();

    /// <summary>Installs the helper so schedules run with the window closed.</summary>
    /// <param name="preferences">The run conditions to record for the helper.</param>
    /// <returns>Whether it worked, and what to tell the user.</returns>
    Task<SchedulerHelperResult> InstallAsync(SchedulerPreferences preferences);

    /// <summary>Removes the helper completely.</summary>
    /// <returns>Whether it worked, and what to tell the user.</returns>
    Task<SchedulerHelperResult> UninstallAsync();
}
