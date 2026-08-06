namespace TechieDeskDb;

/// <summary>
/// The per-user storage convention an operating system family imposes (REQ-FN-037, BRD-130).
/// </summary>
/// <remarks>
/// Kept as an explicit enum rather than an <c>OperatingSystem.IsX()</c> call buried inside the
/// resolver so both platform shapes are assertable from a single-platform test run. A test on macOS
/// can prove the Windows layout, and vice versa, without any conditional compilation.
/// </remarks>
public enum DataDirectoryPlatform
{
    /// <summary>macOS: <c>~/Library/Application Support/&lt;application&gt;</c>.</summary>
    MacOS,

    /// <summary>Windows: <c>%LOCALAPPDATA%\&lt;application&gt;</c>.</summary>
    Windows,

    /// <summary>Linux and other Unix hosts: <c>$HOME/.local/share/&lt;application&gt;</c> (XDG).</summary>
    Unix
}
