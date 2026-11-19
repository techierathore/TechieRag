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
/// <para><b>Details (REQ-RAG-166):</b> the same one call also yields each model's published price, data handling
/// and reasoning, read by <see cref="ModelDetailsReader"/>; the id list is the details' ids.</para>
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
        CancellationToken cancellationToken) =>
        (await ListDetailsAsync(connector, apiKey, handler, cancellationToken).ConfigureAwait(false))
            .Select(model => model.Id)
            .ToList();

    /// <summary>
    /// Lists the models a connector's service reports, each with the price, data handling and reasoning the
    /// service publishes for it (REQ-RAG-166, Lekhak TR-RAG-011).
    /// </summary>
    /// <param name="connector">The connector, its endpoint already resolved.</param>
    /// <param name="apiKey">The API key, or null for a service that needs none.</param>
    /// <param name="handler">The HTTP handler; null uses the default network stack (tests pass a stub).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One entry per distinct id, sorted ordinally by id; the first entry the service gave for an id wins.</returns>
    /// <exception cref="InvalidOperationException">The connector needs a key and none was given, or it has no endpoint.</exception>
    /// <exception cref="NotSupportedException">The connector's service has no listing call this library knows.</exception>
    /// <exception cref="HttpRequestException">The service could not be reached or refused the request.</exception>
    public static async Task<IReadOnlyList<LlmModelDetails>> ListDetailsAsync(
        LlmConnectorDescriptor connector,
        string? apiKey,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        if (connector.RequiresApiKey && string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException($"An API key is required for the '{connector.Name}' connector.");
        }

        var (path, arrayName, idName) = connector.Source switch
        {
            LlmSource.LmStudio => ("v1/models", "data", "id"),
            LlmSource.OpenAICompatible => ("models", "data", "id"),
            LlmSource.Ollama => ("api/tags", "models", "name"),
            // REQ-RAG-130: Anthropic's GET /v1/models answers in the same {"data":[{"id":…}]} shape.
            LlmSource.Anthropic => ("models", "data", "id"),
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
        await LlmHttpGuard.EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);

        // LM Studio and Ollama run models on a machine the host points at, never at a hosted service.
        var local = connector.Source is LlmSource.LmStudio or LlmSource.Ollama;
        return ReadArray(document.RootElement, arrayName, idName)
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .DistinctBy(item => item.Id, StringComparer.Ordinal)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => ModelDetailsReader.Read(item.Id, item.Element, local))
            .ToList();
    }

    private static IEnumerable<(string Id, JsonElement Element)> ReadArray(JsonElement root, string arrayName, string idName)
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
                yield return (id.GetString()!, item);
            }
        }
    }
}
