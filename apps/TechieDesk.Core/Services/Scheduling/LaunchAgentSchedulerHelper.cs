using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using TechieDesk.Services.Localization;
using TechieDeskDb;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Installs the background scheduler as a per-user <b>launchd agent</b> on macOS
/// (BRD-139 / REQ-FN-042, ADR-009).
/// </summary>
/// <remarks>
/// <para><b>A launchd user agent, and nothing more.</b> Not a daemon (which would be system-wide and
/// need administrator rights), not a login item wrapping the whole app, and not a job server. The
/// agent starts at login, runs the same <see cref="SchedulerService"/> against the same data
/// directory, and holds no state of its own — exactly the "hosting choice, not a second
/// implementation" ADR-009 requires.</para>
/// <para><b>Install refuses rather than pretending.</b> If the helper executable is not present in
/// this build, no plist is written. launchd would accept an agent pointing at a missing binary, the
/// UI would then read "Installed", and no schedule would ever run — a silent failure dressed as a
/// success, which is the one outcome worth writing extra code to avoid.</para>
/// <para><b>Uninstall genuinely removes it.</b> The UI design calls this out explicitly: the agent is
/// booted out of the user's launchd domain <i>and</i> the plist is deleted. Leaving the file behind
/// would reinstall the agent at the next login.</para>
/// </remarks>
public sealed class LaunchAgentSchedulerHelper : ISchedulerHelper
{
    /// <summary>The launchd label the agent is registered under.</summary>
    public const string AgentLabel = "com.techiedesk.scheduler";

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(10);

    private readonly ISchedulerHelperLocator locator;
    private readonly ILogger<LaunchAgentSchedulerHelper> logger;
    private readonly LocalizeText localize;
    private readonly string launchAgentsDirectory;
    private readonly string dataDirectory;

    /// <summary>Initializes the helper.</summary>
    /// <param name="locator">Finds the helper executable.</param>
    /// <param name="configuration">Application configuration; used to resolve the data directory.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    public LaunchAgentSchedulerHelper(
        ISchedulerHelperLocator locator,
        IConfiguration configuration,
        ILogger<LaunchAgentSchedulerHelper> logger,
        LocalizeText localize)
        : this(
            locator,
            logger,
            localize,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "LaunchAgents"),
            DataDirectory.Resolve(configuration[DataDirectory.ConfigKey]))
    {
    }

    /// <summary>Initializes the helper with explicit paths, for tests.</summary>
    /// <param name="locator">Finds the helper executable.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="localize">Resolves a resource key into the reader's language.</param>
    /// <param name="launchAgentsDirectory">Directory the plist is written to.</param>
    /// <param name="dataDirectory">The data directory the helper must open.</param>
    public LaunchAgentSchedulerHelper(
        ISchedulerHelperLocator locator,
        ILogger<LaunchAgentSchedulerHelper> logger,
        LocalizeText localize,
        string launchAgentsDirectory,
        string dataDirectory)
    {
        this.locator = locator;
        this.logger = logger;
        this.localize = localize;
        this.launchAgentsDirectory = launchAgentsDirectory;
        this.dataDirectory = dataDirectory;
    }

    /// <summary>Gets the full path of the agent's property list.</summary>
    public string PlistPath => Path.Combine(launchAgentsDirectory, $"{AgentLabel}.plist");

    /// <inheritdoc />
    public SchedulerHelperState GetState()
    {
        // REQ-UI-055: the mechanism NAME is display text and moves with the language. The launchd
        // LABEL it is registered under is AgentLabel, a separate invariant constant, and the plist
        // path is a real file — neither of those ever sees a resource key.
        var mechanism = localize("SchedulerMechanismLaunchAgent");
        var executable = locator.Locate();

        if (File.Exists(PlistPath))
        {
            return new SchedulerHelperState(
                SchedulerHelperStatus.Installed, mechanism, PlistPath, executable,
                localize("SchedulerHelperInstalledReasonMac"));
        }

        return executable is null
            ? new SchedulerHelperState(
                SchedulerHelperStatus.Unavailable, mechanism, PlistPath, null,
                localize("SchedulerHelperUnavailableReasonMac", locator.ExecutableName))
            : new SchedulerHelperState(
                SchedulerHelperStatus.NotInstalled, mechanism, PlistPath, executable,
                localize("SchedulerHelperNotInstalledReason"));
    }

    /// <inheritdoc />
    public async Task<SchedulerHelperResult> InstallAsync(SchedulerPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var executable = locator.Locate();
        if (executable is null)
        {
            return new SchedulerHelperResult(
                false, localize("SchedulerHelperMissingOnInstallMac", locator.ExecutableName));
        }

        try
        {
            Directory.CreateDirectory(launchAgentsDirectory);
            await File.WriteAllTextAsync(
                PlistPath,
                BuildPlist(executable, dataDirectory, preferences, TryResolveDotnetRoot()))
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not write the launchd agent to {Path}", PlistPath);
            return new SchedulerHelperResult(
                false, localize("SchedulerHelperWriteFailed", PlistPath, exception.Message));
        }

        var bootstrap = RunLaunchctl($"bootstrap gui/{GetUserId()} \"{PlistPath}\"");
        if (!bootstrap.Succeeded)
        {
            // launchctl load is the older spelling and still works where bootstrap is refused, for
            // instance when the agent is already registered from a previous install.
            bootstrap = RunLaunchctl($"load -w \"{PlistPath}\"");
        }

        if (!bootstrap.Succeeded)
        {
            logger.LogWarning(
                "The launchd agent plist was written but launchctl refused it: {Message}", bootstrap.Message);
            return new SchedulerHelperResult(
                false, localize("SchedulerHelperLoadRefused", PlistPath, bootstrap.Message));
        }

        logger.LogInformation("Installed the launchd scheduler agent at {Path}", PlistPath);
        return new SchedulerHelperResult(true, localize("SchedulerHelperInstalledMac", PlistPath));
    }

    /// <inheritdoc />
    public Task<SchedulerHelperResult> UninstallAsync()
    {
        var messages = new List<string>();

        var bootout = RunLaunchctl($"bootout gui/{GetUserId()}/{AgentLabel}");
        if (!bootout.Succeeded)
        {
            RunLaunchctl($"unload -w \"{PlistPath}\"");
        }

        if (File.Exists(PlistPath))
        {
            try
            {
                File.Delete(PlistPath);
                messages.Add(localize("SchedulerHelperRemovedFile", PlistPath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(exception, "Could not delete the launchd agent at {Path}", PlistPath);
                return Task.FromResult(new SchedulerHelperResult(
                    false, localize("SchedulerHelperDeleteFailed", PlistPath, exception.Message)));
            }
        }
        else
        {
            messages.Add(localize("SchedulerHelperNoAgentInstalled"));
        }

        logger.LogInformation("Removed the launchd scheduler agent");
        return Task.FromResult(new SchedulerHelperResult(
            true, localize("SchedulerHelperUninstalledMac", string.Join(' ', messages))));
    }

    /// <summary>
    /// Builds the launchd property list for the agent.
    /// </summary>
    /// <param name="executablePath">The helper executable launchd starts.</param>
    /// <param name="dataDirectory">The data directory the helper must open — the same one the app uses.</param>
    /// <param name="preferences">The run conditions to record.</param>
    /// <param name="dotnetRoot">
    /// A .NET installation root to hand the helper, or <see langword="null"/> to omit it.
    /// </param>
    /// <returns>The property list XML.</returns>
    /// <remarks>
    /// <para>Kept static and pure so its content is assertable without touching launchd.</para>
    /// <para><b><c>DOTNET_ROOT</c> is passed because launchd's environment is nearly empty.</b> This
    /// was found the hard way: a bootstrapped agent started, the apphost searched
    /// <c>/usr/local/share/dotnet</c>, found nothing, and exited 131 — whereupon <c>KeepAlive</c>
    /// respawned it into a loop. The user's shell knows where .NET lives; launchd does not, and does
    /// not read a profile. A self-contained helper ignores the variable, so passing it costs nothing
    /// and is omitted entirely when no root can be resolved.</para>
    /// <para><b>The data directory is passed explicitly.</b> A launchd agent inherits almost nothing
    /// from the user's shell, and a helper that resolved a <i>different</i> directory from the app
    /// would migrate and schedule against a second database while both reported success — the exact
    /// REQ-FN-034 defect class, re-created in a new process.</para>
    /// <para><b><c>KeepAlive</c> is conditional on a crash, not unconditional.</b> An unconditional
    /// KeepAlive turns a helper that exits cleanly on sign-out into a restart loop.</para>
    /// </remarks>
    public static string BuildPlist(
        string executablePath,
        string dataDirectory,
        SchedulerPreferences preferences,
        string? dotnetRoot = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine(
            """<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">""");
        builder.AppendLine("""<plist version="1.0">""");
        builder.AppendLine("<dict>");
        builder.AppendLine("  <key>Label</key>");
        builder.AppendLine($"  <string>{Escape(AgentLabel)}</string>");
        builder.AppendLine("  <key>ProgramArguments</key>");
        builder.AppendLine("  <array>");
        builder.AppendLine($"    <string>{Escape(executablePath)}</string>");
        builder.AppendLine("  </array>");
        builder.AppendLine("  <key>RunAtLoad</key>");
        builder.AppendLine("  <true/>");
        builder.AppendLine("  <key>KeepAlive</key>");
        builder.AppendLine("  <dict>");
        builder.AppendLine("    <key>SuccessfulExit</key>");
        builder.AppendLine("    <false/>");
        builder.AppendLine("  </dict>");
        builder.AppendLine("  <key>ProcessType</key>");
        builder.AppendLine("  <string>Background</string>");
        builder.AppendLine("  <key>LowPriorityIO</key>");
        builder.AppendLine("  <true/>");
        builder.AppendLine("  <key>EnvironmentVariables</key>");
        builder.AppendLine("  <dict>");
        builder.AppendLine($"    <key>{Escape(DataDirectoryEnvironmentVariable)}</key>");
        builder.AppendLine($"    <string>{Escape(dataDirectory)}</string>");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            builder.AppendLine("    <key>DOTNET_ROOT</key>");
            builder.AppendLine($"    <string>{Escape(dotnetRoot)}</string>");
        }

        builder.AppendLine("  </dict>");
        builder.AppendLine("  <key>StandardErrorPath</key>");
        builder.AppendLine(
            $"  <string>{Escape(Path.Combine(dataDirectory, DataDirectory.LogDirectoryName, "scheduler-helper.log"))}</string>");
        builder.AppendLine("</dict>");
        builder.AppendLine("</plist>");
        return builder.ToString();
    }

    /// <summary>
    /// Environment variable the helper reads to find the same data directory the app uses.
    /// </summary>
    /// <remarks>
    /// PascalCase with no separators, per the environment-variable rule in the coding standards.
    /// </remarks>
    public const string DataDirectoryEnvironmentVariable = "TechieDeskDataDirectory";

    private static string Escape(string value) => SecurityElement.Escape(value) ?? value;

    /// <summary>
    /// Resolves a .NET installation root to hand the agent, or <see langword="null"/> when the
    /// running host is self-contained and does not have one.
    /// </summary>
    /// <returns>The root directory, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The runtime directory of a framework-dependent host is
    /// <c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;</c>, so the root is three levels
    /// up and is confirmed by the <c>dotnet</c> muxer sitting in it. A self-contained host's runtime
    /// directory is the application folder, which has no muxer — so this correctly answers null and
    /// the plist omits the key rather than pointing the helper at a directory that is not a .NET
    /// installation.
    /// </remarks>
    private static string? TryResolveDotnetRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "dotnet")))
        {
            return configured;
        }

        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        var candidate = Directory.GetParent(runtimeDirectory.TrimEnd(Path.DirectorySeparatorChar))
            ?.Parent?.Parent;
        return candidate is not null && File.Exists(Path.Combine(candidate.FullName, "dotnet"))
            ? candidate.FullName
            : null;
    }

    private int GetUserId()
    {
        // launchctl's gui/<uid> domain. Environment.UserName would not do: the domain is keyed on the
        // numeric uid, and getting it wrong silently targets nobody's session.
        var result = RunTool("/usr/bin/id", "-u", localize);
        return result.Succeeded && int.TryParse(result.Message.Trim(), out var uid) ? uid : 0;
    }

    private ToolResult RunLaunchctl(string arguments)
    {
        var result = RunTool("/bin/launchctl", arguments, localize);
        logger.LogDebug("launchctl {Arguments} -> {Succeeded}: {Message}",
            arguments, result.Succeeded, result.Message);
        return result;
    }

    private static ToolResult RunTool(string fileName, string arguments, LocalizeText localize)
    {
        if (!File.Exists(fileName))
        {
            return new ToolResult(false, localize("SchedulerToolMissing", fileName));
        }

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
                return new ToolResult(
                    false,
                    localize(
                        "SchedulerToolTimedOut",
                        fileName,
                        ToolTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)));
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
