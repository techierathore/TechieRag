namespace TechieDesk.Services.Hosting;

/// <summary>
/// <see cref="IAppEnvironment"/> backed by an explicit content-root path.
/// </summary>
/// <remarks>
/// REQ-FN-035: this single implementation serves both heads. The Blazor Server host constructs it from
/// <c>IWebHostEnvironment.ContentRootPath</c>; the MAUI host constructs it from
/// <c>AppContext.BaseDirectory</c>, which is the app bundle's content root on Mac Catalyst and Windows.
/// Keeping the resolution at the composition root — rather than branching inside the consumers — is what
/// lets <c>TechieRagConfigService</c> and <c>TechieRagManager</c> stay host-agnostic.
/// </remarks>
public sealed class AppEnvironment : IAppEnvironment
{
    /// <summary>Creates an environment rooted at <paramref name="contentRootPath"/>.</summary>
    /// <param name="contentRootPath">Absolute path to the application content root.</param>
    /// <exception cref="ArgumentException">Thrown when the path is null or blank.</exception>
    public AppEnvironment(string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath))
        {
            throw new ArgumentException("A content root path is required.", nameof(contentRootPath));
        }

        ContentRootPath = contentRootPath;
    }

    /// <inheritdoc />
    public string ContentRootPath { get; }
}
