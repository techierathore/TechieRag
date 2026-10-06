using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TechieRag.Tests;

/// <summary>
/// Structural guards over the two publishing workflows and the packable projects they ship
/// (REQ-FN-005 / BRD-86, REQ-FN-065 / BRD-156, REQ-FN-067 / BRD-160).
/// </summary>
/// <remarks>
/// <para>A workflow cannot be run from a test, but the two regressions that matter can be caught by
/// reading the files: a packable project that one workflow forgets to pack (TechieRag.Telemetry was
/// never packed until 2026-09-24), and a second path to nuget.org reappearing in the internal-feed
/// workflow. The packable projects are discovered from <c>src/</c>, so a new package is guarded the
/// moment its project exists.</para>
/// </remarks>
public class PublishingWorkflowTests
{
    private const string InternalWorkflow = ".github/workflows/publish-github-packages.yml";
    private const string PublicWorkflow = ".github/workflows/publish-nuget.yml";

    /// <summary>
    /// Every packable project under <c>src/</c>, plus TechieRag.Agents whether or not its project is on
    /// disk yet, is restored, built and packed by path in both workflows.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-005 BothWorkflowsPackEveryPackableProject")]
    public void BothWorkflowsPackEveryPackableProject()
    {
        var missing = ExpectedProjectPaths()
            .SelectMany(path => new[] { InternalWorkflow, PublicWorkflow }
                .Where(workflow => !ReadRepoFile(workflow).Contains(path, StringComparison.Ordinal))
                .Select(workflow => $"{path} in {workflow}"))
            .ToList();

        Assert.True(missing.Count == 0, "Not packed: " + string.Join("; ", missing));
    }

    /// <summary>
    /// The public workflow gates the version increment and the packed-file check on every package id,
    /// so no package can ship at a different version from the others.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-005 PublicWorkflowChecksEveryPackageId")]
    public void PublicWorkflowChecksEveryPackageId()
    {
        var workflow = ReadRepoFile(PublicWorkflow);
        var packageIds = Regex.Match(workflow, @"PACKAGE_IDS:\s*(.+)").Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var confirmed = Regex.Match(workflow, @"for id in ([^;]+); do").Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var id in ExpectedPackageIds())
        {
            Assert.Contains(id.ToLowerInvariant(), packageIds);
            Assert.Contains(id, confirmed);
        }
    }

    /// <summary>
    /// The internal-feed workflow has no path to nuget.org: no nuget.org source, no API-key secret, no
    /// OIDC login and no <c>publish-nuget-org</c> job. A <c>v*</c> tag therefore reaches GitHub Packages
    /// only.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-067 InternalWorkflowNeverPublishesToNuGetOrg")]
    public void InternalWorkflowNeverPublishesToNuGetOrg()
    {
        var workflow = ReadRepoFile(InternalWorkflow);

        Assert.DoesNotContain("api.nuget.org", workflow);
        Assert.DoesNotContain("secrets.NUGET_API_KEY", workflow);
        Assert.DoesNotContain("NuGet/login", workflow);
        Assert.DoesNotMatch(new Regex(@"^  publish-nuget-org:", RegexOptions.Multiline), workflow);
    }

    /// <summary>
    /// The public workflow runs only on a manual dispatch, never on a push or tag.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-067 PublicWorkflowIsManualDispatchOnly")]
    public void PublicWorkflowIsManualDispatchOnly()
    {
        var workflow = ReadRepoFile(PublicWorkflow);
        var triggers = Regex.Match(workflow, @"^on:\s*\n((?:[ \t]+.*\n|\s*\n)+)", RegexOptions.Multiline).Groups[1].Value;

        Assert.Contains("workflow_dispatch:", triggers);
        Assert.DoesNotMatch(new Regex(@"^  (push|pull_request|schedule|release):", RegexOptions.Multiline), triggers);
    }

    /// <summary>
    /// REQ-FN-067 / BRD-160: the internal workflow runs on a pushed <c>v*</c> tag, takes the version from
    /// the tag, and its publish job pushes every packed package to the GitHub Packages feed and nowhere
    /// else.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-067 VersionTagPublishesToGitHubPackages")]
    public void VersionTagPublishesToGitHubPackages()
    {
        var workflow = ReadRepoFile(InternalWorkflow);

        Assert.Matches(new Regex(@"^  push:\s*\n(?:    .*\n)*?    tags:\s*\n\s*- 'v\*'", RegexOptions.Multiline), workflow);
        Assert.Contains("refs/tags/v*", workflow);
        Assert.Matches(new Regex(@"^  publish-github:", RegexOptions.Multiline), workflow);
        Assert.Contains("https://nuget.pkg.github.com/", workflow);
        var pushes = Regex.Matches(workflow, @"dotnet nuget push .*").Select(match => match.Value).ToList();
        Assert.NotEmpty(pushes);
        Assert.All(pushes, push => Assert.Contains("--source \"github\"", push));
    }

    /// <summary>
    /// REQ-FN-065 / BRD-156: both workflows build and pack <c>TechieRag.Telemetry</c> with the same
    /// <c>-p:Version</c> as the core package into the folder they push from, and the public workflow's
    /// version gate lists its package id.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-065 TelemetryIsPackedAndPushedAtTheSharedVersion")]
    public void TelemetryIsPackedAndPushedAtTheSharedVersion()
    {
        const string telemetryProject = "src/TechieRag.Telemetry/TechieRag.Telemetry.csproj";
        const string version = "-p:Version=${{ steps.version.outputs.version }}";

        var internalFeed = ReadRepoFile(InternalWorkflow);
        var core = Regex.Match(internalFeed, @"dotnet pack src/TechieRag/TechieRag\.csproj .*").Value;
        var telemetry = Regex.Match(internalFeed, @"dotnet pack " + Regex.Escape(telemetryProject) + " .*").Value;
        Assert.Contains(version, core);
        Assert.Contains(version, telemetry);
        Assert.Contains("--output ./nupkgs", telemetry);
        Assert.Contains("for package in ./nupkgs/*.nupkg", internalFeed);

        var publicFeed = ReadRepoFile(PublicWorkflow);
        Assert.Contains("TELEMETRY_PROJECT: " + telemetryProject, publicFeed);
        var publicCore = Regex.Match(publicFeed, @"dotnet pack ""\$CORE_PROJECT"".*").Value;
        var publicTelemetry = Regex.Match(publicFeed, @"dotnet pack ""\$TELEMETRY_PROJECT"".*").Value;
        Assert.Contains(version, publicCore);
        Assert.Contains(version, publicTelemetry);
        Assert.Contains("--output ./artifacts", publicTelemetry);
        Assert.Contains("dotnet nuget push \"./artifacts/*.nupkg\"", publicFeed);
        Assert.Contains("techierag.telemetry", Regex.Match(publicFeed, @"PACKAGE_IDS:\s*(.+)").Groups[1].Value.Split(' '));
    }

    /// <summary>
    /// Every packable project carries the same SourceLink, symbol and README settings as the core.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-005 EveryPackableProjectHasSourceLinkSymbolsAndReadme")]
    public void EveryPackableProjectHasSourceLinkSymbolsAndReadme()
    {
        string[] required = ["PublishRepositoryUrl", "EmbedUntrackedSources", "IncludeSymbols", "SymbolPackageFormat", "PackageReadmeFile"];
        var gaps = PackableProjects()
            .SelectMany(project => required
                .Where(name => project.Document.Descendants(name).FirstOrDefault()?.Value is null or "" or "false")
                .Select(name => $"{project.Path}: {name}"))
            .ToList();

        Assert.True(gaps.Count == 0, "Missing packaging settings: " + string.Join("; ", gaps));
    }

    /// <summary>
    /// REQ-FN-071 (Sevak TR-RAG-048): in both workflows every <c>dotnet build</c> and <c>dotnet pack</c>
    /// carries the release <c>-p:Version</c>, the test project is built in the Build step, and
    /// <c>dotnet test</c> never builds. A test run that builds rebuilt the net10.0 outputs at 1.0.0.0 over
    /// the release build, and <c>pack --no-build</c> shipped them in 1.0.9 to 1.1.1.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-071 NoWorkflowStepRebuildsWithoutTheVersion")]
    public void NoWorkflowStepRebuildsWithoutTheVersion()
    {
        const string version = "-p:Version=${{ steps.version.outputs.version }}";

        foreach (var path in new[] { InternalWorkflow, PublicWorkflow })
        {
            var workflow = ReadRepoFile(path);
            var builds = Regex.Matches(workflow, @"dotnet (build|pack) .*").Select(match => match.Value).ToList();
            var tests = Regex.Matches(workflow, @"dotnet test .*").Select(match => match.Value).ToList();

            Assert.NotEmpty(builds);
            Assert.All(builds, line => Assert.Contains(version, line));
            Assert.Contains(builds, line => line.StartsWith("dotnet build", StringComparison.Ordinal)
                && (line.Contains("tests/TechieRag.Tests/TechieRag.Tests.csproj", StringComparison.Ordinal) || line.Contains("\"$TEST_PROJECT\"", StringComparison.Ordinal)));
            Assert.NotEmpty(tests);
            Assert.All(tests, line => Assert.Contains("--no-build", line));
        }
    }

    /// <summary>
    /// REQ-FN-071 (Sevak TR-RAG-048): both workflows run the assembly-version check on the packed
    /// folder after the last pack and before anything is pushed (or, on the public feed, before the
    /// OIDC login), and the check script exists.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-071 BothWorkflowsCheckAssemblyVersionsBeforePush")]
    public void BothWorkflowsCheckAssemblyVersionsBeforePush()
    {
        const string script = ".github/workflows/scripts/check-package-assembly-versions.cs";
        Assert.True(File.Exists(Path.Combine(RepoRoot(), script)), script + " is missing");

        foreach (var (path, folder) in new[] { (InternalWorkflow, "./nupkgs"), (PublicWorkflow, "./artifacts") })
        {
            var workflow = ReadRepoFile(path);
            var check = workflow.IndexOf($"dotnet run {script} -- {folder} ${{{{ steps.version.outputs.version }}}}", StringComparison.Ordinal);
            var lastPack = workflow.LastIndexOf("dotnet pack ", StringComparison.Ordinal);
            var firstPush = workflow.IndexOf("dotnet nuget push", StringComparison.Ordinal);
            var login = workflow.IndexOf("uses: NuGet/login", StringComparison.Ordinal);

            Assert.True(check > lastPack, $"{path}: the version check must run after the last pack");
            Assert.True(check < firstPush, $"{path}: the version check must run before the push");
            Assert.True(login < 0 || check < login, $"{path}: the version check must run before the OIDC login");
        }
    }

    private static IEnumerable<string> ExpectedProjectPaths() =>
        PackableProjects().Select(project => project.Path)
            .Append("src/TechieRag.Agents/TechieRag.Agents.csproj")
            .Distinct();

    private static IEnumerable<string> ExpectedPackageIds() =>
        ExpectedProjectPaths().Select(path => Path.GetFileNameWithoutExtension(path));

    private static IEnumerable<(string Path, XDocument Document)> PackableProjects()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        foreach (var file in Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories).Order())
        {
            var document = XDocument.Load(file);
            var isPackable = document.Descendants("IsPackable").FirstOrDefault()?.Value;
            if (document.Descendants("PackageId").Any() && isPackable != "false")
            {
                yield return (Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/'), document);
            }
        }
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath)).Replace("\r\n", "\n");

    private static string RepoRoot()
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
