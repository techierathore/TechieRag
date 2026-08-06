using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using TechieDesk.Resources;
using TechieDesk.Services.Localization;
using TechieDesk.Services.Scheduling;
using TechieDesk.Services.Scheduling.Authoring;
using TechieDesk.Services.Scheduling.Jobs;
using TechieDeskDb;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for the scheduling cluster — REQ-FN-042, REQ-FN-028, REQ-FN-020, REQ-UI-046.
/// </summary>
public static class SchedulingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the scheduler, the job runner, the run history and natural-language authoring.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration containing the <c>Scheduler</c> section.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// <para>Everything is a singleton. A schedule outlives any Blazor scope, the in-flight guard has
    /// to be one guard for the whole process, and the background helper hosts these same
    /// registrations with no Blazor scope at all.</para>
    /// <para><b>Job handlers are registered by whoever owns them.</b> This method registers the one
    /// built-in handler; the connector framework adds its own with a plain
    /// <c>AddSingleton&lt;IScheduledJobHandler, ...&gt;</c> and needs no change here (REQ-FN-020).</para>
    /// </remarks>
    public static IServiceCollection AddTechieDeskScheduling(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SchedulerOptions>(configuration.GetSection(SchedulerOptions.SectionName));

        // REQ-UI-055: the scheduling cluster returns sentences a person reads — the plain-language
        // schedule, the skip reasons, the helper's toasts — and composes most of them itself, so it
        // resolves resource keys through this delegate rather than handing bare keys to five call
        // sites. AddLocalization is idempotent (it registers with TryAdd), and calling it here is what
        // lets the BACKGROUND HELPER host resolve the same strings: TechieDeskScheduler's Program.cs
        // hosts this cluster with no Blazor and no appearance services, so nothing else would have
        // registered a localizer for it.
        services.AddLocalization();
        services.TryAddSingleton<LocalizeText>(provider =>
        {
            var localizer = provider.GetRequiredService<IStringLocalizer<AppStrings>>();
            return (key, arguments) =>
                arguments.Length == 0 ? localizer[key].Value : localizer[key, arguments].Value;
        });

        // TimeProvider is injected everywhere in this cluster rather than DateTime.UtcNow being
        // called, which is what makes DST transitions and week-long absences testable.
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IScheduleRepository, ScheduleRepository>();
        services.AddSingleton<IScheduleRunRepository, ScheduleRunRepository>();
        services.AddSingleton<ISchedulerPreferencesStore, SchedulerPreferencesStore>();

        services.AddSingleton<IJobRunner, JobRunner>();
        services.AddSingleton<IBackgroundJobService, BackgroundJobService>();
        services.AddSingleton<IScheduleService, ScheduleService>();

        services.AddSingleton<IRunEnvironmentProbe, DesktopRunEnvironmentProbe>();
        services.AddSingleton<RunConditionEvaluator>();
        services.AddSingleton<ISchedulerService, SchedulerService>();

        services.AddSingleton<ISchedulerHelperLocator, SchedulerHelperLocator>();
        if (DataDirectory.CurrentPlatform == DataDirectoryPlatform.Windows)
        {
            services.AddSingleton<ISchedulerHelper, WindowsSchedulerHelper>();
        }
        else
        {
            services.AddSingleton<ISchedulerHelper, LaunchAgentSchedulerHelper>();
        }

        services.AddSingleton<IScheduleInterpreter, ScheduleInterpreter>();

        // The one built-in action. Everything else that becomes schedulable registers itself.
        services.AddSingleton<IScheduledJobHandler, DatabaseMaintenanceJobHandler>();
        return services;
    }
}
