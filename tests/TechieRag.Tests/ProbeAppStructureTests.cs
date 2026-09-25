using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TechieRag.Tests;

/// <summary>
/// Structural guards over the four-platform probe app, <c>samples/TechieRag.Probe</c>
/// (REQ-FN-056 / BRD-94, and the "no native wiring in the app" half of REQ-FN-055 / BRD-90).
/// </summary>
/// <remarks>
/// <para>The probe is a MAUI app and is not part of <c>TechieRag.slnx</c> (the library's build must
/// not need the MAUI workloads), so these tests read its files. The button itself is pressed by
/// the native smokes: Mac Catalyst and the iOS simulator on the owner's Mac, Windows and the Android
/// emulator on the Windows laptop, and the Android emulator job in CI.</para>
/// </remarks>
public class ProbeAppStructureTests
{
    private const string ProbeFolder = "samples/TechieRag.Probe";

    /// <summary>
    /// REQ-FN-056: the probe project is a MAUI app whose target frameworks name the Android, iOS and
    /// Mac Catalyst heads everywhere and the Windows head on a Windows host.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-056 ProbeTargetsAllFourHeads")]
    public void ProbeTargetsAllFourHeads()
    {
        var project = LoadProject();
        var frameworks = project.Descendants("TargetFrameworks").Select(e => e.Value).ToList();
        var all = string.Join(";", frameworks);

        Assert.Equal("true", project.Descendants("UseMaui").Single().Value);
        Assert.Equal("Exe", project.Descendants("OutputType").Single().Value);
        Assert.Contains("net10.0-android", all, StringComparison.Ordinal);
        Assert.Contains("net10.0-ios", all, StringComparison.Ordinal);
        Assert.Contains("net10.0-maccatalyst", all, StringComparison.Ordinal);

        var windows = project.Descendants("TargetFrameworks")
            .Single(e => e.Value.Contains("net10.0-windows", StringComparison.Ordinal));
        Assert.Contains("IsOSPlatform('windows')", (string?)windows.Attribute("Condition") ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-FN-057: <c>ProbeHead</c> narrows the probe to the one head being built, so CI restores
    /// that head alone and a Linux or macOS runner never needs another platform's workload.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-057 ProbeHeadNarrowsRestoreToOneHead")]
    public void ProbeHeadNarrowsRestoreToOneHead()
    {
        var narrowed = LoadProject().Descendants("TargetFrameworks").Last();

        Assert.Equal("$(ProbeHead)", narrowed.Value);
        Assert.Equal("'$(ProbeHead)' != ''", (string?)narrowed.Attribute("Condition"));
    }

    /// <summary>
    /// REQ-FN-055 / REQ-FN-056: the probe writes no ONNX Runtime wiring of its own. No
    /// <c>NativeReference</c>, no <c>AndroidLibrary</c>, no ONNX Runtime package reference; the one
    /// import is <c>TechieRag.Embedded</c>'s own buildTransitive targets, which a NuGet consumer gets
    /// without writing anything.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-055 ProbeCarriesNoHandWrittenNativeWiring")]
    public void ProbeCarriesNoHandWrittenNativeWiring()
    {
        var project = LoadProject();

        Assert.Empty(project.Descendants("NativeReference"));
        Assert.Empty(project.Descendants("AndroidLibrary"));
        Assert.Empty(project.Descendants("AndroidNativeLibrary"));
        Assert.DoesNotContain(
            project.Descendants("PackageReference"),
            p => ((string?)p.Attribute("Include") ?? string.Empty).StartsWith("Microsoft.ML.OnnxRuntime", StringComparison.Ordinal));

        var imports = project.Descendants("Import").Select(i => ((string?)i.Attribute("Project") ?? string.Empty).Replace('\\', '/')).ToList();
        Assert.Contains("../../src/TechieRag.Embedded/buildTransitive/TechieRag.Embedded.targets", imports);
        // REQ-FN-058 adds TechieRag.Local's own buildTransitive targets; nothing else may be imported.
        Assert.All(imports, path => Assert.Matches(@"^\.\./\.\./src/TechieRag\.[A-Za-z]+/buildTransitive/TechieRag\.[A-Za-z]+\.targets$", path));
    }

    /// <summary>
    /// REQ-FN-056: the probe's one screen has the embed button and shows the top result and the
    /// timings, each under an <c>AutomationId</c> native automation binds to; the timings name the
    /// embed, store and search steps; the runner embeds exactly three texts.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-056 ProbeScreenShowsTopResultAndTimings")]
    public void ProbeScreenShowsTopResultAndTimings()
    {
        var page = LibraryRepoFiles.Read(ProbeFolder + "/MainPage.cs");
        foreach (var id in new[] { "RunEmbedButton", "StatusLabel", "TopResultLabel", "TimingsLabel", "ResultLineLabel" })
        {
            Assert.Contains($"AutomationId = \"{id}\"", page, StringComparison.Ordinal);
        }

        var result = LibraryRepoFiles.Read(ProbeFolder + "/ProbeResult.cs");
        var timings = Regex.Match(result, @"public string Timings =>.*?;", RegexOptions.Singleline).Value;
        Assert.Contains("embed {EmbedMs", timings, StringComparison.Ordinal);
        Assert.Contains("store {StoreMs", timings, StringComparison.Ordinal);
        Assert.Contains("search {SearchMs", timings, StringComparison.Ordinal);

        var runner = LibraryRepoFiles.Read(ProbeFolder + "/ProbeRunner.cs");
        var texts = Regex.Match(runner, @"Texts =\s*\[(.*?)\];", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal(3, Regex.Matches(texts, "\"[^\"]+\"").Count);
    }

    /// <summary>
    /// REQ-FN-056: an unattended run (CI's emulator, the Mac smokes) can press the button without
    /// injected input: the <c>TECHIERAG_PROBE_AUTORUN</c> environment variable turns auto-run on,
    /// and the result line goes to <c>probe-result.txt</c> in the app data folder.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-056 ProbeSupportsUnattendedAutoRun")]
    public void ProbeSupportsUnattendedAutoRun()
    {
        var launch = LibraryRepoFiles.Read(ProbeFolder + "/ProbeLaunch.cs");
        Assert.Contains("\"TECHIERAG_PROBE_AUTORUN\"", launch, StringComparison.Ordinal);

        var page = LibraryRepoFiles.Read(ProbeFolder + "/MainPage.cs");
        Assert.Contains("ProbeLaunch.AutoRun", page, StringComparison.Ordinal);
        Assert.Contains("\"probe-result.txt\"", page, StringComparison.Ordinal);
    }

    private static XDocument LoadProject() =>
        XDocument.Parse(LibraryRepoFiles.Read(ProbeFolder + "/TechieRag.Probe.csproj"));
}
