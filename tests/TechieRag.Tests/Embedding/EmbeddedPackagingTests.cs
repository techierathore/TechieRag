using System.Xml.Linq;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// Structural guard over <c>TechieRag.Embedded</c>'s native ONNX Runtime wiring for MAUI heads
/// (REQ-FN-055 / BRD-90).
/// </summary>
/// <remarks>
/// A MAUI head for Mac Catalyst or iOS can only be linked on a Mac, so the wiring itself is proven by
/// the probe app's builds there; what this guard catches on any host is the regression a Windows
/// build would not: the targets file dropped from the package, or its ONNX Runtime version drifting
/// from the one the package references (the xcframework path would then point at nothing).
/// </remarks>
public class EmbeddedPackagingTests
{
    /// <summary>The targets file and the stub are packed under <c>buildTransitive/</c>.</summary>
    [Fact]
    public void NativeWiringIsPackedAsBuildTransitive()
    {
        var project = XDocument.Load(Locate("src/TechieRag.Embedded/TechieRag.Embedded.csproj"));
        var packed = project.Descendants("None")
            .Where(n => (string?)n.Attribute("Pack") == "true" && (string?)n.Attribute("PackagePath") == "buildTransitive\\")
            .Select(n => (string?)n.Attribute("Include"))
            .ToList();

        Assert.Contains("buildTransitive\\TechieRag.Embedded.targets", packed);
        Assert.Contains("buildTransitive\\onnx-customops-stub.c", packed);
    }

    /// <summary>The targets file's default ONNX Runtime version equals the package's reference.</summary>
    [Fact]
    public void TargetsOnnxVersionMatchesThePackageReference()
    {
        var project = XDocument.Load(Locate("src/TechieRag.Embedded/TechieRag.Embedded.csproj"));
        var referenced = project.Descendants("PackageReference")
            .Single(p => (string?)p.Attribute("Include") == "Microsoft.ML.OnnxRuntime")
            .Attribute("Version")!.Value;

        var targets = XDocument.Load(Locate("src/TechieRag.Embedded/buildTransitive/TechieRag.Embedded.targets"));
        var declared = targets.Descendants("TechieRagOnnxRuntimeVersion").Single().Value;

        Assert.Equal(referenced, declared);
    }

    /// <summary>
    /// Mac Catalyst gets the static xcframework with ForceLoad, -lc++ and CoreML weak; Android gets
    /// the .aar check; iOS and Catalyst Debug get the custom-ops stub.
    /// </summary>
    [Fact]
    public void TargetsWireCatalystAndroidAndTheDebugStub()
    {
        var targets = XDocument.Load(Locate("src/TechieRag.Embedded/buildTransitive/TechieRag.Embedded.targets"));

        var catalyst = targets.Root!.Elements("ItemGroup")
            .Where(g => ((string?)g.Attribute("Condition") ?? string.Empty).Contains("'maccatalyst'"))
            .SelectMany(g => g.Elements("NativeReference"))
            .Single();
        Assert.Equal("$(_TechieRagXcframework)", (string?)catalyst.Attribute("Include"));
        Assert.EndsWith(
            "runtimes/ios/native/onnxruntime.xcframework.zip",
            targets.Descendants("_TechieRagXcframework").Single().Value);
        Assert.Equal("Static", catalyst.Element("Kind")?.Value);
        Assert.Equal("True", catalyst.Element("ForceLoad")?.Value);
        Assert.Equal("CoreML", catalyst.Element("WeakFrameworks")?.Value);
        Assert.Equal("-lc++", catalyst.Element("LinkerFlags")?.Value);

        var targetNames = targets.Root.Elements("Target").Select(t => (string?)t.Attribute("Name")).ToList();
        Assert.Contains("TechieRagEnsureOnnxAndroidLibrary", targetNames);
        Assert.Contains("TechieRagBuildOnnxCustomOpsStub", targetNames);
        Assert.True(File.Exists(Locate("src/TechieRag.Embedded/buildTransitive/onnx-customops-stub.c")));
    }

    private static string Locate(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} above {AppContext.BaseDirectory}.");
    }
}
