using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TechieDesk.Services.Support;

/// <summary>
/// DI wiring for the Support screen's local services (REQ-UI-032/033/047, REQ-FN-027).
/// </summary>
/// <remarks>
/// The issue calls themselves need nothing registered — they go through the already-registered
/// <c>IAppManagerClient</c>. Only the attachment staging area, which touches the file system, needs
/// a service.
/// </remarks>
public static class SupportServiceCollectionExtensions
{
    /// <summary>Adds the support attachment store.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTechieDeskSupport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISupportAttachmentStore, SupportAttachmentStore>();
        return services;
    }
}
