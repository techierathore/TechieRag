using Microsoft.Extensions.Configuration;
using TechieDesk.Services.Data;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for the TechieDesk app data-access layer (Dapper over
/// SQLite/PostgreSQL, BRD-102). Lives in the Microsoft.Extensions.DependencyInjection
/// namespace per the standard ASP.NET Core extension-method convention so callers
/// need no extra using directive.
/// </summary>
public static class AppDbServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IAppDbConnectionFactory"/> and all app repositories,
    /// binding <see cref="AppDbOptions"/> from the <c>AppDb</c> configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration containing the <c>AppDb</c> section.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddTechieDeskData(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppDbOptions>(configuration.GetSection(AppDbOptions.SectionName));
        services.AddSingleton<IAppDbConnectionFactory, AppDbConnectionFactory>();
        services.AddSingleton<IWorkspaceAssignmentRepository, WorkspaceAssignmentRepository>();
        services.AddSingleton<IInstanceSettingRepository, InstanceSettingRepository>();
        services.AddSingleton<IEventLogRepository, EventLogRepository>();
        services.AddSingleton<IGdprRequestRepository, GdprRequestRepository>();
        services.AddSingleton<ILicenseCacheRepository, LicenseCacheRepository>();
        return services;
    }
}
