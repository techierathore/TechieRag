using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Install;
using TechieDeskDb;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for the install-identity and single-instance stack (REQ-FN-051). Lives in the
/// <c>Microsoft.Extensions.DependencyInjection</c> namespace per the standard extension convention.
/// </summary>
public static class InstallServiceCollectionExtensions
{
    /// <summary>
    /// Registers the install identity and, when a launch-time guard result is supplied, the
    /// single-instance state.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Application configuration, read once for the <c>AppDb:DataDirectory</c> override so the
    /// identity is scoped to the SAME directory as every other artefact (REQ-FN-034/037).
    /// </param>
    /// <param name="singleInstance">
    /// The result of the launch-time <see cref="SingleInstanceGuard.TryAcquire"/> call, or null for
    /// hosts that do not guard (the migration console, the scheduler helper, the test project).
    /// </param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Everything is registered with <c>TryAdd</c> and everything is lazy. An install that never
    /// signs in never computes an identity and never writes <c>install-identity.json</c> — BRD-129
    /// makes the account-free launch the normal case, and this requirement must not add a single
    /// byte of work to it.
    /// </para>
    /// <para>
    /// The data directory is captured here rather than injected as <see cref="IConfiguration"/>,
    /// because not every host registers configuration in the container.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddTechieDeskInstallIdentity(
        this IServiceCollection services,
        IConfiguration configuration,
        SingleInstanceResult? singleInstance = null)
    {
        var dataDirectory = DataDirectory.Resolve(configuration[DataDirectory.ConfigKey]);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMachineFingerprintProvider>(provider =>
            new PlatformMachineFingerprintProvider(
                provider.GetService<ILogger<PlatformMachineFingerprintProvider>>()
                    ?? NullLogger<PlatformMachineFingerprintProvider>.Instance));
        services.TryAddSingleton<IProcessLiveness>(SystemProcessLiveness.Instance);
        services.TryAddSingleton<IInstallIdentityProvider>(provider => new InstallIdentityProvider(
            dataDirectory,
            provider.GetRequiredService<IMachineFingerprintProvider>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ILogger<InstallIdentityProvider>>()
                ?? NullLogger<InstallIdentityProvider>.Instance));

        if (singleInstance is not null)
        {
            services.TryAddSingleton(new SingleInstanceState(singleInstance));
        }

        return services;
    }
}
