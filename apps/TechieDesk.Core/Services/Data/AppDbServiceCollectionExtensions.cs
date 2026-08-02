using Microsoft.Extensions.Configuration;
using TechieDesk.Services.Data;
using TechieDesk.Services.Settings;

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

        // REQ-FN-041 (2026-07-26): IWorkspaceAssignmentRepository is gone. User↔workspace
        // membership does not exist on a single-user desktop install. The "WorkspaceAssignment"
        // table is left in the schema — dropping journaled DbUp migrations against a user's real
        // data is its own decision — but nothing reads or writes it any more.
        services.AddSingleton<IInstanceSettingRepository, InstanceSettingRepository>();
        services.AddSingleton<IEventLogRepository, EventLogRepository>();
        services.AddSingleton<IGdprRequestRepository, GdprRequestRepository>();
        services.AddSingleton<ILicenseCacheRepository, LicenseCacheRepository>();

        // REQ-UI-028: both of these are app-database services — the upload ceiling lives in
        // "InstanceSetting" and the settings audit trail lives in "EventLog" — so they are
        // registered with the rest of the data layer rather than being wired separately in the
        // desktop head.
        services.AddSingleton<IAppDefaultsStore, AppDefaultsStore>();
        services.AddSingleton<IAppSettingsChangeLog>(
            provider => new AppSettingsChangeLog(provider.GetRequiredService<IEventLogRepository>()));

        // Agents (REQ-UI-045) and the per-workspace skill catalogue (REQ-RAG-022). The registry
        // sits above both repositories because the rules it enforces — the undeletable built-in
        // @agent, handle uniqueness, and the catalogue∩agent intersection — span the two tables.
        services.AddSingleton<TechieDesk.Services.Agents.IAgentRepository, TechieDesk.Services.Agents.AgentRepository>();
        services.AddSingleton<TechieDesk.Services.Agents.IWorkspaceSkillRepository, TechieDesk.Services.Agents.WorkspaceSkillRepository>();
        services.AddSingleton<TechieDesk.Services.Agents.IAgentRegistry, TechieDesk.Services.Agents.AgentRegistry>();
        return services;
    }
}
