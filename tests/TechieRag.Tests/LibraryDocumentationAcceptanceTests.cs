using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TechieRag.Tests;

/// <summary>
/// Acceptance checks for the documentation and repository-layout rows of the phase-2 checklist
/// (REQ-FN-004, REQ-FN-006, REQ-FN-054, REQ-RAG-046, REQ-RAG-056), read straight from the files in
/// the repository root (the folder holding <c>TechieRag.slnx</c>).
/// </summary>
public sealed class LibraryDocumentationAcceptanceTests
{
    /// <summary>
    /// REQ-FN-004: a reader opening the README's Installation section finds, as the first command,
    /// <c>dotnet add package TechieRag</c> with no source argument, and the text before it names
    /// nuget.org and says no token is needed; the GitHub Packages (PAT) route comes only later.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-004 ReadmeInstallLeadsWithPublicFeed")]
    public void ReadmeInstallLeadsWithPublicFeed()
    {
        var readme = LibraryRepoFiles.Read("README.md");
        var section = Regex.Match(readme, @"^## Installation\n(.*?)(?=^## )", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(section), "README has no '## Installation' section.");

        var firstBlock = Regex.Match(section, @"```[a-z]*\n(.*?)```", RegexOptions.Singleline);
        Assert.True(firstBlock.Success, "The Installation section has no code block.");
        var firstCommand = firstBlock.Groups[1].Value.Split('\n')
            .Select(line => line.Trim())
            .First(line => line.Length > 0 && !line.StartsWith('#'));
        Assert.Equal("dotnet add package TechieRag", firstCommand);

        var lead = section[..firstBlock.Index];
        Assert.Contains("nuget.org", lead, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no token", lead, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PAT", lead, StringComparison.Ordinal);
        Assert.True(section.IndexOf("GitHub Packages", StringComparison.Ordinal) > firstBlock.Index, "The GitHub Packages route must come after the public-feed command.");
    }

    /// <summary>
    /// REQ-FN-006: after the split the repository holds the library only — no <c>apps/</c>, no
    /// TechieDesk test project or docs, the solution lists only projects under <c>src/</c> and the
    /// library's <c>tests/TechieRag.*</c> projects, and no project references anything outside them.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-006 RepositoryHoldsOnlyTheLibrary")]
    public void RepositoryHoldsOnlyTheLibrary()
    {
        var root = LibraryRepoFiles.Root();
        Assert.False(Directory.Exists(Path.Combine(root, "apps")), "apps/ still exists.");
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "tests"), "TechieDesk*"));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "docs"), "TechieDesk-*"));

        var projects = XDocument.Load(Path.Combine(root, "TechieRag.slnx")).Descendants("Project")
            .Select(p => (string)p.Attribute("Path")!)
            .ToList();
        Assert.NotEmpty(projects);
        Assert.All(projects, path => Assert.Matches(@"^(src/TechieRag[^/]*/|tests/TechieRag\.[^/]*Tests/)[^/]+\.csproj$", path));
        Assert.Contains("tests/TechieRag.Tests/TechieRag.Tests.csproj", projects);

        var strayReferences = projects
            .SelectMany(path =>
            {
                var projectDirectory = Path.GetDirectoryName(Path.Combine(root, path))!;
                return XDocument.Load(Path.Combine(root, path)).Descendants("ProjectReference")
                    .Select(reference => Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(projectDirectory, ((string)reference.Attribute("Include")!).Replace('\\', '/')))).Replace('\\', '/'))
                    .Where(target => !target.StartsWith("src/", StringComparison.Ordinal) && !target.StartsWith("tests/", StringComparison.Ordinal))
                    .Select(target => $"{path} -> {target}");
            })
            .ToList();
        Assert.True(strayReferences.Count == 0, "Project references outside src/ and tests/: " + string.Join("; ", strayReferences));
    }

    /// <summary>
    /// REQ-FN-006: the Sevak application, checked out beside this repository, consumes the released
    /// <c>TechieRag</c> and <c>TechieRag.Embedded</c> 1.0.7 packages and has no ProjectReference into
    /// this repository. Skipped with a reason on a host without the sibling Sevak checkout.
    /// </summary>
    [SiblingSevakFact(DisplayName = "REQ-FN-006 SevakConsumesReleasedPackages")]
    public void SevakConsumesReleasedPackages()
    {
        var sevak = SiblingSevakFactAttribute.SevakRoot()!;
        var props = XDocument.Load(Path.Combine(sevak, "Directory.Packages.props"));
        string? Pinned(string id) => props.Descendants("PackageVersion").FirstOrDefault(p => (string?)p.Attribute("Include") == id)?.Attribute("Version")?.Value;
        Assert.Equal("1.0.7", Pinned("TechieRag"));
        Assert.Equal("1.0.7", Pinned("TechieRag.Embedded"));

        var separator = Path.DirectorySeparatorChar;
        var intoLibrary = Directory.GetFiles(sevak, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}bin{separator}") && !file.Contains($"{separator}obj{separator}"))
            .SelectMany(file => XDocument.Load(file).Descendants("ProjectReference")
                .Select(reference => (string)reference.Attribute("Include")!)
                .Where(include => Regex.IsMatch(include, @"TechieRag(\.Embedded|\.Agents|\.Local|\.Telemetry)?\.csproj$"))
                .Select(include => $"{file}: {include}"))
            .ToList();
        Assert.True(intoLibrary.Count == 0, "Sevak references library projects: " + string.Join("; ", intoLibrary));
    }

    /// <summary>
    /// REQ-FN-054: every cell of the UsageGuide's platform support matrix (each package × Windows,
    /// macOS / Mac Catalyst, Android, iOS) reads <c>supported</c>, <c>not supported</c>, or
    /// <c>tested (device…, yyyy-MM-dd)</c> naming a device and a date; footnote markers are ignored.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-054 PlatformMatrixCellsAreClassified")]
    public void PlatformMatrixCellsAreClassified()
    {
        var guide = LibraryRepoFiles.Read("docs/TechieRag-UsageGuide.md");
        var section = Regex.Match(guide, @"^### Platform support matrix[^\n]*\n(.*?)(?=^### )", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
        var rows = section.Split('\n').Where(line => line.StartsWith('|')).ToList();
        Assert.True(rows.Count >= 3, "The platform support matrix has no table.");

        Assert.Equal(["Package", "Windows", "macOS / Mac Catalyst", "Android", "iOS"], Cells(rows[0]));

        var packages = rows.Skip(2).Select(Cells).ToList();
        Assert.Equal(["`TechieRag`", "`TechieRag.Embedded`", "`TechieRag.Agents`", "`TechieRag.Local`"], packages.Select(r => r[0]));
        Assert.All(packages, row => Assert.Equal(5, row.Count));

        var cellPattern = new Regex(@"^(supported|not supported|tested \(.+, \d{4}-\d{2}-\d{2}\))$");
        var bad = packages
            .SelectMany(row => row.Skip(1)
                .Select(cell => Regex.Replace(cell, @"\s*[¹²³⁴⁵⁶⁷⁸⁹]+$", string.Empty))
                .Where(cell => !cellPattern.IsMatch(cell))
                .Select(cell => $"{row[0]}: '{cell}'"))
            .ToList();
        Assert.True(bad.Count == 0, "Unclassified matrix cells: " + string.Join("; ", bad));
    }

    /// <summary>
    /// REQ-RAG-046: the BRD row for the deferred endpoints (image generation, realtime audio, batch,
    /// fine-tuning, moderation, OCR) says they are deferred. The code half is
    /// <c>DeferredEndpointTests.NoPublicSurfaceClaimsADeferredEndpoint</c>.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-046 BrdMarksEndpointsDeferred")]
    public void BrdMarksEndpointsDeferred()
    {
        var brd = LibraryRepoFiles.Read("docs/TechieRag-P2-BRD.md");
        var row = brd.Split('\n').FirstOrDefault(line => line.Contains("**BRD-161**", StringComparison.Ordinal));
        Assert.NotNull(row);
        Assert.Contains("deferred", row, StringComparison.OrdinalIgnoreCase);
        foreach (var endpoint in new[] { "Image generation", "realtime audio", "batch", "fine-tuning", "moderation", "OCR" })
        {
            Assert.Contains(endpoint, row, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// REQ-RAG-056: the UsageGuide records the search benchmark for 1,000, 10,000 and 50,000 chunks
    /// with old-path and new-path timings, and every row reports the top-10 ranking as identical to
    /// the old path.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-056 UsageGuideRecordsSearchBenchmark")]
    public void UsageGuideRecordsSearchBenchmark()
    {
        var rows = LibraryRepoFiles.Read("docs/TechieRag-UsageGuide.md").Split('\n')
            .SkipWhile(line => !line.StartsWith("| Chunks | Dimensions | Old path (ms) | New path (ms)", StringComparison.Ordinal))
            .Skip(2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(Cells)
            .ToList();

        Assert.Equal(["1,000", "10,000", "50,000"], rows.Select(r => r[0]).Distinct());
        Assert.All(rows, row =>
        {
            Assert.Matches(@"^\d+(\.\d+)?$", row[2]);
            Assert.Matches(@"^\d+(\.\d+)?$", row[3]);
            Assert.Equal("identical", row[5]);
        });
    }

    private static List<string> Cells(string row) =>
        row.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();
}

/// <summary>
/// A <see cref="FactAttribute"/> for a check against the Sevak application repository checked out
/// beside this one (REQ-FN-006); skipped with a reason when that checkout is absent.
/// </summary>
public sealed class SiblingSevakFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="SiblingSevakFactAttribute"/> class.</summary>
    public SiblingSevakFactAttribute()
    {
        if (SevakRoot() is null)
        {
            Skip = "Sevak repository not checked out beside this repository (../Sevak/Sevak.slnx); check out Sevak there to run it.";
        }
    }

    /// <summary>Gets the sibling Sevak repository root, or null when it is not checked out.</summary>
    /// <returns>The Sevak root folder, or null.</returns>
    public static string? SevakRoot()
    {
        var root = LibraryRepoFiles.TryRoot();
        var sevak = root is null ? null : Path.Combine(Path.GetDirectoryName(root)!, "Sevak");
        return sevak is not null && File.Exists(Path.Combine(sevak, "Sevak.slnx")) ? sevak : null;
    }
}

/// <summary>Reads files from the repository root, found by walking up to <c>TechieRag.slnx</c>.</summary>
internal static class LibraryRepoFiles
{
    /// <summary>Reads a repository file with normalised line endings.</summary>
    /// <param name="relativePath">Path relative to the repository root.</param>
    /// <returns>The file's text.</returns>
    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root(), relativePath)).Replace("\r\n", "\n");

    /// <summary>Gets the repository root or throws.</summary>
    /// <returns>The folder holding <c>TechieRag.slnx</c>.</returns>
    public static string Root() =>
        TryRoot() ?? throw new InvalidOperationException("Repository root (TechieRag.slnx) not found above the test output.");

    /// <summary>Gets the repository root, or null.</summary>
    /// <returns>The folder holding <c>TechieRag.slnx</c>, or null.</returns>
    public static string? TryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName;
    }
}
