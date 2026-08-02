using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using TechieDesk.Resources;
using TechieDesk.Services.Data;
using TechieDesk.Services.Localization;
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
        // REQ-UI-055: the registry's refusals are toasted by the agent editor, so it resolves a
        // LocalizeText. TryAdd and idempotent AddLocalization, so this composes with the identical
        // registration in AddTechieDeskScheduling / AddTechieDeskWebIngestion whichever runs first —
        // and it is what keeps AddTechieDeskData self-sufficient, which its own registration test
        // asserts.
        // AddLogging as well as AddLocalization: the resource-manager localizer factory takes an
        // ILoggerFactory, and both are TryAdd-based, so calling them here costs nothing in a host
        // that already registered logging and is the difference between self-sufficient and
        // "works only if somebody else went first".
        services.AddLogging();
        services.AddLocalization();
        services.TryAddSingleton<LocalizeText>(provider =>
        {
            var localizer = provider.GetRequiredService<IStringLocalizer<AppStrings>>();
            return (key, arguments) =>
                arguments.Length == 0 ? localizer[key].Value : localizer[key, arguments].Value;
        });

        services.AddSingleton<TechieDesk.Services.Agents.IAgentRepository, TechieDesk.Services.Agents.AgentRepository>();
        services.AddSingleton<TechieDesk.Services.Agents.IWorkspaceSkillRepository, TechieDesk.Services.Agents.WorkspaceSkillRepository>();
        services.AddSingleton<TechieDesk.Services.Agents.IAgentRegistry, TechieDesk.Services.Agents.AgentRegistry>();
        return services;
    }
}
