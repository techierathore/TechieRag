using System.Reflection;

namespace TechieDesk.Services.Updates;

/// <summary>
/// Reports the entry assembly's informational version (REQ-FN-038b).
/// </summary>
/// <remarks>
/// The fallback for hosts with no MAUI platform — which in practice means the test project. It is
/// registered with <c>TryAdd</c> so the real <c>MauiAppVersionProvider</c> always wins on a packaged
/// build, exactly like <c>EphemeralSecretStore</c> defers to <c>OsCredentialStore</c>.
/// </remarks>
public sealed class AssemblyAppVersionProvider : IAppVersionProvider
{
    private readonly Lazy<(ReleaseVersion Parsed, string Raw)> version = new(Read);

    /// <inheritdoc />
    public ReleaseVersion Current => version.Value.Parsed;

    /// <inheritdoc />
    public string RawVersion => version.Value.Raw;

    private static (ReleaseVersion, string) Read()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AssemblyAppVersionProvider).Assembly;
        var raw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? assembly.GetName().Version?.ToString()
                  ?? "0.0.0";

        return ReleaseVersion.TryParse(raw, out var parsed) ? (parsed, raw) : (default, raw);
    }
}
