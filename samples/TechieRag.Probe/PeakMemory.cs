using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TechieRag.Probe;

/// <summary>
/// The probe process's peak memory, measured the way each platform's memory limit counts it
/// (REQ-FN-059, REQ-FN-060).
/// </summary>
/// <remarks>
/// <para>On Windows, Android and Linux <see cref="Process.PeakWorkingSet64"/> is the peak resident set
/// (<c>PeakWorkingSetSize</c>, <c>VmHWM</c>). On iOS and Mac Catalyst .NET reports 0 there (seen
/// 2026-09-25 on macOS 27), and the number the system's memory limit applies to is the physical
/// footprint, not the resident set (which also counts the model file's clean, reclaimable pages). So on
/// Apple platforms this reads <c>ri_lifetime_max_phys_footprint</c> from <c>proc_pid_rusage</c>, the
/// same figure <c>footprint &lt;pid&gt;</c> prints as <c>phys_footprint_peak</c>.</para>
/// </remarks>
public static class PeakMemory
{
    private const int RusageInfoV4 = 4;
    private const int UuidBytes = 16;
    private const int LifetimeMaxPhysFootprintIndex = 28;

    /// <summary>Gets what the peak was measured as on this platform.</summary>
    public static string Method => IsApple ? "peak physical footprint" : "peak working set";

    private static bool IsApple => OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsMacOS();

    /// <summary>Reads the peak so far.</summary>
    /// <returns>Bytes, or 0 when the platform does not report it.</returns>
    public static long ReadBytes()
    {
        if (IsApple)
        {
            return ReadApplePeakFootprint();
        }

        using var process = Process.GetCurrentProcess();
        return process.PeakWorkingSet64;
    }

    private static long ReadApplePeakFootprint()
    {
        // rusage_info_v4: a 16-byte UUID, then uint64 fields; ri_lifetime_max_phys_footprint is the 29th.
        var buffer = new byte[1024];
        try
        {
            if (ProcPidRusage(Environment.ProcessId, RusageInfoV4, buffer) != 0)
            {
                return 0;
            }
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }

        return (long)BitConverter.ToUInt64(buffer, UuidBytes + (LifetimeMaxPhysFootprintIndex * sizeof(ulong)));
    }

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "proc_pid_rusage")]
    private static extern int ProcPidRusage(int pid, int flavor, byte[] buffer);
}
