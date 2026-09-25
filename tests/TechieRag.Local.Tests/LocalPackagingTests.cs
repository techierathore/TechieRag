using System.IO.Compression;
using System.Xml.Linq;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// Structural guard over <c>TechieRag.Local</c>'s native ONNX Runtime GenAI wiring for MAUI heads
/// (REQ-FN-058 / BRD-102).
/// </summary>
/// <remarks>
/// A Mac Catalyst or iOS head links only on a Mac, so the wiring itself is proven by the probe app's
/// builds and runs there. What this guard catches on any host: the targets file dropped from the
/// package, its GenAI version drifting from the package reference (the xcframework path would point at
/// nothing), or a GenAI upgrade that changes what GenAI wires by itself.
/// </remarks>
public class LocalPackagingTests
{
    private const string TargetsPath = "src/TechieRag.Local/buildTransitive/TechieRag.Local.targets";
    private const string ProjectPath = "src/TechieRag.Local/TechieRag.Local.csproj";

    /// <summary>The targets file is packed under <c>buildTransitive/</c>.</summary>
    [Fact(DisplayName = "REQ-FN-058 GenAiWiringIsPackedAsBuildTransitive")]
    public void GenAiWiringIsPackedAsBuildTransitive()
    {
        var project = XDocument.Load(RepoFiles.Locate(ProjectPath));
        var packed = project.Descendants("None")
            .Where(n => (string?)n.Attribute("Pack") == "true" && (string?)n.Attribute("PackagePath") == "buildTransitive\\")
            .Select(n => (string?)n.Attribute("Include"))
            .ToList();

        Assert.Contains("buildTransitive\\TechieRag.Local.targets", packed);
    }

    /// <summary>The targets file's default GenAI version equals the package's reference.</summary>
    [Fact(DisplayName = "REQ-FN-058 TargetsGenAiVersionMatchesThePackageReference")]
    public void TargetsGenAiVersionMatchesThePackageReference()
    {
        var project = XDocument.Load(RepoFiles.Locate(ProjectPath));
        var referenced = project.Descendants("PackageReference")
            .Single(p => (string?)p.Attribute("Include") == "Microsoft.ML.OnnxRuntimeGenAI")
            .Attribute("Version")!.Value;

        var declared = XDocument.Load(RepoFiles.Locate(TargetsPath)).Descendants("TechieRagOnnxRuntimeGenAIVersion").Single().Value;

        Assert.Equal(referenced, declared);
    }

    /// <summary>
    /// Mac Catalyst gets GenAI's static xcframework with ForceLoad, -lc++ and CoreML weak, behind an
    /// existence check; Android gets the .aar check; everything can be switched off.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-058 TargetsWireCatalystAndCheckAndroid")]
    public void TargetsWireCatalystAndCheckAndroid()
    {
        var targets = XDocument.Load(RepoFiles.Locate(TargetsPath));

        var catalyst = targets.Root!.Elements("ItemGroup")
            .Where(g => ((string?)g.Attribute("Condition") ?? string.Empty).Contains("'maccatalyst'", StringComparison.Ordinal))
            .SelectMany(g => g.Elements("NativeReference"))
            .Single();
        Assert.Equal("$(_TechieRagGenAiXcframework)", (string?)catalyst.Attribute("Include"));
        Assert.EndsWith("runtimes/ios/native/onnxruntime-genai.xcframework.zip", targets.Descendants("_TechieRagGenAiXcframework").Single().Value, StringComparison.Ordinal);
        Assert.Equal("Static", catalyst.Element("Kind")?.Value);
        Assert.Equal("True", catalyst.Element("ForceLoad")?.Value);
        Assert.Equal("CoreML", catalyst.Element("WeakFrameworks")?.Value);
        Assert.Equal("-lc++", catalyst.Element("LinkerFlags")?.Value);

        var targetNames = targets.Root.Elements("Target").Select(t => (string?)t.Attribute("Name")).ToList();
        Assert.Contains("TechieRagCheckGenAiXcframework", targetNames);
        Assert.Contains("TechieRagEnsureGenAiAndroidLibrary", targetNames);
        Assert.Contains("TechieRagDisableLocalNativeWiring", targets.Descendants("_TechieRagWireGenAi").Single().Attribute("Condition")!.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The GenAI package the targets point at, as restored on this machine, still links nothing on Mac
    /// Catalyst (<c>_._</c> placeholders), still carries the Catalyst slice in its iOS xcframework, and
    /// still wires iOS and Android by itself; a GenAI upgrade that changes any of these shows here.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-058 RestoredGenAiPackageNeedsExactlyTheCatalystWiring")]
    public void RestoredGenAiPackageNeedsExactlyTheCatalystWiring()
    {
        var version = XDocument.Load(RepoFiles.Locate(TargetsPath)).Descendants("TechieRagOnnxRuntimeGenAIVersion").Single().Value;
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packages))
        {
            packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        }

        var root = Path.Combine(packages, "microsoft.ml.onnxruntimegenai", version);
        Assert.True(Directory.Exists(root), $"Microsoft.ML.OnnxRuntimeGenAI {version} is not restored at {root}.");

        var catalystBuild = Directory.GetDirectories(Path.Combine(root, "buildTransitive"), "*maccatalyst*").Single();
        Assert.Equal(["_._"], Directory.GetFiles(catalystBuild).Select(Path.GetFileName));

        var iosTargets = File.ReadAllText(Directory.GetFiles(Directory.GetDirectories(Path.Combine(root, "buildTransitive"), "*-ios*").Single(), "*.targets").Single());
        Assert.Contains("onnxruntime-genai.xcframework.zip", iosTargets, StringComparison.Ordinal);
        var androidTargets = File.ReadAllText(Directory.GetFiles(Directory.GetDirectories(Path.Combine(root, "buildTransitive"), "*-android*").Single(), "*.targets").Single());
        Assert.Contains("AndroidLibrary", androidTargets, StringComparison.Ordinal);

        using var xcframework = ZipFile.OpenRead(Path.Combine(root, "runtimes", "ios", "native", "onnxruntime-genai.xcframework.zip"));
        Assert.Contains(xcframework.Entries, e => e.FullName.StartsWith("onnxruntime-genai.xcframework/ios-arm64_x86_64-maccatalyst/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The probe writes no GenAI wiring of its own: no <c>NativeReference</c>, no <c>AndroidLibrary</c>,
    /// no GenAI package reference; it imports <c>TechieRag.Local.targets</c> by path only because a
    /// ProjectReference carries no NuGet build assets.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-058 ProbeImportsLocalTargetsAndNoWiring")]
    public void ProbeImportsLocalTargetsAndNoWiring()
    {
        var project = XDocument.Parse(RepoFiles.Read("samples/TechieRag.Probe/TechieRag.Probe.csproj"));

        Assert.Empty(project.Descendants("NativeReference"));
        Assert.Empty(project.Descendants("AndroidLibrary"));
        Assert.DoesNotContain(project.Descendants("PackageReference"), p => ((string?)p.Attribute("Include") ?? string.Empty).StartsWith("Microsoft.ML.OnnxRuntimeGenAI", StringComparison.Ordinal));

        var imports = project.Descendants("Import").Select(i => ((string?)i.Attribute("Project") ?? string.Empty).Replace('\\', '/')).ToList();
        Assert.Contains("../../src/TechieRag.Local/buildTransitive/TechieRag.Local.targets", imports);
    }
}
