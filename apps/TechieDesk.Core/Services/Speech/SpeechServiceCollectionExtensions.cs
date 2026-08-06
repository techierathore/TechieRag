using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TechieDesk.Services.Speech;

/// <summary>
/// Registers the speech services behind the composer's mic and read-aloud controls
/// (REQ-UI-035 / REQ-UI-036).
/// </summary>
public static class SpeechServiceCollectionExtensions
{
    /// <summary>
    /// Adds dictation and read-aloud to the service graph.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Both registrations use <c>TryAdd</c>, so a head that has already registered its platform
    /// implementation keeps it. Call this AFTER the platform registrations, exactly as
    /// <c>AddTechieDeskUpdates</c> is called after <c>MauiAppVersionProvider</c>: the fallbacks here
    /// report themselves unavailable, and one that silently won would disable dictation on a machine
    /// that supports it.
    /// </remarks>
    public static IServiceCollection AddTechieDeskSpeech(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IDictationService, UnsupportedDictationService>();
        services.TryAddSingleton<IReadAloudService, UnsupportedReadAloudService>();

        return services;
    }
}
