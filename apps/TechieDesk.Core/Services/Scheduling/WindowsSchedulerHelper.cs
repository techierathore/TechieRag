using System.Diagnostics;
using TechieDesk.Services.Localization;
using TechieDeskDb;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Installs the background scheduler as a <b>per-user logon task</b> on Windows
/// (BRD-139 / REQ-FN-042, ADR-009).
/// </summary>
/// <remarks>
/// <para><b>A per-user scheduled task, not a Windows service.</b> The UI design names the Windows
/// mechanism "Windows service (per-user)"; the implementable form of that phrase is a Task Scheduler
/// logon task running as the signed-in user. A true service runs in session 0 with no access to the
/// user's profile — which is where the per-user data directory, the Credential Manager entries and
/// the user's whole install live — and registering one needs elevation. A logon task needs none, and
/// BRD-139's "installed and removed from the app UI" is only true of the mechanism that does not
/// demand an administrator prompt.</para>
/// <para><b>⚠ Never executed.</b> This project's only development and smoke host is macOS; this class
/// has been compiled but no code path in it has been run. Treat the schtasks argument strings as
/// unverified.</para>
/// </remarks>
public sealed class WindowsSchedulerHelper : ISchedulerHelper
{
    /// <summary>The Task Scheduler task name the helper is registered under.</summary>
    public const string TaskName = "TechieDeskScheduler";

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(15);

    private readonly ISchedulerHelperLocator locator;
    private readonly ILogger<WindowsSchedulerHelper> logger;
    private readonly LocalizeText localize;
    private readonly string dataDirectory;

    /// <summary>Initializes the helper.</summary>
    /// <param name="locator">Finds the helper executable.</param>
    /// <param name="configuration">Application configuration; used to resolve the data directory.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    public WindowsSchedulerHelper(
        ISchedulerHelperLocator locator,
        IConfiguration configuration,
        ILogger<WindowsSchedulerHelper> logger,
        LocalizeText localize)
    {
        this.locator = locator;
        this.logger = logger;
        this.localize = localize;
        dataDirectory = DataDirectory.Resolve(configuration[DataDirectory.ConfigKey]);
    }

    /// <inheritdoc />
    public SchedulerHelperState GetState()
    {
        // REQ-UI-055: the mechanism NAME is display text and moves with the language. TaskName is the
        // invariant identifier schtasks is called with, and the location quotes it verbatim.
        var mechanism = localize("SchedulerMechanismWindowsLogonTask");
        var location = $@"Task Scheduler \{TaskName}";
        var executable = locator.Locate();

        if (executable is null)
        {
            return new SchedulerHelperState(
                SchedulerHelperStatus.Unavailable, mechanism, location, null,
                localize("SchedulerHelperUnavailableReasonWindows", locator.ExecutableName));
        }

        var query = RunTool("schtasks.exe", $"/Query /TN \"{TaskName}\"", localize);
        return query.Succeeded
            ? new SchedulerHelperState(
                SchedulerHelperStatus.Installed, mechanism, location, executable,
                localize("SchedulerHelperInstalledReasonWindows"))
            : new SchedulerHelperState(
                SchedulerHelperStatus.NotInstalled, mechanism, location, executable,
                localize("SchedulerHelperNotInstalledReason"));
    }

    /// <inheritdoc />
    public Task<SchedulerHelperResult> InstallAsync(SchedulerPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var executable = locator.Locate();
        if (executable is null)
        {
            return Task.FromResult(new SchedulerHelperResult(
                false, localize("SchedulerHelperMissingOnInstallWindows", locator.ExecutableName)));
        }

        // /RL LIMITED keeps the task at the user's own privilege level: this runs the user's own app
        // against the user's own files and has no business asking for more.
        var arguments =
            $"/Create /TN \"{TaskName}\" /TR \"\\\"{executable}\\\"\" /SC ONLOGON /RL LIMITED /F";
        var create = RunTool("schtasks.exe", arguments, localize);
        if (!create.Succeeded)
        {
            logger.LogWarning("schtasks refused to create the logon task: {Message}", create.Message);
            return Task.FromResult(new SchedulerHelperResult(
                false, localize("SchedulerHelperTaskCreateRefused", create.Message)));
        }

        // The task inherits the user's environment, so the data directory is pinned the same way the
        // launchd agent pins it — one directory, never two (REQ-FN-034).
        Environment.SetEnvironmentVariable(
            LaunchAgentSchedulerHelper.DataDirectoryEnvironmentVariable,
            dataDirectory,
            EnvironmentVariableTarget.User);

        logger.LogInformation("Installed the Windows logon task {TaskName}", TaskName);
        return Task.FromResult(new SchedulerHelperResult(
            true, localize("SchedulerHelperInstalledWindows", TaskName)));
    }

    /// <inheritdoc />
    public Task<SchedulerHelperResult> UninstallAsync()
    {
        var delete = RunTool("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F", localize);
        if (!delete.Succeeded && !delete.Message.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("schtasks refused to delete the logon task: {Message}", delete.Message);
            return Task.FromResult(new SchedulerHelperResult(
                false, localize("SchedulerHelperTaskDeleteRefused", delete.Message)));
        }

        Environment.SetEnvironmentVariable(
            LaunchAgentSchedulerHelper.DataDirectoryEnvironmentVariable, null, EnvironmentVariableTarget.User);

        logger.LogInformation("Removed the Windows logon task {TaskName}", TaskName);
        return Task.FromResult(new SchedulerHelperResult(
            true, localize("SchedulerHelperUninstalledWindows", TaskName)));
    }

    private static ToolResult RunTool(string fileName, string arguments, LocalizeText localize)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return new ToolResult(false, localize("SchedulerToolNotStarted", fileName));
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)ToolTimeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return new ToolResult(false, localize("SchedulerToolTimedOutSoon", fileName));
            }

            var message = string.IsNullOrWhiteSpace(error) ? output : error;
            return new ToolResult(process.ExitCode == 0, message.Trim());
        }
        catch (Exception exception)
        {
            return new ToolResult(false, exception.Message);
        }
    }

    private readonly record struct ToolResult(bool Succeeded, string Message);
}
