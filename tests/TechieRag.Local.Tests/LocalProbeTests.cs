using System.Text.RegularExpressions;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// The probe app's second button (REQ-FN-059 / BRD-106) and the per-platform local-model numbers in
/// the UsageGuide (REQ-FN-060 / BRD-108), read from the repository files.
/// </summary>
/// <remarks>
/// The probe is a MAUI app outside <c>TechieRag.slnx</c>, so the button itself is pressed by the
/// native smokes (Mac Catalyst and the iOS simulator on the owner's Mac; screenshots under
/// <c>tests/.artifacts/probe/</c>). These tests keep the screen, the unattended switch and the
/// recorded numbers from regressing.
/// </remarks>
public class LocalProbeTests
{
    private const string ProbeFolder = "samples/TechieRag.Probe";

    /// <summary>
    /// REQ-FN-059: the screen has the second button and shows the sentence and the generation timings,
    /// each under an AutomationId; the timings name the first token, tokens per second and peak memory,
    /// and peak memory is in decimal megabytes (1,000,000 bytes) so it agrees with the download line
    /// on the same screen (<c>ModelDownloadService.FormatBytes</c>), not binary MiB.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-059 ProbeSecondButtonShowsSentenceAndTimings")]
    public void ProbeSecondButtonShowsSentenceAndTimings()
    {
        var page = RepoFiles.Read(ProbeFolder + "/MainPage.cs");
        foreach (var id in new[] { "RunGenerateButton", "GenerateStatusLabel", "GeneratedSentenceLabel", "GenerationTimingsLabel" })
        {
            Assert.Contains($"AutomationId = \"{id}\"", page, StringComparison.Ordinal);
        }

        var result = RepoFiles.Read(ProbeFolder + "/GenerationResult.cs");
        var timings = Regex.Match(result, @"public string Timings =>.*?;", RegexOptions.Singleline).Value;
        Assert.Contains("first token {TimeToFirstTokenMs", timings, StringComparison.Ordinal);
        Assert.Contains("{TokensPerSecond:F1} tokens/s", timings, StringComparison.Ordinal);
        Assert.Contains("peak {PeakMemoryMb:F0} MB", timings, StringComparison.Ordinal);

        Assert.Contains("BytesPerMegabyte = 1_000_000d", result, StringComparison.Ordinal);
        Assert.DoesNotContain("1_048_576", result, StringComparison.Ordinal);
        Assert.Contains("peakMb={PeakMemoryMb:F0}", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-FN-059: the runner goes through <c>TechieRag.Local</c> the way an app does (platform default
    /// model, terms through <c>ConfirmTermsAsync</c>, never <c>TermsAccepted</c>) and reads peak memory
    /// through <c>PeakMemory</c>, which covers iOS and Mac Catalyst where the working-set peak is 0.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-059 ProbeRunnerAsksTermsAndMeasuresPeak")]
    public void ProbeRunnerAsksTermsAndMeasuresPeak()
    {
        var runner = RepoFiles.Read(ProbeFolder + "/LocalProbeRunner.cs");
        Assert.Contains("LocalModel.PlatformDefault", runner, StringComparison.Ordinal);
        Assert.Contains("ConfirmTermsAsync = confirmTerms", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("TermsAccepted", runner, StringComparison.Ordinal);
        Assert.Contains("PeakMemory.ReadBytes()", runner, StringComparison.Ordinal);

        var peak = RepoFiles.Read(ProbeFolder + "/PeakMemory.cs");
        Assert.Contains("proc_pid_rusage", peak, StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-FN-059: an unattended run presses the second button without injected input:
    /// <c>TECHIERAG_PROBE_AUTORUN=local</c>, or the Android intent extra <c>autorunlocal</c>.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-059 ProbeSupportsUnattendedLocalRun")]
    public void ProbeSupportsUnattendedLocalRun()
    {
        var launch = RepoFiles.Read(ProbeFolder + "/ProbeLaunch.cs");
        Assert.Contains("AutoRunLocalValue = \"local\"", launch, StringComparison.Ordinal);

        var page = RepoFiles.Read(ProbeFolder + "/MainPage.cs");
        Assert.Contains("ProbeLaunch.AutoRunLocal", page, StringComparison.Ordinal);

        var activity = RepoFiles.Read(ProbeFolder + "/Platforms/Android/MainActivity.cs");
        Assert.Contains("\"autorunlocal\"", activity, StringComparison.Ordinal);
    }

    /// <summary>
    /// REQ-FN-060: the UsageGuide's "Local model: measured per platform" table covers Windows,
    /// macOS or Mac Catalyst, Android and iOS; every row names a device; a dated row (yyyy-MM-dd)
    /// carries a model, a tokens-per-second figure, a peak memory in MB or GB and a first-token time
    /// (or "not recorded"); an undated row says "not yet run" and carries no numbers.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-060 LocalModelNumbersNameDeviceAndDate")]
    public void LocalModelNumbersNameDeviceAndDate()
    {
        var guide = RepoFiles.Read("docs/TechieRag-UsageGuide.md");
        var section = Regex.Match(guide, @"^### Local model: measured per platform[^\n]*\n(.*?)(?=^### )", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
        var rows = section.Split('\n').Where(line => line.StartsWith('|')).ToList();
        Assert.True(rows.Count >= 3, "The local-model measurement table is missing.");
        Assert.Equal(["Platform", "Device", "Date", "Model", "How measured", "First token (s)", "Tokens per second", "Peak memory"], Cells(rows[0]));

        var body = rows.Skip(2).Select(Cells).ToList();
        Assert.All(body, row => Assert.Equal(8, row.Count));
        foreach (var platform in new[] { "Windows", "mac", "Android", "iOS" })
        {
            Assert.Contains(body, row => row[0].Contains(platform, StringComparison.OrdinalIgnoreCase));
        }

        var bad = body.Where(row => !RowIsValid(row)).Select(row => string.Join(" | ", row)).ToList();
        Assert.True(bad.Count == 0, "Rows without a device and date, or with bad numbers: " + string.Join("; ", bad));
    }

    private static bool RowIsValid(IReadOnlyList<string> row)
    {
        if (string.IsNullOrWhiteSpace(row[1]) || row[1] == "—")
        {
            return false;
        }

        if (row[2] == "not yet run")
        {
            return row.Skip(3).All(cell => cell == "—");
        }

        var number = @"^\d+(\.\d+)?(–\d+(\.\d+)?)?";
        return Regex.IsMatch(row[2], @"^\d{4}-\d{2}-\d{2}$")
            && row[3].Length > 0 && row[3] != "—"
            && (Regex.IsMatch(row[5], number + "$") || row[5] == "not recorded")
            && Regex.IsMatch(row[6], number + "$")
            && Regex.IsMatch(row[7], number + " (MB|GB)$");
    }

    private static List<string> Cells(string row) =>
        row.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToList();
}
