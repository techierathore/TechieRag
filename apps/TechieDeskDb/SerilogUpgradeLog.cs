using DbUp.Engine.Output;
using Serilog;

namespace TechieDeskDb;

/// <summary>
/// Routes all DbUp engine output through Serilog so every migration outcome
/// lands in the console and the rolling file under logs/ (REQ-NFR-009).
/// </summary>
public sealed class SerilogUpgradeLog : IUpgradeLog
{
    /// <summary>Logs a trace-level message from DbUp.</summary>
    /// <param name="format">Message format string.</param>
    /// <param name="args">Format arguments.</param>
    public void LogTrace(string format, params object[] args) =>
        Log.Verbose(Render(format, args));

    /// <summary>Logs a debug-level message from DbUp.</summary>
    /// <param name="format">Message format string.</param>
    /// <param name="args">Format arguments.</param>
    public void LogDebug(string format, params object[] args) =>
        Log.Debug(Render(format, args));

    /// <summary>Logs an information-level message from DbUp.</summary>
    /// <param name="format">Message format string.</param>
    /// <param name="args">Format arguments.</param>
    public void LogInformation(string format, params object[] args) =>
        Log.Information(Render(format, args));

    /// <summary>Logs a warning-level message from DbUp.</summary>
    /// <param name="format">Message format string.</param>
    /// <param name="args">Format arguments.</param>
    public void LogWarning(string format, params object[] args) =>
        Log.Warning(Render(format, args));

    /// <summary>Logs an error-level message from DbUp.</summary>
    /// <param name="format">Message format string.</param>
    /// <param name="args">Format arguments.</param>
    public void LogError(string format, params object[] args) =>
        Log.Error(Render(format, args));

    /// <summary>Logs an error-level message with exception detail from DbUp.</summary>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="format">Message format string.</param>
    /// <param name="args">Format arguments.</param>
    public void LogError(Exception ex, string format, params object[] args) =>
        Log.Error(ex, Render(format, args));

    private static string Render(string format, object[] args) =>
        args is { Length: > 0 } ? string.Format(format, args) : format;
}
