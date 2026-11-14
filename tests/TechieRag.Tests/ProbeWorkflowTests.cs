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
            Assert.Contains($"dotnet build ${{{{ env.PROBE }}}} -f {framework} -p:ProbeHead={framework}", body, StringComparison.Ordinal);
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

    /// <summary>
    /// REQ-FN-057: the two Apple head jobs run on an image that carries the Xcode their .NET SDK
    /// asks for (macos-26; macos-15 tops out at Xcode 26.3 while .NET for iOS 26.5 builds only under
    /// Xcode 26.5, which failed run 36148218069), select that Xcode with
    /// <c>scripts/select-xcode.sh</c> after the workload install and before the build, append the
    /// flags the script hands back, and write a failed build's first error lines to the run page as
    /// annotations, so the reason is readable without signing in.
    /// </summary>
    [Theory(DisplayName = "REQ-FN-057 AppleJobsSelectTheXcodeTheirSdkAsksFor")]
    [InlineData("maccatalyst", "maui-maccatalyst")]
    [InlineData("ios", "maui-ios")]
    public void AppleJobsSelectTheXcodeTheirSdkAsksFor(string job, string workload)
    {
        var body = Job(LibraryRepoFiles.Read(Workflow), job);

        Assert.Contains("runs-on: macos-26", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Xcode_*.app", body, StringComparison.Ordinal);

        var install = body.IndexOf($"dotnet workload install {workload}", StringComparison.Ordinal);
        var select = body.IndexOf($"bash samples/TechieRag.Probe/scripts/select-xcode.sh {job}", StringComparison.Ordinal);
        var build = body.IndexOf("dotnet build ${{ env.PROBE }}", StringComparison.Ordinal);
        Assert.True(install >= 0 && select > install && build > select, $"'{job}' must install the workload, then select Xcode, then build.");

        Assert.Contains("$PROBE_BUILD_FLAGS", body, StringComparison.Ordinal);
        Assert.Contains("if: failure()", body, StringComparison.Ordinal);
        Assert.Contains("sed 's/^/::error::/'", body, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(LibraryRepoFiles.Root(), "samples/TechieRag.Probe/scripts/select-xcode.sh")));
    }

    /// <summary>
    /// REQ-FN-057: every step that pipes a command's output (into <c>tee</c>) runs with <c>pipefail</c>,
    /// so a failed build fails its step and its head reads "failed", never "built".
    /// </summary>
    /// <remarks>
    /// A step with no <c>shell:</c> runs as <c>bash -e</c>, where a pipeline's status is its last
    /// command's: run 43448574999 (13 Nov 2026) reported the iOS head "built" while its build had failed
    /// with "Optimizing assemblies for size failed", because <c>tee</c> exited 0. <c>shell: bash</c> makes
    /// GitHub run the step with <c>-eo pipefail</c>.
    /// </remarks>
    [Fact(DisplayName = "REQ-FN-057 PipedStepsFailWhenTheirCommandFails")]
    public void PipedStepsFailWhenTheirCommandFails()
    {
        var steps = LibraryRepoFiles.Read(Workflow).Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n      - ");
        var piped = steps.Where(step => step.Contains("run: ", StringComparison.Ordinal) && step.Contains("| tee ", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(piped);
        Assert.All(piped, step => Assert.True(
            step.Contains("shell: bash\n", StringComparison.Ordinal) || step.Contains("set -o pipefail", StringComparison.Ordinal),
            $"This step pipes its output but would ignore a failure: {step.Split('\n')[0]}"));
    }

    /// <summary>
    /// REQ-FN-057: the Xcode selection script reads the wanted version from the installed SDK pack
    /// (never a hard-coded number), falls back to <c>-p:ValidateXcodeVersion=false</c> when that
    /// Xcode is absent instead of failing, and hands the flags to the workflow through
    /// <c>GITHUB_ENV</c>.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-057 SelectXcodeScriptReadsTheWantedVersionFromTheSdkPack")]
    public void SelectXcodeScriptReadsTheWantedVersionFromTheSdkPack()
    {
        var script = LibraryRepoFiles.Read("samples/TechieRag.Probe/scripts/select-xcode.sh");

        Assert.Contains("Microsoft.iOS.Sdk", script, StringComparison.Ordinal);
        Assert.Contains("Microsoft.MacCatalyst.Sdk", script, StringComparison.Ordinal);
        Assert.Contains("packs/\"$PACK\".net10.0_*", script, StringComparison.Ordinal);
        Assert.Contains("-p:ValidateXcodeVersion=false", script, StringComparison.Ordinal);
        Assert.Contains("\"$GITHUB_ENV\"", script, StringComparison.Ordinal);
        Assert.Contains("::notice::", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"Xcode_2\d\.\d"), script);
    }

    /// <summary>
    /// REQ-FN-057: the Xcode the script selects is the one the SDK pack's <c>Versions.plist</c> asks for
    /// (<c>RecommendedXcodeVersion</c>), not the pack folder's version: Microsoft.iOS.Sdk.net10.0_26.5
    /// 26.5.10318 requires Xcode 26.6, and choosing Xcode 26.5 from the folder name fails its build.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-057 SelectXcodeReadsRecommendedXcodeFromThePack")]
    public void SelectXcodeReadsRecommendedXcodeFromThePack()
    {
        var script = LibraryRepoFiles.Read("samples/TechieRag.Probe/scripts/select-xcode.sh");

        Assert.Contains("Versions.plist", script, StringComparison.Ordinal);
        Assert.Contains("<key>RecommendedXcodeVersion</key>", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-FN-057: both Apple heads install one pinned workload set (<c>APPLE_WORKLOAD_SET</c>), so a new
    /// .NET for iOS that needs an Xcode the runner image lacks cannot break the build unannounced (run
    /// 43448574999: SDK 27.2 needs Xcode 27.2, macos-26 tops out at 26.6).
    /// </summary>
    /// <param name="job">The workflow job.</param>
    /// <param name="workload">Its workload id.</param>
    [Theory(DisplayName = "REQ-FN-057 AppleJobsInstallThePinnedWorkloadSet")]
    [InlineData("maccatalyst", "maui-maccatalyst")]
    [InlineData("ios", "maui-ios")]
    public void AppleJobsInstallThePinnedWorkloadSet(string job, string workload)
    {
        var yaml = LibraryRepoFiles.Read(Workflow).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Matches(new Regex(@"^  APPLE_WORKLOAD_SET: 10\.0\.\d+$", RegexOptions.Multiline), yaml);
        Assert.Contains($"dotnet workload install {workload} --version ${{{{ env.APPLE_WORKLOAD_SET }}}}", Job(yaml, job), StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-FN-057: the Android button script waits until the emulator resolves the model host before
    /// pressing, and says "no network" plainly when it never does. "Booted" is not "online": a press right
    /// after boot failed with "HttpRequestException: Connection failure" (run 43448574999; reproduced
    /// 13 Nov 2026 on the API 34 image, whose data network came up after the press).
    /// </summary>
    [Fact(DisplayName = "REQ-FN-057 AndroidScriptWaitsForTheNetworkBeforePressing")]
    public void AndroidScriptWaitsForTheNetworkBeforePressing()
    {
        var script = LibraryRepoFiles.Read("samples/TechieRag.Probe/scripts/run-android-emulator.sh");

        var wait = script.IndexOf("until adb shell ping -c 1 -W 2 \"$MODEL_HOST\"", StringComparison.Ordinal);
        var press = script.IndexOf("--ez autorun true", StringComparison.Ordinal);
        Assert.True(wait >= 0 && press > wait, "The script must wait for the network before pressing the button.");
        Assert.Contains("svc data enable", script, StringComparison.Ordinal);
        Assert.Contains("NOT RUN - the emulator could not resolve", script, StringComparison.Ordinal);
    }

    /// <summary>Returns one job's block: from its two-space-indented key to the next job's key.</summary>
    private static string Job(string yaml, string name) =>
        Regex.Match(yaml, $@"^  {Regex.Escape(name)}:\n(.*?)(?=^  [a-z_]+:\n|\z)", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
}
