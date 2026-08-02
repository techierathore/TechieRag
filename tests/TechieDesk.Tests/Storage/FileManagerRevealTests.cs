using TechieDesk.Services.Storage;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Storage;

/// <summary>
/// REQ-UI-041 (BRD-133): the data/storage surface reveals the data directory in Finder or File
/// Explorer. The command is a pure function of platform and path, so both platforms are assertable
/// from one host — which is the only way the Windows form is checked at all from a Mac-only build.
/// </summary>
public sealed class FileManagerRevealTests : IDisposable
{
    private readonly string sandbox;

    /// <summary>Creates a sandbox directory that genuinely exists on disk.</summary>
    public FileManagerRevealTests()
    {
        sandbox = Path.Combine(Path.GetTempPath(), "techiedesk-reveal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
    }

    /// <summary>Removes the sandbox.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    /// <summary>
    /// macOS selects the item in Finder rather than opening it — revealing a .db by opening it
    /// would launch whatever application claims the extension.
    /// </summary>
    [Fact]
    public void MacCommandSelectsInFinderWithoutOpeningTheFile()
    {
        var command = FileManagerReveal.CommandFor(DataDirectoryPlatform.MacOS, sandbox);

        Assert.Equal("open", command.FileName);
        Assert.Equal(["-R", Path.GetFullPath(sandbox)], command.Arguments);
    }

    /// <summary>Windows selects the item in File Explorer, for the same reason.</summary>
    [Fact]
    public void WindowsCommandSelectsInFileExplorer()
    {
        var command = FileManagerReveal.CommandFor(DataDirectoryPlatform.Windows, sandbox);

        Assert.Equal("explorer.exe", command.FileName);
        Assert.Equal([$"/select,{Path.GetFullPath(sandbox)}"], command.Arguments);
    }

    /// <summary>Linux has no portable select verb, so the containing directory is opened.</summary>
    [Fact]
    public void UnixCommandOpensTheContainingDirectory()
    {
        var file = Path.Combine(sandbox, "techiedesk.db");
        File.WriteAllText(file, "x");

        var command = FileManagerReveal.CommandFor(DataDirectoryPlatform.Unix, file);

        Assert.Equal("xdg-open", command.FileName);
        Assert.Equal([Path.GetFullPath(sandbox)], command.Arguments);
    }

    /// <summary>
    /// The real data directory is <c>~/Library/Application Support/TechieDesk</c> — a path with a
    /// space in it. It is passed as ONE argument, so nothing needs quoting and nothing in the path
    /// can be re-interpreted as a further argument.
    /// </summary>
    [Fact]
    public void PathWithSpacesStaysOneArgument()
    {
        var spaced = Path.Combine(sandbox, "Application Support", "TechieDesk");
        Directory.CreateDirectory(spaced);

        var command = FileManagerReveal.CommandFor(DataDirectoryPlatform.MacOS, spaced);

        Assert.Equal(2, command.Arguments.Count);
        Assert.Equal(Path.GetFullPath(spaced), command.Arguments[1]);
        Assert.DoesNotContain('"', command.Arguments[1]);
    }

    /// <summary>Nothing is launched for a path that does not exist, and the caller is told why.</summary>
    [Fact]
    public void RefusesToRevealAPathThatDoesNotExist()
    {
        var missing = Path.Combine(sandbox, "not-here");

        var outcome = FileManagerReveal.Reveal(DataDirectoryPlatform.MacOS, missing);

        Assert.False(outcome.Launched);
        Assert.Equal(FileManagerReveal.NothingThereKey, outcome.MessageKey);
        Assert.Contains(missing, Assert.Single(outcome.Arguments) as string, StringComparison.Ordinal);
    }

    /// <summary>A blank path is refused rather than resolved against the working directory.</summary>
    [Fact]
    public void RefusesABlankPath()
    {
        var outcome = FileManagerReveal.Reveal(DataDirectoryPlatform.MacOS, "   ");

        Assert.False(outcome.Launched);
        Assert.Equal(FileManagerReveal.NoPathKey, outcome.MessageKey);
    }

    /// <summary>
    /// A host with no such launcher reports the failure naming the command it tried, instead of
    /// throwing into the UI or claiming to have opened a window it did not open.
    /// </summary>
    [Fact]
    public void ReportsTheCommandItCouldNotRun()
    {
        // Unix is the branch whose launcher (xdg-open) is absent on this macOS host, which makes
        // this the real "the launcher is missing" path rather than a simulated one.
        var outcome = FileManagerReveal.Reveal(DataDirectoryPlatform.Unix, sandbox);

        if (outcome.Launched)
        {
            // A host that really does have xdg-open is not a failure; the honest-failure path is
            // only assertable where the launcher is genuinely absent.
            return;
        }

        Assert.Contains("xdg-open", (string)outcome.Arguments[0], StringComparison.Ordinal);
    }
}
