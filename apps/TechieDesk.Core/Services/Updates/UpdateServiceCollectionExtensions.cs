using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TechieDesk.Services.Updates;

/// <summary>
/// Registers the update check (REQ-FN-038b).
/// </summary>
public static class UpdateServiceCollectionExtensions
{
    /// <summary>Adds the update feed, preferences store and update service.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration carrying the <c>Updates</c> section.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTechieDeskUpdates(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<UpdateOptions>(configuration.GetSection(UpdateOptions.SectionName));

        // TryAdd so a host that supplies a real platform version provider wins; the assembly-based
        // one is the fallback for hosts with no MAUI, i.e. the test project.
        services.TryAddSingleton<IAppVersionProvider, AssemblyAppVersionProvider>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<UpdateLaunchState>();
        services.TryAddScoped<IUpdatePreferencesStore, UpdatePreferencesStore>();

        services.AddHttpClient<IUpdateFeed, GitHubReleaseFeed>((provider, client) =>
        {
            var options = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<UpdateOptions>>().Value;

            client.BaseAddress = new Uri(options.ApiBaseAddress);
            client.Timeout = TimeSpan.FromSeconds(20);

            // GitHub rejects requests with no User-Agent outright, and pinning the API version stops
            // a future default from changing the payload shape under a shipped build.
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TechieDesk", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });

        // A separate client for the package itself: downloads follow a redirect to a storage host
        // that is not the API, need no API headers, and must not inherit the API's short timeout —
        // an installer is hundreds of megabytes.
        services.AddHttpClient<IUpdateService, UpdateService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(30);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TechieDesk", "1.0"));
        });

        return services;
    }
}
