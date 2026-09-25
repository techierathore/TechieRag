using Microsoft.Extensions.Logging;
using TechieRag.Abstractions;

namespace TechieRag.Llm;

/// <summary>
/// The hook through which the <c>TechieRag.Local</c> package serves <see cref="LlmSource.Local"/>
/// (REQ-RAG-064 / BRD-104).
/// </summary>
/// <remarks>
/// <para><b>Why a registry.</b> The in-process model lives in its own package (ADR-014) because its
/// runtime is heavy and fast-moving; the core cannot reference it. So the core's
/// <see cref="LlmProviderFactory"/> arm and the builder's configuration arm ask this registry, and
/// <c>TechieRag.Local</c> fills it: <c>LocalLlm.Register()</c>, which every <c>UseLocalLlm()</c>
/// overload also calls.</para>
/// <para><b>No endpoint, no key.</b> The factory is given the model id (or null for the platform
/// default) and the logger factory, nothing else.</para>
/// </remarks>
public static class LocalLlmProviderRegistry
{
    private static Func<string?, ILoggerFactory?, ILlmProvider>? factory;

    /// <summary>Gets whether a local-model package has registered itself in this process.</summary>
    public static bool IsRegistered => Volatile.Read(ref factory) is not null;

    /// <summary>
    /// Registers the function that creates the local provider; the last registration wins.
    /// </summary>
    /// <param name="providerFactory">Creates a provider for a model id (null = the platform default).</param>
    /// <exception cref="ArgumentNullException"><paramref name="providerFactory"/> is null.</exception>
    public static void Register(Func<string?, ILoggerFactory?, ILlmProvider> providerFactory)
    {
        ArgumentNullException.ThrowIfNull(providerFactory);
        Volatile.Write(ref factory, providerFactory);
    }

    /// <summary>
    /// Creates the local provider for a model id.
    /// </summary>
    /// <param name="modelId">The model id; null or empty selects the platform default.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>The provider.</returns>
    /// <exception cref="InvalidOperationException">No local-model package has registered itself.</exception>
    public static ILlmProvider Create(string? modelId, ILoggerFactory? loggerFactory = null)
    {
        var registered = Volatile.Read(ref factory)
            ?? throw new InvalidOperationException(
                "LlmSource.Local needs the TechieRag.Local package. Reference it and call LocalLlm.Register() "
                + "once at start-up, or configure the model with UseLocalLlm().");
        return registered(string.IsNullOrWhiteSpace(modelId) ? null : modelId, loggerFactory);
    }
}
