using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Scheduling;
using TechieRag.Embedded;
using Xunit;

namespace TechieDesk.Tests.Scheduling;

/// <summary>
/// The background helper installer (REQ-FN-042 / BRD-139) — the piece that decides whether a schedule
/// can run with the window closed.
/// </summary>
public sealed class SchedulerHelperTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "techiedesk-helper-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temporary directories the tests write into.</summary>
    public SchedulerHelperTests()
    {
        Directory.CreateDirectory(LaunchAgentsDirectory);
        Directory.CreateDirectory(DataDirectory);
    }

    private string LaunchAgentsDirectory => Path.Combine(root, "LaunchAgents");

    private string DataDirectory => Path.Combine(root, "data");

    /// <summary>Removes the temporary directories.</summary>
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// With no helper executable in the build, install refuses and writes nothing — an agent pointing
    /// at a missing binary would read as Installed and never run anything.
    /// </summary>
    [Fact]
    public async Task InstallRefusesWhenTheHelperIsNotInTheBuild()
    {
        var helper = Build(helperPath: null);

        var result = await helper.InstallAsync(SchedulerPreferences.Default);

        Assert.False(result.Succeeded);
        Assert.Contains("not present in this build", result.Message);
        Assert.False(File.Exists(helper.PlistPath));
    }

    /// <summary>Without a helper executable the state is Unavailable, not merely NotInstalled.</summary>
    [Fact]
    public void StateDistinguishesUnavailableFromNotInstalled()
    {
        Assert.Equal(SchedulerHelperStatus.Unavailable, Build(helperPath: null).GetState().Status);
        Assert.Equal(SchedulerHelperStatus.NotInstalled, Build(CreateFakeHelper()).GetState().Status);
    }

    /// <summary>A state that is not Installed says schedules do not run with the window closed.</summary>
    [Fact]
    public void NotInstalledMeansSchedulesDoNotRunWithTheWindowClosed()
    {
        var state = Build(CreateFakeHelper()).GetState();

        Assert.False(state.RunsWithWindowClosed);
        Assert.Contains("only while TechieDesk is open", state.Reason);
    }

    /// <summary>The state names the mechanism and the exact file it lives in, as the UI requires.</summary>
    [Fact]
    public void StateNamesTheMechanismAndItsLocation()
    {
        var state = Build(CreateFakeHelper()).GetState();

        Assert.Equal("launchd user agent", state.MechanismName);
        Assert.EndsWith("com.techiedesk.scheduler.plist", state.MechanismLocation);
    }

    /// <summary>The generated agent starts the located helper at login and keeps it alive after a crash.</summary>
    [Fact]
    public void ThePlistStartsTheHelperAtLoginAndRestartsItAfterACrash()
    {
        var plist = LaunchAgentSchedulerHelper.BuildPlist(
            "/Applications/TechieDesk.app/Contents/Helpers/TechieDeskScheduler",
            DataDirectory,
            SchedulerPreferences.Default);

        Assert.Contains("<key>Label</key>", plist);
        Assert.Contains("com.techiedesk.scheduler", plist);
        Assert.Contains("/Applications/TechieDesk.app/Contents/Helpers/TechieDeskScheduler", plist);
        Assert.Contains("<key>RunAtLoad</key>", plist);
        Assert.Contains("<key>SuccessfulExit</key>", plist);
    }

    /// <summary>
    /// The agent pins the data directory, so the helper cannot open a different database from the app
    /// — the REQ-FN-034 divergence, in a second process.
    /// </summary>
    [Fact]
    public void ThePlistPinsTheSameDataDirectoryTheAppUses()
    {
        var plist = LaunchAgentSchedulerHelper.BuildPlist(
            "/tmp/TechieDeskScheduler", DataDirectory, SchedulerPreferences.Default);

        // The directory must be the VALUE of the environment-variable key, not merely present
        // somewhere in the file — it also appears in the log path, which would satisfy a looser
        // assertion while the helper inherited no directory at all.
        var keyMarker = $"<key>{LaunchAgentSchedulerHelper.DataDirectoryEnvironmentVariable}</key>";
        var keyIndex = plist.IndexOf(keyMarker, StringComparison.Ordinal);
        Assert.True(keyIndex >= 0);
        var valueLine = plist[(keyIndex + keyMarker.Length)..].Split('\n')[1];
        Assert.Equal($"    <string>{DataDirectory}</string>", valueLine.TrimEnd('\r'));
    }

    /// <summary>
    /// The agent carries a .NET root when one is supplied, because launchd's environment is nearly
    /// empty and a framework-dependent helper cannot find the runtime without it.
    /// </summary>
    [Fact]
    public void ThePlistCarriesADotnetRootWhenOneIsSupplied()
    {
        var withRoot = LaunchAgentSchedulerHelper.BuildPlist(
            "/tmp/TechieDeskScheduler", DataDirectory, SchedulerPreferences.Default, "/Users/me/.dotnet");
        var withoutRoot = LaunchAgentSchedulerHelper.BuildPlist(
            "/tmp/TechieDeskScheduler", DataDirectory, SchedulerPreferences.Default, null);

        Assert.Contains("<key>DOTNET_ROOT</key>", withRoot);
        Assert.Contains("<string>/Users/me/.dotnet</string>", withRoot);

        // Omitted rather than emitted empty: a self-contained helper has no root, and an empty
        // DOTNET_ROOT would point its host at nothing.
        Assert.DoesNotContain("DOTNET_ROOT", withoutRoot);
    }

    /// <summary>A path containing XML-significant characters is escaped rather than breaking the plist.</summary>
    [Fact]
    public void ThePlistEscapesPathsContainingMarkupCharacters()
    {
        var plist = LaunchAgentSchedulerHelper.BuildPlist(
            "/tmp/Tools &amp; Helpers/TechieDeskScheduler", DataDirectory, SchedulerPreferences.Default);

        Assert.DoesNotContain("Tools &amp; Helpers", plist.Replace("&amp;amp;", "@"));
    }

    /// <summary>
    /// Uninstalling deletes the agent file, not just unloads it — a file left behind would reinstall
    /// itself at the next login.
    /// </summary>
    [Fact]
    public async Task UninstallDeletesTheAgentFile()
    {
        var helper = Build(CreateFakeHelper());
        await File.WriteAllTextAsync(helper.PlistPath, "<plist/>");

        var result = await helper.UninstallAsync();

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(helper.PlistPath));
    }

    /// <summary>Uninstalling when nothing is installed succeeds and says so.</summary>
    [Fact]
    public async Task UninstallWithNothingInstalledSaysSo()
    {
        var result = await Build(CreateFakeHelper()).UninstallAsync();

        Assert.True(result.Succeeded);
        Assert.Contains("no agent installed", result.Message);
    }

    /// <summary>The locator finds a helper sitting beside the application and reports null otherwise.</summary>
    [Fact]
    public void TheLocatorFindsAHelperBesideTheApplication()
    {
        var beside = Path.Combine(root, "app");
        Directory.CreateDirectory(beside);

        Assert.Null(new SchedulerHelperLocator(null, beside).Locate());

        File.WriteAllText(Path.Combine(beside, new SchedulerHelperLocator(null, beside).ExecutableName), "#!/bin/sh");
        Assert.NotNull(new SchedulerHelperLocator(null, beside).Locate());
    }

    /// <summary>A configured path wins over the search, so a developer build can be pointed at.</summary>
    [Fact]
    public void AConfiguredPathWinsOverTheSearch()
    {
        var configured = Path.Combine(root, "custom-helper");
        File.WriteAllText(configured, "#!/bin/sh");

        Assert.Equal(configured, new SchedulerHelperLocator(configured, root).Locate());
    }

    /// <summary>
    /// The helper host can load ONNX Runtime, which is what makes ingesting with the window closed
    /// possible at all (TR-RAG-025).
    /// </summary>
    /// <remarks>
    /// <para>This is the defect that held REQ-FN-042 open. The installer half of the requirement was
    /// complete and tested above, and none of it was worth anything: the helper is a plain
    /// <c>net10.0</c> process, and in a plain <c>net10.0</c> process every ingest died before it read
    /// a single message, because <c>Microsoft.ML.OnnxRuntime</c> declares its imports against the
    /// literal name <c>onnxruntime.dll</c> and nothing on macOS ever found the
    /// <c>libonnxruntime.dylib</c> the package ships.</para>
    /// <para>The assertion belongs beside the installer tests rather than only in the library's own
    /// suite because the claim being made is about <b>this kind of host</b> — no MAUI, no static
    /// linking, no app bundle — which is exactly what the test project and the helper both are. It
    /// loads the real native library; a stub cannot fail the way this failed.</para>
    /// </remarks>
    [Fact]
    public void TheHelperHostLoadsTheOnnxNativeLibrary()
    {
        var status = OnnxRuntimeProbe.Check();

        Assert.True(status.Loaded, status.Failure);
        Assert.NotEmpty(status.Providers);
    }

    /// <summary>
    /// A load that reported no usable execution provider would satisfy the check above and still not
    /// embed anything, so the CPU provider — the one guaranteed everywhere — is asserted by name.
    /// </summary>
    [Fact]
    public void TheHelperHostHasAnExecutionProviderThatCanRunInference()
    {
        var providers = OnnxRuntimeProbe.Check().Providers;

        Assert.Contains(providers, provider => provider.Contains("CPU", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The probe's own sentence names the providers, because that line in the helper's log is the
    /// evidence an operator has that this host can embed.
    /// </summary>
    /// <remarks>
    /// <c>Program.ReportEmbeddingCapability</c> writes exactly this text at startup. Asserting the
    /// shape of it keeps "the log says ONNX loaded" from degrading into a line that says nothing.
    /// </remarks>
    [Fact]
    public void TheProbeDescribesWhatItLoaded()
    {
        var status = OnnxRuntimeProbe.Check();

        Assert.Contains("loaded", status.Describe(), StringComparison.Ordinal);
        Assert.All(status.Providers, provider => Assert.Contains(provider, status.Describe(), StringComparison.Ordinal));
    }

    /// <summary>
    /// Probing twice is harmless, which is what lets the helper check at startup without caring
    /// whether the module initializer already ran.
    /// </summary>
    [Fact]
    public void TheProbeIsSafeToRepeat()
    {
        var first = OnnxRuntimeProbe.Check();
        var second = OnnxRuntimeProbe.Check();

        Assert.Equal(first.Loaded, second.Loaded);
        Assert.Equal(first.Providers, second.Providers);
    }

    private string CreateFakeHelper()
    {
        var path = Path.Combine(root, "TechieDeskScheduler");
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
        return path;
    }

    private LaunchAgentSchedulerHelper Build(string? helperPath) => new(
        new FakeHelperLocator(helperPath),
        NullLogger<LaunchAgentSchedulerHelper>.Instance,
        SchedulingText.Localize,
        LaunchAgentsDirectory,
        DataDirectory);
}
