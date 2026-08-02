using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace TechieDesk.Services.Install;

/// <summary>
/// Reads the host's platform-stable identifier and returns it hashed (REQ-FN-051 clause 1).
/// </summary>
/// <remarks>
/// <para>
/// macOS is the shipped head, so <c>IOPlatformUUID</c> is the value of record: it is issued by the
/// hardware, survives an OS reinstall and a rename, and is the identifier Apple's own licensing
/// guidance points at. It is read by running <c>ioreg</c> rather than by P/Invoking IOKit because
/// the marshalling for the latter is ~60 lines of CoreFoundation for the same string, and every
/// failure mode here is already handled by falling back.
/// </para>
/// <para>
/// <b>Known limitation, stated rather than hidden.</b> If the Catalyst head is ever shipped with the
/// App Sandbox entitlement, spawning <c>ioreg</c> will be denied and this degrades to
/// <see cref="MachineFingerprintSource.MachineName"/>, which a user can change. That is a
/// deliberate degradation — the install identity must keep working (REQ-FN-051 must not gate a
/// launch, BRD-129) — but the anti-clone property weakens to nothing on such a build. The upgrade
/// path is a direct IOKit P/Invoke, which the sandbox permits.
/// </para>
/// <para>
/// The result is cached for the process lifetime. Nothing here touches the OS credential store, so
/// this feature is unaffected by REQ-FN-043 (the missing Mac Catalyst keychain entitlement).
/// </para>
/// </remarks>
public sealed class PlatformMachineFingerprintProvider : IMachineFingerprintProvider
{
    /// <summary>
    /// Application-specific salt mixed into every fingerprint.
    /// </summary>
    /// <remarks>
    /// Without it the hash of a hardware UUID would be the same value in every application that
    /// hashed the same UUID, i.e. a ready-made cross-application tracking key. Salting scopes the
    /// fingerprint to TechieDesk. Changing this constant re-fingerprints every install, so it is
    /// versioned in its own text.
    /// </remarks>
    private const string FingerprintSalt = "TechieDesk.InstallIdentity.v1";

    /// <summary>How long a platform-identifier probe is allowed to take before it is abandoned.</summary>
    private const int ProbeTimeoutMilliseconds = 3000;

    private readonly ILogger<PlatformMachineFingerprintProvider> logger;
    private readonly object gate = new();

    private MachineFingerprint? cached;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformMachineFingerprintProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger used to record which source was reached, once.</param>
    public PlatformMachineFingerprintProvider(ILogger<PlatformMachineFingerprintProvider> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Hashes a raw platform value with the application salt.
    /// </summary>
    /// <param name="rawValue">The raw platform identifier, or any test-supplied stand-in.</param>
    /// <returns>Lower-case hexadecimal SHA-256 of the salted value.</returns>
    /// <remarks>
    /// Public because tests and <see cref="InstallIdentityStore"/> both need the identical function,
    /// and two copies of a hash function is how the two halves silently stop agreeing.
    /// </remarks>
    public static string Hash(string rawValue)
    {
        var bytes = Encoding.UTF8.GetBytes(FingerprintSalt + "|" + rawValue);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <inheritdoc />
    public MachineFingerprint Get()
    {
        lock (gate)
        {
            cached ??= Probe();
            return cached;
        }
    }

    /// <summary>Reads the best identifier this host offers, never throwing.</summary>
    /// <returns>The fingerprint to use for this host.</returns>
    private MachineFingerprint Probe()
    {
        var (rawValue, source) = ReadPlatformIdentifier();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            rawValue = Environment.MachineName;
            source = MachineFingerprintSource.MachineName;
        }

        var isStable = source is MachineFingerprintSource.MacPlatformUuid
            or MachineFingerprintSource.WindowsMachineGuid
            or MachineFingerprintSource.LinuxMachineId;

        if (!isStable)
        {
            logger.LogWarning(
                "No platform-stable machine identifier was available; the install identity falls back "
                + "to the host name ({Source}) and can be changed by the user (REQ-FN-051)", source);
        }
        else
        {
            logger.LogInformation("Install identity is bound to the {Source} machine identifier", source);
        }

        return new MachineFingerprint(Hash(rawValue!), source, isStable);
    }

    /// <summary>Dispatches to the per-platform identifier read.</summary>
    /// <returns>The raw identifier and its source; a null value when none was obtainable.</returns>
    private (string? RawValue, MachineFingerprintSource Source) ReadPlatformIdentifier()
    {
        try
        {
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            {
                return (ReadMacPlatformUuid(), MachineFingerprintSource.MacPlatformUuid);
            }

            if (OperatingSystem.IsWindows())
            {
                return (ReadWindowsMachineGuid(), MachineFingerprintSource.WindowsMachineGuid);
            }

            return (ReadLinuxMachineId(), MachineFingerprintSource.LinuxMachineId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading the platform machine identifier failed; falling back");
            return (null, MachineFingerprintSource.None);
        }
    }

    /// <summary>Reads <c>IOPlatformUUID</c> out of <c>ioreg</c> output.</summary>
    /// <returns>The UUID, or null when it could not be read.</returns>
    private string? ReadMacPlatformUuid()
    {
        var output = RunProbe("/usr/sbin/ioreg", "-rd1 -c IOPlatformExpertDevice");
        if (output is null)
        {
            return null;
        }

        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains("IOPlatformUUID", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim().Trim('"').Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Reads the Windows cryptography machine GUID.</summary>
    /// <returns>The GUID, or null when it could not be read.</returns>
    /// <remarks>
    /// Read through <c>reg query</c> rather than <c>Microsoft.Win32.Registry</c> so this file stays
    /// in the plain <c>net10.0</c> Core assembly the test project references, with no
    /// Windows-targeted package reference. The Windows head has no <c>Platforms/Windows</c> sources
    /// yet (REQ-FN-035), so this path is untested against a real Windows host.
    /// </remarks>
    private string? ReadWindowsMachineGuid()
    {
        var output = RunProbe(
            "reg", @"query HKLM\SOFTWARE\Microsoft\Cryptography /v MachineGuid");
        if (output is null)
        {
            return null;
        }

        foreach (var line in output.Split('\n'))
        {
            var marker = line.IndexOf("REG_SZ", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
            {
                continue;
            }

            var value = line[(marker + "REG_SZ".Length)..].Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Reads the systemd/D-Bus machine id.</summary>
    /// <returns>The machine id, or null when neither file exists.</returns>
    private static string? ReadLinuxMachineId()
    {
        string[] candidates = ["/etc/machine-id", "/var/lib/dbus/machine-id"];
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var value = File.ReadAllText(candidate).Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Runs a short read-only command and captures its standard output.</summary>
    /// <param name="fileName">The executable to run.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>Standard output, or null when the command could not be run or timed out.</returns>
    /// <remarks>
    /// Bounded by <see cref="ProbeTimeoutMilliseconds"/> because this runs on the launch path and a
    /// hung probe would be indistinguishable from an app that will not start.
    /// </remarks>
    private string? RunProbe(string fileName, string arguments)
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
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(ProbeTimeoutMilliseconds))
            {
                logger.LogWarning("{FileName} did not complete within the probe timeout", fileName);
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not run {FileName} to read the machine identifier", fileName);
            return null;
        }
    }
}
