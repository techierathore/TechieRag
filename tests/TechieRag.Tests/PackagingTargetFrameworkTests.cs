using System.Xml.Linq;
using Xunit;

namespace TechieRag.Tests;

/// <summary>
/// Structural guard over the package's target frameworks (REQ-RAG-037 / BRD-118).
/// </summary>
/// <remarks>
/// <para><b>What this can and cannot prove.</b> That the net8.0 target <i>compiles</i> is proven by the
/// build, not by a test — this test project itself runs on net10.0 and can only ever load the net10.0
/// assembly. What this guard catches is the regression that a build would not: someone simplifying the
/// project file back to a single TFM, which silently drops every LTS consumer at the next release with
/// a green build and no warning.</para>
/// <para>Kept alongside the structural guards the wider solution already uses, for the same reason —
/// a promise nobody asserts is a promise that quietly stops being true.</para>
/// </remarks>
public class PackagingTargetFrameworkTests
{
    /// <summary>The package still ships for net8.0 as well as net10.0.</summary>
    [Fact]
    public void ThePackageStillTargetsNet8()
    {
        var frameworks = ReadTargetFrameworks();

        Assert.Contains("net8.0", frameworks);
        Assert.Contains("net10.0", frameworks);
    }

    /// <summary>
    /// net10.0 stays first so that it is the framework a modern consumer resolves to and the one the
    /// IDE shows by default; NuGet picks the best match either way, but ordering drives the tooling.
    /// </summary>
    [Fact]
    public void Net10IsTheLeadingTargetFramework()
    {
        Assert.Equal("net10.0", ReadTargetFrameworks()[0]);
    }

    private static string[] ReadTargetFrameworks()
    {
        var projectFile = LocateProjectFile();
        var document = XDocument.Load(projectFile);

        var declared = document.Descendants("TargetFrameworks").FirstOrDefault()?.Value
            ?? document.Descendants("TargetFramework").FirstOrDefault()?.Value
            ?? throw new InvalidOperationException($"No target framework declared in {projectFile}.");

        return declared
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string LocateProjectFile()
    {
        // Walk up from the test assembly rather than hard-coding a relative depth, so the guard keeps
        // working if the output path gains or loses a folder (a TFM subfolder, a RID subfolder).
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "TechieRag", "TechieRag.csproj");
            if (File.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate src/TechieRag/TechieRag.csproj from " + AppContext.BaseDirectory);
    }
}
