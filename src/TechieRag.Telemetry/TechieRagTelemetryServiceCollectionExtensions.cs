using Microsoft.Extensions.DependencyInjection;

namespace TechieRag.Telemetry;

/// <summary>
/// Registers the opt-in TechieRag OpenTelemetry exporters in a dependency-injection container
/// (REQ-RAG-036 / BRD-117).
/// </summary>
public static class TechieRagTelemetryServiceCollectionExtensions
{
    /// <summary>Registers a <see cref="TechieRagTelemetryPipeline"/> as a singleton.</summary>
    /// <param name="services">The container to add the pipeline to.</param>
    /// <param name="configure">
    /// Optional configuration. Omit it and nothing is exported: the defaults enable neither tracing
    /// nor metrics, so the registered pipeline is inert and opens no socket.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <para>The pipeline is built lazily, when it is first resolved, and the container disposes it.
    /// Resolve it once at startup — for example from the host's application-started hook — so the
    /// exporters are running before the first completion or retrieval happens.</para>
    /// <para>A non-loopback OTLP endpoint throws at resolve time unless
    /// <see cref="TechieRagTelemetryOptions.AllowRemoteEndpoint"/> is set; see
    /// <see cref="TechieRagTelemetryOptions.ValidateEndpoint"/>.</para>
    /// </remarks>
    public static IServiceCollection AddTechieRagTelemetry(
        this IServiceCollection services,
        Action<TechieRagTelemetryOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(_ =>
        {
            var options = new TechieRagTelemetryOptions();
            configure?.Invoke(options);
            return TechieRagTelemetryPipeline.Create(options);
        });

        return services;
    }
}
