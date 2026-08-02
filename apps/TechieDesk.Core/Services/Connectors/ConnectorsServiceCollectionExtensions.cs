using Microsoft.Extensions.DependencyInjection.Extensions;
using TechieDesk.Services.Connectors;
using TechieDesk.Services.Scheduling;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for connector background jobs — REQ-FN-020, BRD-65.
/// </summary>
public static class ConnectorsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the connector job handler and the connector-facing view over the background job
    /// service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// <para><b>Call this AFTER <c>AddTechieDeskScheduling</c>.</b> Everything here composes over the
    /// scheduler that call registers; nothing here starts a timer, owns a queue or opens a run row on
    /// its own (ADR-009 — one scheduler, two possible hosts). The handler is registered exactly the
    /// way <c>AddTechieDeskScheduling</c>'s own remarks anticipate: a plain
    /// <c>AddSingleton&lt;IScheduledJobHandler, ...&gt;</c>, with no change to the scheduling
    /// cluster.</para>
    /// <para><b>The two seams are <c>TryAdd</c>.</b> <see cref="IConnectorResolver"/> and
    /// <see cref="IConnectorDocumentSink"/> are what the connector cluster and the host supply; a
    /// build that registers its own before this call keeps it. The defaults are a resolver that
    /// honestly reports "no connector types are installed" and a sink that ingests into the
    /// catalogue, so the app boots and the screen is truthful on a build where the connectors
    /// themselves are not wired yet.</para>
    /// </remarks>
    public static IServiceCollection AddTechieDeskConnectorJobs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IConnectorResolver, NoConnectorsResolver>();
        services.TryAddScoped<IConnectorDocumentSink, RagConnectorDocumentSink>();

        services.AddSingleton<IScheduledJobHandler, ConnectorJobHandler>();
        services.AddSingleton<IConnectorJobService, ConnectorJobService>();
        return services;
    }
}
