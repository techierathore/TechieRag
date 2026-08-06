using System.Diagnostics;

namespace TechieDesk.Services.Install;

/// <summary>
/// Default <see cref="IProcessLiveness"/> over the operating system's process table.
/// </summary>
public sealed class SystemProcessLiveness : IProcessLiveness
{
    /// <summary>How far a recorded and an observed start time may differ and still be the same process.</summary>
    /// <remarks>
    /// The recorded value is written by the owner from its own <see cref="Process.StartTime"/>, but
    /// it round-trips through JSON at second-ish precision, so an exact comparison would report
    /// every live owner as dead and disable the guard entirely.
    /// </remarks>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    /// <summary>Gets a shared instance.</summary>
    public static SystemProcessLiveness Instance { get; } = new();

    /// <inheritdoc />
    public bool IsAlive(int processId, DateTimeOffset? startedAtUtc)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            if (startedAtUtc is null)
            {
                return true;
            }

            var observed = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            return (observed - startedAtUtc.Value).Duration() <= StartTimeTolerance;
        }
        catch (ArgumentException)
        {
            // No process with that id — the ordinary "the owner crashed" answer.
            return false;
        }
        catch (InvalidOperationException)
        {
            // The process exited between the lookup and the read.
            return false;
        }
        catch (Exception ex) when (ex is NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // The platform would not answer. Treat an unknown owner as gone: a guard that cannot
            // read the process table must not be able to keep the app shut (degrade, never lock).
            return false;
        }
    }
}
