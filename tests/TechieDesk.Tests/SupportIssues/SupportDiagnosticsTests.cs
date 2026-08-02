using TechieDesk.Services.Support;
using Xunit;

namespace TechieDesk.Tests.SupportIssues;

/// <summary>
/// REQ-UI-032: the optional diagnostics block — app version, OS, and the last 200 log lines,
/// nothing else.
/// </summary>
public sealed class SupportDiagnosticsTests : IDisposable
{
    private readonly string sandbox;

    /// <summary>Creates a private log directory for one test.</summary>
    public SupportDiagnosticsTests()
    {
        sandbox = Path.Combine(Path.GetTempPath(), "techiedesk-diag-tests", Guid.NewGuid().ToString("N"));
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

    /// <summary>The tail is capped at 200 lines and takes them from the END of the file.</summary>
    [Fact]
    public void OnlyTheLastTwoHundredLinesAreRead()
    {
        var lines = Enumerable.Range(1, 500).Select(number => $"line {number}");
        File.WriteAllLines(Path.Combine(sandbox, "techiedesk-20260727.log"), lines);

        var tail = SupportDiagnostics.ReadRecentLogLines(sandbox);

        Assert.Equal(SupportDiagnostics.MaxLogLines, tail.Count);
        Assert.Equal("line 301", tail[0]);
        Assert.Equal("line 500", tail[^1]);
    }

    /// <summary>A file shorter than the cap is returned whole.</summary>
    [Fact]
    public void ShortLogIsReturnedWhole()
    {
        File.WriteAllLines(Path.Combine(sandbox, "techiedesk.log"), ["a", "b", "c"]);

        Assert.Equal(["a", "b", "c"], SupportDiagnostics.ReadRecentLogLines(sandbox));
    }

    /// <summary>With several rolling files, the newest one is the one read.</summary>
    [Fact]
    public void NewestLogFileWins()
    {
        var older = Path.Combine(sandbox, "techiedesk-20260701.log");
        var newer = Path.Combine(sandbox, "techiedesk-20260727.log");
        File.WriteAllLines(older, ["old"]);
        File.WriteAllLines(newer, ["new"]);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-20));
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

        Assert.Equal(["new"], SupportDiagnostics.ReadRecentLogLines(sandbox));
    }

    /// <summary>
    /// A log directory that does not exist yields nothing rather than throwing — submitting an issue
    /// must not fail because the app has not written a log yet.
    /// </summary>
    [Fact]
    public void MissingLogDirectoryYieldsNothing()
    {
        Assert.Empty(SupportDiagnostics.ReadRecentLogLines(Path.Combine(sandbox, "nope")));
    }

    /// <summary>Files that are not logs are ignored, so no database or config is ever read.</summary>
    [Fact]
    public void NonLogFilesAreIgnored()
    {
        File.WriteAllText(Path.Combine(sandbox, "techiedesk.db"), "SECRET DOCUMENT CONTENT");

        Assert.Empty(SupportDiagnostics.ReadRecentLogLines(sandbox));
    }

    /// <summary>The block names the version and the OS the screen promised it would send.</summary>
    [Fact]
    public void BlockCarriesVersionAndOperatingSystem()
    {
        var block = SupportDiagnostics.Build("1.4.0", "macOS 15.5", ["[INF] started"]);

        Assert.Contains("App version: 1.4.0", block);
        Assert.Contains("Operating system: macOS 15.5", block);
        Assert.Contains("[INF] started", block);
    }

    /// <summary>With no log available the block says so instead of implying one was attached.</summary>
    [Fact]
    public void BlockSaysSoWhenThereAreNoLogLines()
    {
        var block = SupportDiagnostics.Build("1.4.0", "macOS 15.5", Array.Empty<string>());

        Assert.Contains("none available", block);
    }
}
