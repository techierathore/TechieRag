using System.Diagnostics;
using Xunit;

namespace TechieRag.Tests;

/// <summary>
/// REQ-FN-063 / BRD-154: a consumer project targeting <c>net8.0</c> that references <c>TechieRag</c>
/// and <c>TechieRag.Telemetry</c> restores and builds.
/// </summary>
/// <remarks>
/// <para>This test project runs on net10.0 and can only load net10.0 assemblies, so the net8.0 promise
/// is checked the way a consumer would meet it: a throwaway <c>net8.0</c> project is written to a
/// temporary folder, references both library projects, uses a public type from each, and is restored
/// and built with the same <c>dotnet</c> that runs this test. A library that stops compiling for
/// net8.0, or that picks up an API net8.0 lacks, fails here.</para>
/// <para>The libraries themselves are not rebuilt (<c>--no-dependencies</c>): the solution build that
/// precedes this test run already compiled their net8.0 targets from the current source, and building
/// them again from inside a test would race that build's output folders.</para>
/// </remarks>
public sealed class Net8ConsumerBuildTests : IDisposable
{
    private readonly string consumerFolder = Path.Combine(Path.GetTempPath(), $"trnet8-{Guid.NewGuid():N}");

    /// <summary>Creates the temporary folder the consumer project is written to.</summary>
    public Net8ConsumerBuildTests() => Directory.CreateDirectory(consumerFolder);

    /// <summary>
    /// A net8.0 class library referencing both projects and calling into each restores and builds with
    /// exit code 0.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-063 ANet8ConsumerRestoresAndBuilds")]
    public void ANet8ConsumerRestoresAndBuilds()
    {
        var repoRoot = RepoRoot();
        File.WriteAllText(Path.Combine(consumerFolder, "Net8Consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{Path.Combine(repoRoot, "src", "TechieRag", "TechieRag.csproj")}" />
                <ProjectReference Include="{Path.Combine(repoRoot, "src", "TechieRag.Telemetry", "TechieRag.Telemetry.csproj")}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(consumerFolder, "Consumer.cs"), """
            namespace Net8Consumer;

            public static class Consumer
            {
                public static TechieRag.TechieRagBuilder Builder() => new TechieRag.TechieRagBuilder().UseSqliteVec("consumer.db");

                public static TechieRag.Telemetry.TechieRagTelemetryOptions Telemetry() => new();
            }
            """);

        var (exitCode, output) = RunDotnet(
            $"build \"{Path.Combine(consumerFolder, "Net8Consumer.csproj")}\" -c {Configuration()} --no-dependencies -nodeReuse:false");

        Assert.True(exitCode == 0, $"net8.0 consumer build failed (exit {exitCode}):\n{output}");
        Assert.True(
            File.Exists(Path.Combine(consumerFolder, "bin", Configuration(), "net8.0", "Net8Consumer.dll")),
            "The consumer assembly was not produced:\n" + output);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(consumerFolder)) Directory.Delete(consumerFolder, recursive: true);
        }
        catch (IOException)
        {
            // A lingering compiler handle only delays cleanup of a temp folder; it is not a test result.
        }
    }

    private static (int ExitCode, string Output) RunDotnet(string arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath()
        };

        // The test host inherits MSBuild's own variables from the run that launched it; a child build
        // must resolve its SDK afresh, as a consumer's would.
        foreach (var variable in new[] { "MSBuildExtensionsPath", "MSBUILD_EXE_PATH", "MSBuildSDKsPath", "MSBuildLoadMicrosoftTargetsReadOnly" })
        {
            start.Environment.Remove(variable);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            return (-1, "Timed out after 5 minutes.\n" + stdout.Result + stderr.Result);
        }

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private static string Configuration() =>
        AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";

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
