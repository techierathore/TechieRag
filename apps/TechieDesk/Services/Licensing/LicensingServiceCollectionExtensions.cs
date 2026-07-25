using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechieDesk.Services.Licensing;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for the TechieDesk licensing stack (REQ-FN-013/014/015): the license
/// validation/status service, the feature gate over FeatureSvc, and the shared
/// <see cref="TechieDesk.Services.Licensing.LicensingOptions"/>. Lives in the
/// Microsoft.Extensions.DependencyInjection namespace per the standard extension convention.
/// </summary>
public static class LicensingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the license service and feature gate (both scoped, per circuit) and binds
    /// <see cref="TechieDesk.Services.Licensing.LicensingOptions"/> from the <c>AppManager</c>
    /// configuration section (so <c>AppManager:LicenseGraceHours</c> is honored). Ensures a
    /// <see cref="System.TimeProvider"/> is available for testable time.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration containing the <c>AppManager</c> section.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddTechieDeskLicensing(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LicensingOptions>(
            configuration.GetSection(LicensingOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ILicenseService, LicenseService>();
        services.AddScoped<IFeatureGate, FeatureGateService>();

        return services;
    }
}
