using Microsoft.Extensions.DependencyInjection.Extensions;
using TechieDesk.Services.Flows;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for the no-code agent flow builder — REQ-UI-040 (BRD-92).
/// </summary>
public static class FlowRegistrationExtensions
{
    /// <summary>
    /// Registers durable flow storage and the workspace flow service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para><b>Call this AFTER <c>AddTechieDeskData</c> and <c>AddTechieDeskMcp</c>.</b> The
    /// repository needs the app database's connection factory and the service needs the workspace MCP
    /// surface for its tool names and its run-time tools.</para>
    /// <para><b>There is no in-memory alternative registered.</b> A flow the user re-composes on every
    /// launch is not a saved flow, so <see cref="IFlowRepository"/> resolves to
    /// <see cref="SqliteFlowRepository"/> and to nothing else.</para>
    /// </remarks>
    public static IServiceCollection AddTechieDeskFlows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IFlowRepository, SqliteFlowRepository>();
        services.AddScoped<IWorkspaceFlowService, WorkspaceFlowService>();
        return services;
    }
}
