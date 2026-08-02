using Microsoft.Extensions.FileProviders;

namespace TechieDesk.Services.Hosting;

/// <summary>
/// <see cref="IHostEnvironment"/> for the desktop head (REQ-FN-035).
/// </summary>
/// <remarks>
/// A MAUI app is not built on the generic host, so nothing supplies an <see cref="IHostEnvironment"/>
/// — yet the AppManager transport-security checks and the authentication state provider both branch
/// on <c>IsDevelopment()</c>. Rather than rewrite those call sites (and lose the Development-only
/// guard that keeps the self-signed-certificate opt-out out of a shipped build), the desktop host
/// supplies the environment itself.
/// <para>
/// The environment name defaults to Production. That default is deliberate and load-bearing:
/// REQ-NFR-004 gates the "accept an untrusted AppManager certificate" opt-out on
/// <c>IsDevelopment()</c>, so a shipped desktop build must never report Development by accident.
/// </para>
/// </remarks>
public sealed class DesktopHostEnvironment : IHostEnvironment
{
    /// <summary>Creates a desktop host environment.</summary>
    /// <param name="applicationName">The application name.</param>
    /// <param name="contentRootPath">Absolute path to the application content root.</param>
    /// <param name="environmentName">
    /// The environment name. Defaults to <see cref="Environments.Production"/> — see the remarks on
    /// why this must not silently become Development in a shipped build.
    /// </param>
    public DesktopHostEnvironment(
        string applicationName,
        string contentRootPath,
        string? environmentName = null)
    {
        ApplicationName = applicationName;
        ContentRootPath = contentRootPath;
        EnvironmentName = string.IsNullOrWhiteSpace(environmentName)
            ? Environments.Production
            : environmentName;
        ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
    }

    /// <inheritdoc />
    public string ApplicationName { get; set; }

    /// <inheritdoc />
    public string EnvironmentName { get; set; }

    /// <inheritdoc />
    public string ContentRootPath { get; set; }

    /// <inheritdoc />
    public IFileProvider ContentRootFileProvider { get; set; }
}
