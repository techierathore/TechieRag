using System.Net.Http.Headers;
using System.Text.Json;

namespace TechieRag.Llm;

/// <summary>
/// Asks a connector's service which models it serves (REQ-RAG-126, Lekhak TR-RAG-004).
/// </summary>
/// <remarks>
/// <para><b>One call per service family:</b> LM Studio answers <c>GET /v1/models</c>, Ollama
/// <c>GET /api/tags</c>, every OpenAI-compatible connector <c>GET {endpoint}/models</c> with the
/// key as a bearer token, and an Anthropic-compatible one <c>GET {endpoint}/v1/models</c> with
/// <c>x-api-key</c> (REQ-RAG-130; a service without that call answers 404, which throws). All map onto
/// the same list of ids.</para>
/// <para><b>A failure throws:</b> an unreachable or refusing service raises
/// <see cref="HttpRequestException"/> rather than returning an empty list, so a host never shows "no
/// models" for a server that is down. The API key never appears in a message.</para>
/// </remarks>
internal static class LlmModelLister
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Lists the model ids a connector's service reports.</summary>
    /// <param name="connector">The connector, its endpoint already resolved.</param>
    /// <param name="apiKey">The API key, or null for a service that needs none.</param>
    /// <param name="handler">The HTTP handler; null uses the default network stack (tests pass a stub).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The model ids, distinct and sorted ordinally.</returns>
    /// <exception cref="InvalidOperationException">The connector needs a key and none was given, or it has no endpoint.</exception>
    /// <exception cref="NotSupportedException">The connector's service has no listing call this library knows.</exception>
    /// <exception cref="HttpRequestException">The service could not be reached or refused the request.</exception>
    public static async Task<IReadOnlyList<string>> ListAsync(
        LlmConnectorDescriptor connector,
        string? apiKey,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        if (connector.RequiresApiKey && string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException($"An API key is required for the '{connector.Name}' connector.");
        }

        var (path, read) = connector.Source switch
        {
            LlmSource.LmStudio => ("v1/models", (Func<JsonElement, IEnumerable<string>>)ReadOpenAIModels),
            LlmSource.OpenAICompatible => ("models", ReadOpenAIModels),
            LlmSource.Ollama => ("api/tags", ReadOllamaModels),
            // REQ-RAG-130: Anthropic's GET /v1/models answers in the same {"data":[{"id":…}]} shape.
            LlmSource.Anthropic => ("models", ReadOpenAIModels),
            _ => throw new NotSupportedException(
                $"Listing models is not supported for the '{connector.Name}' connector; it covers LM Studio, Ollama, OpenAI-compatible and Anthropic-compatible connectors.")
        };

        if (connector.RequiresEndpoint && string.IsNullOrWhiteSpace(connector.Endpoint))
        {
            throw LlmProviderFactory.EndpointRequired(connector, modelId: null);
        }

        var endpoint = connector.Endpoint
            ?? throw new InvalidOperationException($"Connector '{connector.Name}' has no endpoint.");
        var baseUri = connector.Source == LlmSource.Anthropic
            ? AnthropicLlmProvider.VersionedBase(endpoint)
            : new Uri(endpoint.TrimEnd('/') + "/");
        using var httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        httpClient.Timeout = Timeout;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
        request.Headers.TryAddWithoutValidation("User-Agent", OpenAICompatibleLlmProvider.DefaultUserAgent);
        if (connector.Source == LlmSource.Anthropic)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicLlmProvider.ApiVersion);
        }
        else if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        LlmHttpGuard.EnsureSuccess(response);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        return read(document.RootElement)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads <c>{"data":[{"id":"…"}]}</c>, the OpenAI and LM Studio shape.</summary>
    /// <param name="root">The response body.</param>
    /// <returns>The ids.</returns>
    private static IEnumerable<string> ReadOpenAIModels(JsonElement root) =>
        ReadArray(root, "data", "id");

    /// <summary>Reads <c>{"models":[{"name":"…"}]}</c>, Ollama's <c>/api/tags</c> shape.</summary>
    /// <param name="root">The response body.</param>
    /// <returns>The model names, as <c>ollama/&lt;name&gt;</c> routes take them.</returns>
    private static IEnumerable<string> ReadOllamaModels(JsonElement root) =>
        ReadArray(root, "models", "name");

    private static IEnumerable<string> ReadArray(JsonElement root, string arrayName, string idName)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(arrayName, out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty(idName, out var id)
                && id.ValueKind == JsonValueKind.String)
            {
                yield return id.GetString()!;
            }
        }
    }
}
