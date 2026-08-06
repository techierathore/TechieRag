using System.Xml.Linq;
using Xunit;

namespace TechieDesk.Tests.Packaging;

/// <summary>
/// REQ-NFR-004 / 004a (BRD-95): a distributable product must not ship pre-release packages.
/// </summary>
/// <remarks>
/// History this guards. TrBlazeUI.Components 1.0.7 depends on HtmlSanitizer 9.0.892, which pins
/// AngleSharp to the exact version <c>[0.17.1]</c> — the moderate mXSS advisory
/// GHSA-pgww-w46g-26qg (NU1902). The app overrides that with a direct HtmlSanitizer reference.
/// For several months the only override that reached a patched AngleSharp was 9.1.949-beta, which
/// also dragged in AngleSharp.Css 1.0.0-beta.216, so clearing the advisory cost two pre-release
/// packages in a shipped product. HtmlSanitizer 9.1.973 (stable) resolves AngleSharp 1.6.0 and
/// AngleSharp.Css 1.0.0, both stable, which retired that trade-off on 2026-07-30.
///
/// These are cheap build-configuration assertions rather than behavioural tests, and they exist
/// because both failure modes are silent: reverting to a beta still compiles and still passes every
/// behavioural test, and downgrading to the stable-but-older 9.0.967 reintroduces the vulnerable
/// AngleSharp pin without any test noticing. Neither regression is visible at run time until a
/// release audit catches it, which is exactly what happened the first time.
/// </remarks>
public sealed class PrereleasePackageGuardTests
{
    /// <summary>
    /// Walks up from the test assembly to the repository root, identified by the solution file.
    /// </summary>
    /// <returns>The absolute path to the repository root.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>
    /// Reads every <c>PackageReference</c> version declared by the desktop head.
    /// </summary>
    /// <returns>Package id and declared version pairs.</returns>
    private static IReadOnlyList<(string Id, string Version)> DesktopPackageReferences()
    {
        var projectPath = Path.Combine(RepositoryRoot(), "apps", "TechieDesk", "TechieDesk.csproj");
        Assert.True(File.Exists(projectPath), $"Expected the desktop head at {projectPath}.");

        return XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(element => (
                Id: element.Attribute("Include")?.Value ?? string.Empty,
                Version: element.Attribute("Version")?.Value ?? string.Empty))
            .Where(reference => reference.Id.Length > 0 && reference.Version.Length > 0)
            .ToList();
    }

    /// <summary>
    /// No package the desktop head references directly may be a pre-release: a version carrying a
    /// SemVer pre-release label (the <c>-beta</c> / <c>-rc</c> suffix) fails this test.
    /// </summary>
    [Fact]
    public void DesktopHeadShipsNoPrereleasePackages()
    {
        var prerelease = DesktopPackageReferences()
            .Where(reference => reference.Version.Contains('-'))
            .Select(reference => $"{reference.Id} {reference.Version}")
            .ToList();

        Assert.True(
            prerelease.Count == 0,
            "REQ-NFR-004/004a: the desktop head must ship only stable packages, but found: "
                + string.Join(", ", prerelease));
    }

    /// <summary>
    /// HtmlSanitizer must stay at or above 9.1.973 — the first STABLE release reaching a patched
    /// AngleSharp. The stable-but-older 9.0.967 would satisfy the pre-release check above while
    /// silently restoring the vulnerable AngleSharp <c>[0.17.1]</c> pin, so it is excluded here.
    /// </summary>
    [Fact]
    public void HtmlSanitizerStaysOnTheStableAngleSharpLine()
    {
        var sanitizer = DesktopPackageReferences()
            .SingleOrDefault(reference =>
                string.Equals(reference.Id, "HtmlSanitizer", StringComparison.OrdinalIgnoreCase));

        Assert.False(
            sanitizer.Id is null,
            "The direct HtmlSanitizer reference overrides TrBlazeUI's vulnerable transitive pin; "
                + "removing it reintroduces AngleSharp [0.17.1] (GHSA-pgww-w46g-26qg).");

        var parsed = Version.Parse(sanitizer.Version.Split('-')[0]);
        Assert.True(
            parsed >= new Version(9, 1, 973),
            $"HtmlSanitizer must be >= 9.1.973 to resolve AngleSharp >= 1.5.0, but was {sanitizer.Version}.");
    }
}
