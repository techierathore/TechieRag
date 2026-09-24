using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TechieRag.Agents.DependencyInjection;

/// <summary>Registers a TechieRag agent in a service collection (REQ-RAG-016 / BRD-84).</summary>
public static class TechieRagAgentServiceCollectionExtensions
{
    /// <summary>The service key of the keyed <see cref="AIAgent"/> registration.</summary>
    public const string AgentServiceKey = "techierag";

    /// <summary>
    /// Registers <see cref="ITechieRagAgent"/> (singleton) and a keyed <see cref="AIAgent"/>
    /// (<see cref="AgentServiceKey"/>), built from the container's <see cref="ITechieRag"/> and, when
    /// present, its <see cref="ILoggerFactory"/>.
    /// </summary>
    /// <param name="services">The service collection; <see cref="ITechieRag"/> must be registered (for example by <c>AddTechieRag</c>).</param>
    /// <param name="configure">Configures the builder: at least one <c>Use*</c> model method.</param>
    /// <returns>The same collection.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static IServiceCollection AddTechieRagAgent(this IServiceCollection services, Action<TechieRagAgentBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton<ITechieRagAgent>(provider =>
        {
            var builder = new TechieRagAgentBuilder(provider.GetRequiredService<ITechieRag>());
            if (provider.GetService<ILoggerFactory>() is { } loggerFactory)
            {
                builder.WithLogging(loggerFactory);
            }

            configure(builder);
            return builder.Build();
        });
        services.AddKeyedSingleton<AIAgent>(AgentServiceKey, (provider, _) => provider.GetRequiredService<ITechieRagAgent>().Agent);
        return services;
    }
}
