using System.ComponentModel;
using System.Diagnostics;
using TechieDeskDb;

namespace TechieDesk.Services.Storage;

/// <summary>
/// Reveals a path in the host's file manager — Finder on macOS, File Explorer on Windows
/// (REQ-UI-041, BRD-133).
/// </summary>
/// <remarks>
/// <para>
/// The command is built by <see cref="CommandFor"/>, a pure function of platform and path, so the
/// macOS AND Windows forms are both assertable from one test host. Only <see cref="Reveal"/> starts
/// a process.
/// </para>
/// <para>
/// Nothing here shells out blindly. A path that does not exist is refused before any process is
/// started, and a launcher that is missing or refused by the OS is reported back as a failure
/// naming the command that was attempted. Reveal is a convenience — it must never throw into the
/// UI, and it must never claim to have opened a window it did not open.
/// </para>
/// </remarks>
public static class FileManagerReveal
{
    /// <summary>Resource key for a reveal asked for with no path at all.</summary>
    public const string NoPathKey = "RevealNoPath";

    /// <summary>Resource key for a path that does not exist. Takes the path.</summary>
    public const string NothingThereKey = "RevealNothingThere";

    /// <summary>Resource key for an OS that returned no process. Takes the launcher name.</summary>
    public const string NotStartedKey = "RevealNotStarted";

    /// <summary>Resource key for a successful reveal. Takes the path.</summary>
    public const string RevealedKey = "RevealSucceeded";

    /// <summary>Resource key for a launcher that threw. Takes the launcher, the path and the error.</summary>
    public const string LauncherFailedKey = "RevealLauncherFailed";

    /// <summary>
    /// Builds the file-manager command that selects a path, without running anything.
    /// </summary>
    /// <param name="platform">The storage convention of the host.</param>
    /// <param name="path">Absolute path to reveal.</param>
    /// <returns>The executable and its arguments.</returns>
    /// <remarks>
    /// macOS uses <c>open -R</c>, which selects the item inside its containing folder rather than
    /// opening it — revealing a database file by opening it would launch whatever is registered for
    /// the extension. Windows uses <c>explorer /select,</c> for the same reason. Neither is a shell
    /// string: the path is passed as its own argument, so spaces in
    /// <c>~/Library/Application Support</c> need no quoting and nothing in the path can be
    /// interpreted as a further argument. Linux has no portable "select" verb, so the containing
    /// directory is opened with <c>xdg-open</c>.
    /// </remarks>
    public static FileManagerRevealCommand CommandFor(DataDirectoryPlatform platform, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        return platform switch
        {
            DataDirectoryPlatform.MacOS => new FileManagerRevealCommand("open", ["-R", fullPath]),
            DataDirectoryPlatform.Windows => new FileManagerRevealCommand("explorer.exe", [$"/select,{fullPath}"]),
            _ => new FileManagerRevealCommand("xdg-open", [ContainingDirectory(fullPath)])
        };
    }

    /// <summary>
    /// Reveals a path in the host's file manager.
    /// </summary>
    /// <param name="path">Absolute path to a file or directory.</param>
    /// <returns>Whether the file manager was launched, and a message describing either outcome.</returns>
    public static FileManagerRevealOutcome Reveal(string path)
        => Reveal(DataDirectory.CurrentPlatform, path);

    /// <summary>
    /// Reveals a path in the file manager of an explicitly named platform.
    /// </summary>
    /// <param name="platform">The storage convention of the host.</param>
    /// <param name="path">Absolute path to a file or directory.</param>
    /// <returns>Whether the file manager was launched, and a message describing either outcome.</returns>
    public static FileManagerRevealOutcome Reveal(DataDirectoryPlatform platform, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new FileManagerRevealOutcome(false, NoPathKey, []);
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return new FileManagerRevealOutcome(false, NothingThereKey, [fullPath]);
        }

        var command = CommandFor(platform, fullPath);
        var startInfo = new ProcessStartInfo(command.FileName) { UseShellExecute = false };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            return process is null
                ? new FileManagerRevealOutcome(false, NotStartedKey, [command.FileName])
                : new FileManagerRevealOutcome(true, RevealedKey, [fullPath]);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return new FileManagerRevealOutcome(
                false, LauncherFailedKey, [command.FileName, fullPath, exception.Message]);
        }
    }

    /// <summary>Gets the directory containing a path, falling back to the path itself.</summary>
    /// <param name="fullPath">An absolute path.</param>
    /// <returns>The containing directory, or the path when it has no parent.</returns>
    private static string ContainingDirectory(string fullPath)
    {
        var parent = Path.GetDirectoryName(fullPath);
        return string.IsNullOrEmpty(parent) ? fullPath : parent;
    }
}
