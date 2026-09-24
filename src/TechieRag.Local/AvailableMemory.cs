using System.Globalization;
using System.Runtime.InteropServices;

namespace TechieRag.Local;

/// <summary>
/// Reads how much memory the process can still take on this platform (REQ-RAG-060 / BRD-99).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><b>Linux and Android:</b> <c>MemAvailable</c> from <c>/proc/meminfo</c>.</description></item>
/// <item><description><b>Windows:</b> <c>GlobalMemoryStatusEx</c> available physical memory.</description></item>
/// <item><description><b>iOS:</b> <c>os_proc_available_memory()</c>, the amount the app can allocate
/// before the system ends it — the number that matters on a phone.</description></item>
/// <item><description><b>Mac Catalyst, macOS and anything else:</b> the memory the .NET runtime reports as available
/// to the process minus what the process already uses; an estimate.</description></item>
/// </list>
/// Returns null when nothing could be read, and the gate then lets the load proceed.
/// </remarks>
internal static class AvailableMemory
{
    /// <summary>Reads the available memory in bytes, or null when it cannot be read.</summary>
    /// <returns>Bytes, or null.</returns>
    public static long? Read()
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            {
                return ReadProcMeminfo();
            }

            if (OperatingSystem.IsWindows())
            {
                return ReadWindows();
            }

            if (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst())
            {
                return (long)OsProcAvailableMemory();
            }

            var info = GC.GetGCMemoryInfo();
            return Math.Max(0, info.TotalAvailableMemoryBytes - Environment.WorkingSet);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Parses the <c>MemAvailable</c> line of <c>/proc/meminfo</c>.</summary>
    /// <param name="meminfo">The file's text.</param>
    /// <returns>Bytes, or null when the line is missing.</returns>
    internal static long? ParseMemAvailable(string meminfo)
    {
        foreach (var line in meminfo.Split('\n'))
        {
            if (!line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kilobytes)
                ? kilobytes * 1024
                : null;
        }

        return null;
    }

    private static long? ReadProcMeminfo() =>
        File.Exists("/proc/meminfo") ? ParseMemAvailable(File.ReadAllText("/proc/meminfo")) : null;

    private static long? ReadWindows()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.AvailablePhysical : null;
    }

    [DllImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "os_proc_available_memory")]
    private static extern nuint OsProcAvailableMemory();

    /// <summary>The Win32 <c>MEMORYSTATUSEX</c> structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
