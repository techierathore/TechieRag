using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace TechieDesk.Services.Updates;

/// <summary>
/// Reports the packaged application's version from the platform (REQ-FN-038b).
/// </summary>
/// <remarks>
/// <para>The platform half of <see cref="IAppVersionProvider"/>. <c>AppInfo.VersionString</c> reads
/// <c>CFBundleShortVersionString</c> on macOS and the package version on Windows — the value the
/// packaging workflow stamped via <c>ApplicationDisplayVersion</c>. That matters for correctness, not
/// tidiness: the assembly-based fallback would report the assembly's own version, which nothing in
/// the release pipeline sets, so every install would believe it was running 1.0.0 and would be
/// offered an "update" to whatever was published.</para>
/// <para>Reading it is wrapped because <c>AppInfo</c> throws on a host with no platform application
/// object. An update check is not worth crashing the app over, so an unreadable version degrades to
/// 0.0.0, which makes the app offer an update rather than silently suppress one.</para>
/// </remarks>
public sealed class MauiAppVersionProvider : IAppVersionProvider
{
    private readonly Lazy<(ReleaseVersion Parsed, string Raw)> version;

    /// <summary>Initializes a new instance of the <see cref="MauiAppVersionProvider"/> class.</summary>
    /// <param name="logger">Diagnostics.</param>
    public MauiAppVersionProvider(ILogger<MauiAppVersionProvider> logger)
    {
        version = new Lazy<(ReleaseVersion, string)>(() => Read(logger));
    }

    /// <inheritdoc />
    public ReleaseVersion Current => version.Value.Parsed;

    /// <inheritdoc />
    public string RawVersion => version.Value.Raw;

    private static (ReleaseVersion, string) Read(ILogger logger)
    {
        string raw;
        try
        {
            raw = AppInfo.Current.VersionString;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The platform did not report an application version");
            return (default, "unknown");
        }

        if (ReleaseVersion.TryParse(raw, out var parsed))
        {
            return (parsed, raw);
        }

        logger.LogWarning("The platform reported an unparseable application version: {Version}", raw);
        return (default, raw);
    }
}
