using Microsoft.Extensions.Logging;
using TechieRag.Abstractions;

namespace TechieRag.Llm;

/// <summary>
/// Builds an <see cref="ILlmProvider"/> for a named connector or a routed model (REQ-RAG-034).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One place that knows which provider implementation serves which connector,
/// so callers can go from a model name straight to a working provider.</para>
/// <para><b>No new provider classes:</b> every connector in
/// <see cref="LlmConnectorCatalog"/> other than Anthropic, Gemini and the two local runtimes is
/// OpenAI-compatible, so they are all served by the existing
/// <see cref="OpenAICompatibleLlmProvider"/> pointed at a different base URL. Adding a service is a
/// catalog row; it is not a class, an enum member and a switch arm.</para>
/// <para><b>Credentials:</b> the API key is passed to the provider and never logged or included in
/// an exception message.</para>
/// </remarks>
public static class LlmProviderFactory
{
    /// <summary>
    /// Creates a provider for a resolved route.
    /// </summary>
    /// <param name="route">The connector and model to use.</param>
    /// <param name="apiKey">The API key, or null/empty for local runtimes that need none.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="maxTokens">Default max output tokens, used by providers that require it up front.</param>
    /// <returns>A provider configured for the route.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="route"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the connector needs an API key and none was supplied.</exception>
    public static ILlmProvider Create(
        ModelRoute route,
        string? apiKey,
        ILoggerFactory? loggerFactory = null,
        int maxTokens = 2048)
    {
        ArgumentNullException.ThrowIfNull(route);

        var connector = route.Connector;
        if (connector.RequiresApiKey && string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException($"An API key is required for the '{connector.Name}' connector.");
        }

        return connector.Source switch
        {
            LlmSource.Anthropic => new AnthropicLlmProvider(
                apiKey!,
                route.ModelId,
                connector.Endpoint,
                maxTokens,
                loggerFactory?.CreateLogger<AnthropicLlmProvider>()),

            LlmSource.GoogleGemini => new GoogleGeminiLlmProvider(
                apiKey!,
                route.ModelId,
                connector.Endpoint,
                loggerFactory?.CreateLogger<GoogleGeminiLlmProvider>()),

            LlmSource.Ollama => new OllamaLlmProvider(
                connector.Endpoint ?? "http://localhost:11434",
                route.ModelId,
                loggerFactory?.CreateLogger<OllamaLlmProvider>()),

            LlmSource.LmStudio => new LmStudioLlmProvider(
                connector.Endpoint ?? "http://localhost:1234",
                route.ModelId,
                loggerFactory?.CreateLogger<LmStudioLlmProvider>()),

            LlmSource.OpenAICompatible => new OpenAICompatibleLlmProvider(
                connector.Endpoint ?? throw new InvalidOperationException($"Connector '{connector.Name}' has no endpoint."),
                apiKey ?? string.Empty,
                route.ModelId,
                loggerFactory?.CreateLogger<OpenAICompatibleLlmProvider>()),

            _ => throw new InvalidOperationException($"Connector '{connector.Name}' has no provider implementation.")
        };
    }

    /// <summary>
    /// Routes a model name to a service and creates the provider for it.
    /// </summary>
    /// <param name="modelName">A bare model name, or <c>connector/model</c>.</param>
    /// <param name="apiKey">The API key, or null/empty for local runtimes that need none.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="maxTokens">Default max output tokens.</param>
    /// <returns>A provider for the service the model name resolves to.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the model name identifies no single service.</exception>
    public static ILlmProvider CreateForModel(
        string modelName,
        string? apiKey,
        ILoggerFactory? loggerFactory = null,
        int maxTokens = 2048) =>
        Create(ModelRouter.Require(modelName), apiKey, loggerFactory, maxTokens);
}
