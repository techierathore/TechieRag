using System.Text.RegularExpressions;
using Xunit;

namespace TechieRag.Tests.Documentation;

/// <summary>
/// REQ-FN-061 / BRD-109: the published documentation describes <c>TechieRag.Local</c> and never
/// claims "offline" without saying the model downloads once first.
/// </summary>
/// <remarks>
/// Reads the files from the repository root (found by walking up to <c>TechieRag.slnx</c>), so a
/// later edit that drops the section or reintroduces a bare offline claim fails the default run.
/// </remarks>
public sealed class LocalModelDocsTests
{
    /// <summary>
    /// The reader-facing pages the offline wording was corrected on: README, the guides, the BRD, the
    /// competitor analysis, the AI reference and the packaged copies shipped inside the NuGet package.
    /// Internal records (checklists, feedback logs, briefs, proposals) quote history and are not pages
    /// a reader is told to follow.
    /// </summary>
    private static readonly string[] ReaderPages =
    [
        "README.md",
        "docs/TechieRag-UsageGuide.md",
        "docs/TechieRag-UserGuide.md",
        "docs/TechieRag.Embedded-UserGuide.md",
        "docs/EMBEDDED-PACKAGE-GUIDE.md",
        "docs/TechieRag-BRD.md",
        "docs/TechieRag-CompetitorAnalysis.md",
        "docs/TechieRag-AI-Reference.md",
        "src/TechieRag/build/content/TechieRag-AI-Reference.md",
        "src/TechieRag/build/content/techierag-claude-command.md",
        "src/TechieRag/build/content/techierag-opencode-command.md"
    ];

    private static readonly Regex OfflineClaim = new(@"\boffline\b|air-gapped", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DownloadsOnce = new(
        @"downloads? once|downloaded once|one-time download|after one download|once the model (is|has) downloaded|once downloaded",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Acceptance of REQ-FN-061, first half: when a reader opens the UsageGuide, a section headed for
    /// <c>TechieRag.Local</c> exists and says how to use it (<c>UseLocalLlm</c>).
    /// </summary>
    [Fact(DisplayName = "REQ-FN-061 UsageGuideHasLocalModelSection")]
    public void UsageGuideHasLocalModelSection()
    {
        var guide = ReadRepoFile("docs/TechieRag-UsageGuide.md");

        var heading = guide.Split('\n').FirstOrDefault(line =>
            line.StartsWith('#') && line.Contains("TechieRag.Local", StringComparison.Ordinal));

        Assert.NotNull(heading);
        Assert.Contains("UseLocalLlm", guide, StringComparison.Ordinal);
    }

    /// <summary>
    /// Acceptance of REQ-FN-061, second half: no reader-facing page that claims offline (or
    /// air-gapped) operation leaves out that the model downloads once first.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-061 NoPageClaimsOfflineWithoutDownloadingOnce")]
    public void NoPageClaimsOfflineWithoutDownloadingOnce()
    {
        var bare = ReaderPages
            .Where(page => File.Exists(Path.Combine(RepoRoot(), page)))
            .Where(page =>
            {
                var text = ReadRepoFile(page);
                return OfflineClaim.IsMatch(text) && !DownloadsOnce.IsMatch(text);
            })
            .ToList();

        Assert.True(bare.Count == 0, "These pages claim offline use without saying the model downloads once first: " + string.Join(", ", bare));
        Assert.Contains(ReaderPages, page => OfflineClaim.IsMatch(ReadRepoFile(page)));
    }

    /// <summary>Reads a repository file with line endings normalised.</summary>
    /// <param name="relativePath">Path from the repository root.</param>
    /// <returns>The file's text.</returns>
    internal static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    /// <summary>Finds the repository root by walking up from the test output to <c>TechieRag.slnx</c>.</summary>
    /// <returns>The repository root.</returns>
    internal static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root (TechieRag.slnx) not found above the test output.");
    }
}
