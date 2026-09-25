using System.Text.RegularExpressions;
using Xunit;

namespace TechieRag.Tests;

/// <summary>
/// Structural guards over the probe CI workflow, <c>.github/workflows/probe.yml</c>
/// (REQ-FN-057 / BRD-95).
/// </summary>
/// <remarks>
/// <para>A workflow cannot be run from a test; the run page itself exists only after a push. What
/// these tests catch is the regression that would hide a head: a missing build job, a head job with
/// an <c>if:</c> that skips it, or a report that no longer names every head as built, failed or not
/// run.</para>
/// </remarks>
public class ProbeWorkflowTests
{
    private const string Workflow = ".github/workflows/probe.yml";

    /// <summary>
    /// REQ-FN-057: the workflow runs on every push, and each of the four heads has its own job that
    /// builds the probe with <c>-f</c> for that head's target framework, with no job-level
    /// <c>if:</c> that could skip it.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-057 WorkflowBuildsAllFourHeadsOnPush")]
    public void WorkflowBuildsAllFourHeadsOnPush()
    {
        var yaml = LibraryRepoFiles.Read(Workflow);
        Assert.Matches(new Regex(@"^on:\s*\n(\s+[a-z_]+:\s*\n)*?\s+push:", RegexOptions.Multiline), yaml);

        var heads = new Dictionary<string, string>
        {
            ["windows"] = "net10.0-windows10.0.19041.0",
            ["android"] = "net10.0-android",
            ["maccatalyst"] = "net10.0-maccatalyst",
            ["ios"] = "net10.0-ios"
        };
        foreach (var (job, framework) in heads)
        {
            var body = Job(yaml, job);
            Assert.False(string.IsNullOrEmpty(body), $"No '{job}' job in {Workflow}.");
            Assert.Contains($"dotnet build ${{{{ env.PROBE }}}} -f {framework}", body, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex(@"^    if:", RegexOptions.Multiline), body);
        }

        Assert.Contains("PROBE: samples/TechieRag.Probe/TechieRag.Probe.csproj", yaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-FN-057: the report job always runs after all four heads, writes each head as built,
    /// failed or not run to the run page's summary, and fails the run when any head did not build,
    /// so an unbuildable head is reported and never skipped.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-057 ReportNamesEveryHeadBuiltFailedOrNotRun")]
    public void ReportNamesEveryHeadBuiltFailedOrNotRun()
    {
        var report = Job(LibraryRepoFiles.Read(Workflow), "report");

        Assert.Contains("needs: [windows, android, maccatalyst, ios]", report, StringComparison.Ordinal);
        Assert.Contains("if: always()", report, StringComparison.Ordinal);
        Assert.Contains("GITHUB_STEP_SUMMARY", report, StringComparison.Ordinal);
        Assert.Contains("success) echo built", report, StringComparison.Ordinal);
        Assert.Contains("failure) echo failed", report, StringComparison.Ordinal);
        Assert.Contains("echo \"not run", report, StringComparison.Ordinal);
        foreach (var head in new[] { "| Windows |", "| Android |", "| Mac Catalyst |", "| iOS |" })
        {
            Assert.Contains(head, report, StringComparison.Ordinal);
        }

        Assert.Matches(new Regex(@"\[\[ ""\$r"" == success \]\] \|\| exit 1"), report);
    }

    /// <summary>
    /// REQ-FN-057: the Android job presses the probe's button on an emulator (x86_64, the ABI the
    /// probe builds for emulators) and reports the outcome to the report job.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-057 AndroidJobPressesTheButtonOnAnEmulator")]
    public void AndroidJobPressesTheButtonOnAnEmulator()
    {
        var android = Job(LibraryRepoFiles.Read(Workflow), "android");

        Assert.Contains("reactivecircus/android-emulator-runner", android, StringComparison.Ordinal);
        Assert.Contains("arch: x86_64", android, StringComparison.Ordinal);
        Assert.Contains("samples/TechieRag.Probe/scripts/run-android-emulator.sh", android, StringComparison.Ordinal);
        Assert.Contains("emulator: ${{ steps.emulator.outcome }}", android, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(LibraryRepoFiles.Root(), "samples/TechieRag.Probe/scripts/run-android-emulator.sh")));
    }

    /// <summary>Returns one job's block: from its two-space-indented key to the next job's key.</summary>
    private static string Job(string yaml, string name) =>
        Regex.Match(yaml, $@"^  {Regex.Escape(name)}:\n(.*?)(?=^  [a-z_]+:\n|\z)", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
}
