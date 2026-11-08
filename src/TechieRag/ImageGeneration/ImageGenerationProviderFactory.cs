using TechieRag.Abstractions;

namespace TechieRag.ImageGeneration;

/// <summary>
/// Creates image providers from a <c>connector/model</c> route, the way <see cref="Llm.LlmProviderFactory"/> creates
/// chat providers (REQ-RAG-155 / BRD-213).
/// </summary>
/// <remarks>
/// <para>Connectors: <c>openai</c>, <c>gemini</c>, <c>venice</c>, <c>openai-compatible</c> (any service speaking OpenAI's
/// image format, REQ-RAG-156) and <c>azure-openai</c> (an Azure OpenAI deployment, REQ-RAG-157). The last two need an
/// endpoint: the <c>endpoint</c> argument, or an <c>@https://…</c> suffix on the route as the chat routes take it,
/// for example <c>openai-compatible/flux-dev@https://images.example.com/v1</c>. A bare model name resolves when it names one
/// service: <c>gpt-image-*</c> and <c>dall-e-*</c> are OpenAI, <c>gemini-*</c> is Gemini; any other bare name must
/// carry its connector.</para>
/// <para>The key is handed to the provider and held in memory only; nothing here stores it.</para>
/// </remarks>
/// <example>
/// <code>
/// var images = ImageGenerationProviderFactory.CreateForModel("openai/gpt-image-1", apiKey);
/// var image = await images.GenerateImageAsync("a lighthouse at dawn", ImageSize.Square1024);
/// await File.WriteAllBytesAsync("hero.png", image.Bytes);
/// </code>
/// </example>
public static class ImageGenerationProviderFactory
{
    /// <summary>The OpenAI connector's key.</summary>
    public const string OpenAIName = "openai";

    /// <summary>The Gemini connector's key.</summary>
    public const string GeminiName = "gemini";

    /// <summary>The Venice connector's key.</summary>
    public const string VeniceName = "venice";

    /// <summary>The connector for any service speaking OpenAI's image format at an endpoint the app gives (REQ-RAG-156).</summary>
    public const string OpenAICompatibleName = "openai-compatible";

    /// <summary>The Azure OpenAI connector; the route's model is the deployment name (REQ-RAG-157).</summary>
    public const string AzureOpenAIName = "azure-openai";

    /// <summary>Gets the connectors that generate images.</summary>
    public static IReadOnlyList<string> Connectors { get; } = [OpenAIName, GeminiName, VeniceName, OpenAICompatibleName, AzureOpenAIName];

    /// <summary>Creates the image provider a route names.</summary>
    /// <param name="route"><c>connector/model</c>, for example <c>venice/venice-sd35</c>, or a bare model that names one service; an
    /// <c>@https://…</c> suffix gives the endpoint, as on the chat routes.</param>
    /// <param name="apiKey">The API key; held in memory by the provider only.</param>
    /// <param name="endpoint">The service's base URL in place of the connector's; null keeps it.</param>
    /// <param name="requestTimeout">The provider's default call timeout; null for 120 seconds.</param>
    /// <returns>The provider; it is <see cref="IDisposable"/>.</returns>
    /// <exception cref="ArgumentException">The route or key is empty, or the endpoint is not an absolute http or https URL.</exception>
    /// <exception cref="InvalidOperationException">The route names no image connector, or names <c>openai-compatible</c> or
    /// <c>azure-openai</c> without an endpoint.</exception>
    public static IImageGenerationProvider CreateForModel(string route, string apiKey, string? endpoint = null, TimeSpan? requestTimeout = null)
        => CreateForModel(null, route, apiKey, endpoint, requestTimeout);

    /// <summary>Creates the provider a route names, sending through a given handler (tests).</summary>
    internal static IImageGenerationProvider CreateForModel(
        HttpMessageHandler? handler, string route, string apiKey, string? endpoint, TimeSpan? requestTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        var trimmed = route.Trim();
        var at = trimmed.IndexOf("@http", StringComparison.OrdinalIgnoreCase);
        if (at > 0)
        {
            endpoint ??= trimmed[(at + 1)..];
            trimmed = trimmed[..at];
        }

        var (connector, model) = Resolve(trimmed);
        return Create(handler, connector, apiKey, model, endpoint, requestTimeout);
    }

    /// <summary>Creates an image provider for a connector.</summary>
    /// <param name="connector"><c>openai</c>, <c>gemini</c> or <c>venice</c>.</param>
    /// <param name="apiKey">The API key; held in memory by the provider only.</param>
    /// <param name="model">The model; null for the connector's default.</param>
    /// <param name="endpoint">The service's base URL in place of the connector's; null keeps it.</param>
    /// <param name="requestTimeout">The provider's default call timeout; null for 120 seconds.</param>
    /// <returns>The provider; it is <see cref="IDisposable"/>.</returns>
    /// <exception cref="ArgumentException">The key is empty, or the endpoint is not an absolute http or https URL.</exception>
    /// <exception cref="InvalidOperationException">The connector does not generate images.</exception>
    public static IImageGenerationProvider Create(string connector, string apiKey, string? model = null, string? endpoint = null, TimeSpan? requestTimeout = null)
        => Create(null, connector, apiKey, model, endpoint, requestTimeout);

    /// <summary>Creates an image provider that sends through a given handler (tests).</summary>
    internal static IImageGenerationProvider Create(
        HttpMessageHandler? handler, string connector, string apiKey, string? model, string? endpoint, TimeSpan? requestTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connector);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        if (endpoint is not null
            && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException($"The endpoint '{endpoint}' is not an absolute http or https URL.", nameof(endpoint));
        }

        var name = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        return connector.Trim().ToLowerInvariant() switch
        {
            OpenAIName => new OpenAIImageGenerationProvider(
                handler, apiKey, name ?? OpenAIImageGenerationProvider.DefaultModel, endpoint, requestTimeout, OpenAIName),
            GeminiName or "google" => new GeminiImageGenerationProvider(handler, apiKey, name ?? GeminiImageGenerationProvider.DefaultModel, endpoint, requestTimeout),
            VeniceName => new VeniceImageGenerationProvider(handler, apiKey, name ?? VeniceImageGenerationProvider.DefaultModel, endpoint, requestTimeout),
            OpenAICompatibleName => new OpenAIImageGenerationProvider(
                handler, apiKey, name ?? RequireModel(OpenAICompatibleName), RequireEndpoint(endpoint, OpenAICompatibleName), requestTimeout, OpenAICompatibleName),
            AzureOpenAIName => new AzureOpenAIImageGenerationProvider(
                handler, apiKey, name ?? RequireModel(AzureOpenAIName), RequireEndpoint(endpoint, AzureOpenAIName), apiVersion: null, requestTimeout),
            _ => throw new InvalidOperationException(
                $"'{connector}' does not generate images through this library; the image connectors are {string.Join(", ", Connectors)}.")
        };
    }

    /// <summary>Refuses a connector that needs an endpoint when none was given.</summary>
    private static string RequireEndpoint(string? endpoint, string connector) =>
        endpoint ?? throw new InvalidOperationException(
            $"The '{connector}' image route needs an endpoint: pass endpoint, or end the route with @https://<your service>.");

    /// <summary>Refuses a connector that has no default model when none was given.</summary>
    private static string RequireModel(string connector) =>
        throw new InvalidOperationException(
            connector == AzureOpenAIName
                ? "The 'azure-openai' image route needs the deployment name: azure-openai/<deployment>."
                : $"The '{connector}' image route needs a model: {connector}/<model>.");

    /// <summary>Splits a route into connector and model.</summary>
    private static (string Connector, string? Model) Resolve(string route)
    {
        var slash = route.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0)
        {
            return (route[..slash], route[(slash + 1)..]);
        }

        if (route.StartsWith("gpt-image", StringComparison.OrdinalIgnoreCase) || route.StartsWith("dall-e", StringComparison.OrdinalIgnoreCase))
        {
            return (OpenAIName, route);
        }

        if (route.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
        {
            return (GeminiName, route);
        }

        if (Connectors.Contains(route, StringComparer.OrdinalIgnoreCase))
        {
            return (route, null);
        }

        throw new InvalidOperationException(
            $"'{route}' names no single image service; write it as connector/model, for example venice/{route} (connectors: {string.Join(", ", Connectors)}).");
    }
}
