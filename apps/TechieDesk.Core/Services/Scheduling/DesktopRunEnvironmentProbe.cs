using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TechieDesk.Services.Scheduling;

/// <summary>
/// Reads power source and network name from the operating system (BRD-139 run conditions).
/// </summary>
/// <remarks>
/// <para><b>Shell-outs, not APIs, and deliberately.</b> The managed alternatives are a MAUI Essentials
/// dependency this project keeps out of Core, or a P/Invoke into IOKit. Both would cost more than the
/// question is worth: these two values are read at most once per scheduler tick and every failure
/// path already degrades to "unknown", which never blocks a run.</para>
/// <para><b>Every failure is swallowed on purpose.</b> A probe that throws would take a scheduler tick
/// with it, so an unreadable battery would stop every automation on the machine. Unknown is always a
/// safe answer here because <see cref="RunConditionEvaluator"/> treats it as permission.</para>
/// </remarks>
public sealed class DesktopRunEnvironmentProbe : IRunEnvironmentProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public PowerState GetPowerState()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Create("MACCATALYST")))
        {
            // Windows exposes this through GetSystemPowerStatus, which is a P/Invoke this class
            // deliberately does not carry yet. Unknown means "do not block", which is the correct
            // behaviour for a condition that cannot be tested.
            return PowerState.Unknown;
        }

        var output = RunTool("/usr/bin/pmset", "-g ps");
        if (output is null)
        {
            return PowerState.Unknown;
        }

        if (output.Contains("AC Power", StringComparison.OrdinalIgnoreCase))
        {
            return PowerState.Mains;
        }

        return output.Contains("Battery Power", StringComparison.OrdinalIgnoreCase)
            ? PowerState.Battery
            : PowerState.Unknown;
    }

    /// <inheritdoc />
    public string? GetCurrentNetworkName()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Create("MACCATALYST")))
        {
            return null;
        }

        var output = RunTool("/usr/sbin/networksetup", "-getairportnetwork en0");
        if (output is null)
        {
            return null;
        }

        // "Current Wi-Fi Network: Home". Recent macOS releases withhold this without Location
        // Services, and the tool then prints an error line instead — which parses to null, i.e.
        // "cannot tell", i.e. do not block.
        var marker = output.IndexOf(':');
        if (marker < 0 || !output.Contains("Wi-Fi Network", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = output[(marker + 1)..].Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? RunTool(string fileName, string arguments)
    {
        if (!File.Exists(fileName))
        {
            return null;
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
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
