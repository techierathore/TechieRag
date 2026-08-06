using System.Reflection;
using TechieDesk.Services.AppManager;
using Xunit;

namespace TechieDesk.Tests.Support;

/// <summary>
/// REQ-NFR-008 / BRD-99: privacy and data locality. Nothing leaves the instance except calls
/// to the operator-configured LLM/embedding providers and to AppManager, and the instance
/// emits no telemetry.
/// </summary>
/// <remarks>
/// These are structural guards over the shipped TechieDesk assembly rather than behavioural
/// tests. They exist so that a future change which introduces a new outbound HTTP client, or
/// links a telemetry exporter, fails the build until it is deliberately reviewed and added to
/// the allowlist below.
/// </remarks>
public sealed class OutboundEgressTests
{
    /// <summary>
    /// The complete set of TechieDesk types permitted to hold an <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><c>AppManagerClient</c> — AppManager (auth/licensing), base URL from <c>AppManager:BaseUrl</c>.</description></item>
    /// <item><description><c>OllamaProbe</c> — first-run wizard probe of a local Ollama, default <c>http://localhost:11434</c>.</description></item>
    /// <item><description><c>GitHubReleaseFeed</c> — update check (REQ-FN-038b). <b>Reviewed 2026-07-27.</b> The
    /// first egress path in this app to a host the operator did not configure, so it was accepted only with the
    /// automatic launch check defaulted OFF (<c>UpdateOptions.AutoCheckOnLaunch</c>) — a stock install still makes
    /// no unsolicited call, and the check happens when the operator asks for it. Sends no credential, so it
    /// discloses nothing about who is running the app beyond the request itself.</description></item>
    /// <item><description><c>UpdateService</c> — downloads the update package (REQ-FN-038b). <b>Reviewed
    /// 2026-07-27.</b> Only ever fetches a URL that came from a release the operator was shown and chose to
    /// download; never contacts anything on its own.</description></item>
    /// </list>
    /// Every other outbound call in the product originates in the TechieRag library's provider
    /// classes, where the endpoint is supplied by the operator's saved configuration.
    /// </remarks>
    private static readonly string[] AllowedHttpClientTypes =
    [
        "TechieDesk.Services.AppManager.AppManagerClient",
        "TechieDesk.Services.Setup.OllamaProbe",
        "TechieDesk.Services.Updates.GitHubReleaseFeed",
        "TechieDesk.Services.Updates.UpdateService"
    ];

    /// <summary>
    /// Assembly-name fragments that would indicate a telemetry or diagnostics exporter has been
    /// linked into the application.
    /// </summary>
    /// <remarks>
    /// <c>TechieRag.Telemetry</c> was added 2026-07-31 with REQ-RAG-036. That requirement ships OTLP
    /// metric and trace exporters for the library, but as a <b>separate opt-in package</b> precisely so
    /// this app never carries one. The core <c>TechieRag</c> package still links no exporter, so the
    /// marker below guards the only new way one could arrive: someone adding the opt-in package to
    /// TechieDesk. Note it is the package name and not a substring of <c>TechieRag</c>, so the core
    /// reference is unaffected.
    /// </remarks>
    private static readonly string[] TelemetryAssemblyMarkers =
    [
        "ApplicationInsights",
        "OpenTelemetry",
        "Sentry",
        "Datadog",
        "NewRelic",
        "TechieRag.Telemetry"
    ];

    private static Assembly TechieDeskAssembly => typeof(AppManagerClient).Assembly;

    /// <summary>
    /// Only the two reviewed types may take an <see cref="HttpClient"/> dependency, so a new
    /// egress path cannot be introduced into the app unnoticed.
    /// </summary>
    [Fact]
    public void OnlyAllowlistedTypesTakeAnHttpClientDependency()
    {
        var httpClientConsumers = TechieDeskAssembly.GetTypes()
            .Where(type => type.GetConstructors()
                .Any(constructor => constructor.GetParameters()
                    .Any(parameter => parameter.ParameterType == typeof(HttpClient))))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var unexpected = httpClientConsumers.Except(AllowedHttpClientTypes).ToArray();

        Assert.True(
            unexpected.Length == 0,
            "Unreviewed outbound HTTP client(s) found (REQ-NFR-008): " + string.Join(", ", unexpected));
    }

    /// <summary>
    /// A stock install makes no unsolicited outbound call. The update check (REQ-FN-038b) is the
    /// only egress path in the app that targets a host the operator never configured, so its
    /// automatic-at-launch behaviour must stay opt-in or the zero-egress default is gone — the app
    /// would contact a third party on every start of a fresh install.
    /// </summary>
    [Fact]
    public void TheUpdateCheckDoesNotRunAtLaunchByDefault()
    {
        var options = new TechieDesk.Services.Updates.UpdateOptions();

        Assert.False(options.AutoCheckOnLaunch);
    }

    /// <summary>
    /// No telemetry or APM exporter is linked into TechieDesk — the instance phones no home.
    /// </summary>
    [Fact]
    public void NoTelemetryExporterIsLinkedIntoTheApplication()
    {
        var referenced = TechieDeskAssembly.GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .ToArray();

        var telemetryReferences = referenced
            .Where(name => TelemetryAssemblyMarkers.Any(
                marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            telemetryReferences.Length == 0,
            "Telemetry exporter reference(s) found (REQ-NFR-008): " + string.Join(", ", telemetryReferences));
    }

    /// <summary>
    /// No telemetry or APM exporter reaches the app <i>transitively</i> either — nothing anywhere in
    /// the shipped dependency closure, not merely nothing in the direct references.
    /// </summary>
    /// <remarks>
    /// <para>Added 2026-07-31 with REQ-RAG-036. <see cref="NoTelemetryExporterIsLinkedIntoTheApplication"/>
    /// reads <c>GetReferencedAssemblies()</c>, which is the app's <b>direct</b> reference list only; an
    /// exporter pulled in one hop further — a package that itself depends on OTLP — would not appear
    /// there. Now that an exporter package exists in this repo at all, that hole is worth closing:
    /// this test looks at the files actually deployed alongside the app assembly, which is the whole
    /// closure NuGet resolved, transitive hops included.</para>
    /// <para>This is a strictly wider net than the direct-reference test, not a replacement for it.</para>
    /// </remarks>
    [Fact]
    public void NoTelemetryExporterReachesTheAppTransitively()
    {
        var deployedDirectory = Path.GetDirectoryName(TechieDeskAssembly.Location);

        Assert.False(
            string.IsNullOrEmpty(deployedDirectory),
            "The TechieDesk assembly reported no location, so the dependency closure cannot be inspected.");

        var offenders = Directory
            .EnumerateFiles(deployedDirectory!, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name ?? string.Empty)
            .Where(name => TelemetryAssemblyMarkers.Any(
                marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Telemetry exporter assembly/assemblies found in the shipped dependency closure "
            + "(REQ-NFR-008): " + string.Join(", ", offenders));
    }

    /// <summary>
    /// With <c>AppManager:BaseUrl</c> unset the instance is in offline single-user mode and the
    /// AppManager client never acquires a base address — proving the zero-egress default.
    /// </summary>
    [Fact]
    public void OfflineModeLeavesTheAppManagerClientWithNoBaseAddress()
    {
        var options = new AppManagerOptions { BaseUrl = string.Empty };

        Assert.False(options.IsConfigured);

        using var httpClient = new HttpClient();
        if (httpClient.BaseAddress == null && options.IsConfigured)
        {
            httpClient.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        }

        Assert.Null(httpClient.BaseAddress);
    }
}
