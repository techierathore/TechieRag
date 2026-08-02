using TechieDesk.Services.Backup;
using TechieDesk.Services.Updates;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the backup and restore surface (REQ-FN-046/047).
/// </summary>
public static class BackupServiceCollectionExtensions
{
    /// <summary>Adds <see cref="BackupService"/> to the container.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Scoped, matching every other page-facing service in this host — the MAUI head keeps one scope
    /// for the window's lifetime, so this behaves as a per-window singleton. The app version is
    /// resolved from <see cref="IAppVersionProvider"/> here rather than injected into the service,
    /// which keeps <see cref="BackupService"/> constructible from a test with a literal version and
    /// no platform seam.
    /// </remarks>
    public static IServiceCollection AddTechieDeskBackup(this IServiceCollection services)
    {
        services.AddScoped(provider => new BackupService(
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<ILogger<BackupService>>(),
            provider.GetService<IAppVersionProvider>()?.RawVersion ?? "unknown"));

        return services;
    }
}
