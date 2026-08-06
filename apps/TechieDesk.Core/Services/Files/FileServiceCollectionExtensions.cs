using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TechieDesk.Services.Files;

/// <summary>
/// Registers the native file-save abstraction behind thread export (REQ-FN-010).
/// </summary>
public static class FileServiceCollectionExtensions
{
    /// <summary>
    /// Adds the file-save fallback to the service graph.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Uses <c>TryAdd</c>, so a head that already registered its platform save panel keeps it. Call
    /// this AFTER the platform registration — same ordering rule as <c>AddTechieDeskSpeech</c>.
    /// </remarks>
    public static IServiceCollection AddTechieDeskFileSave(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IFileSaveService, UnsupportedFileSaveService>();

        return services;
    }
}
