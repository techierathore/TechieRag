using Microsoft.Extensions.DependencyInjection.Extensions;
using TechieDesk.Services.Agents.Mcp;
using TechieDesk.Services.Auth;
using TechieRag.Mcp;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for admin-registered MCP tool servers — REQ-RAG-023 (BRD-86).
/// </summary>
public static class McpRegistrationExtensions
{
    /// <summary>
    /// Registers durable MCP server storage, its credential store, and the workspace MCP service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para><b>Call this AFTER <c>AddTechieDeskData</c>.</b> The registry needs the app database's
    /// connection factory.</para>
    /// <para><b>The in-memory registry is deliberately never registered.</b>
    /// <see cref="IMcpServerRegistry"/> resolves to <see cref="SqliteMcpServerRegistry"/> and to
    /// nothing else. <c>InMemoryMcpServerRegistry</c> is documented by the library as
    /// process-lifetime storage; in a desktop application that means an administrator re-typing
    /// every MCP server on every launch, so it is not an acceptable default here and there is no
    /// <c>TryAdd</c> that could quietly leave it in place.</para>
    /// <para><b>One object, two interfaces.</b> The same singleton answers
    /// <see cref="IMcpServerRegistry"/> — what the library's agent extensions read — and
    /// <see cref="IMcpServerAdministration"/>, which is what the Agents screen reads. Registering
    /// two instances would mean two objects claiming to own one table.</para>
    /// <para><b>The secret store is a floor, not an override.</b> The desktop head registers the real
    /// <c>OsCredentialStore</c> long before this call; the <c>TryAdd</c> here only means a host
    /// without a platform store still resolves — and resolves to one that reports itself non-durable
    /// rather than to one that writes credentials to a plain file.</para>
    /// </remarks>
    public static IServiceCollection AddTechieDeskMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISecretStore, EphemeralSecretStore>();

        services.AddSingleton<IMcpSecretStore, McpSecretStore>();

        services.AddSingleton<SqliteMcpServerRegistry>();
        services.AddSingleton<IMcpServerRegistry>(
            provider => provider.GetRequiredService<SqliteMcpServerRegistry>());
        services.AddSingleton<IMcpServerAdministration>(
            provider => provider.GetRequiredService<SqliteMcpServerRegistry>());

        services.AddScoped<IWorkspaceMcpService, WorkspaceMcpService>();
        return services;
    }
}
