using Microsoft.Extensions.DependencyInjection.Extensions;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Connectors;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for saved connectors — REQ-RAG-019 (BRD-63) and REQ-RAG-020 (BRD-64).
/// </summary>
public static class ConnectorRegistrationExtensions
{
    /// <summary>
    /// Registers connector storage, credential resolution and the real
    /// <see cref="IConnectorResolver"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// <para><b>Call this AFTER <c>AddTechieDeskData</c> and BEFORE
    /// <c>AddTechieDeskConnectorJobs</c>.</b> It needs the app database's connection factory, and the
    /// job cluster registers its seams with <c>TryAdd</c> — so registering the real resolver first is
    /// exactly how the honest "no connector types are installed" default
    /// (<see cref="NoConnectorsResolver"/>) is replaced, with no change to the job cluster.</para>
    /// <para><b>Scoped, because a connector run is a scope.</b>
    /// <see cref="ConnectorJobHandler"/> opens one per run and lets it go again;
    /// <see cref="DatabaseConnectorResolver"/> disposes the HTTP clients it built when that scope
    /// ends. Making it a singleton would keep a self-hosted host's connection pool alive between
    /// runs and would outlive a credential rotation.</para>
    /// <para><b>The secret store is a floor, not an override.</b> The desktop head registers the real
    /// <c>OsCredentialStore</c> (Keychain / Credential Manager) long before this call; the
    /// <c>TryAdd</c> here only means a host without a platform store — the scheduler helper, the test
    /// project — still resolves, and resolves to a store that reports itself non-durable rather than
    /// to one that writes tokens to a plain file.</para>
    /// </remarks>
    public static IServiceCollection AddTechieDeskConnectors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISecretStore, EphemeralSecretStore>();

        services.AddSingleton<IConnectorRepository, ConnectorRepository>();
        services.AddSingleton<IConnectorDocumentMap, ConnectorDocumentMap>();
        services.AddSingleton<IConnectorSecretStore, ConnectorSecretStore>();

        services.AddScoped<IConnectorResolver, DatabaseConnectorResolver>();
        services.AddScoped<IConnectorRegistry, ConnectorRegistry>();
        return services;
    }
}
