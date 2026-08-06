using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechieRag.Abstractions;

namespace TechieRag.Embedding;

/// <summary>
/// Embedding provider for services that speak the OpenAI <c>/embeddings</c> shape — Voyage AI,
/// Mistral, and OpenAI-compatible gateways (REQ-RAG-035).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Voyage and Mistral differ from OpenAI only in base URL, model name and an
/// optional <c>input_type</c> hint, so they are one implementation with three factory methods rather
/// than three near-identical classes.</para>
/// <para><b>Real batching:</b> unlike <see cref="HttpEmbeddingProvider"/>, which posts one text at a
/// time to stay gentle with self-hosted containers, these are hosted APIs that accept an array and
/// bill per token. <see cref="EmbedBatchAsync"/> therefore sends the whole batch in one request and
/// reorders the response by its <c>index</c> field — the specification does not promise the
/// service returns them in order, and a mis-ordered batch would attach every embedding to the wrong
/// chunk, which retrieval would never reveal as an error.</para>
/// <para><b>Asymmetric embeddings:</b> Voyage and Cohere-style models embed a query differently from
/// a document. <see cref="IEmbeddingProvider"/> has no query-versus-document distinction and could
/// not gain one without breaking every implementer, so the document hint is the default and
/// <see cref="EmbedQueryAsync"/> is offered as an extra member on this concrete class.</para>
/// <para><b>Dependencies:</b> raw <see cref="HttpClient"/> and <c>System.Text.Json</c> only.</para>
/// </remarks>
public sealed class OpenAICompatibleEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient httpClient;
    private readonly string apiPath;
    private readonly string? documentInputType;
    private readonly string? queryInputType;
    private readonly bool ownsHttpClient;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public int Dimensions { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Revision 1: this provider's encoding has never changed. A built-in provider publishes a
    /// real signature because it KNOWS its own identity — leaving it to the interface default
    /// would report "unknown" and silently switch off staleness detection for every install that
    /// uses it, which is exactly the defect REQ-RAG-052 was raised for.
    /// </remarks>
    public string EmbeddingSignature => Models.EmbeddingStaleness.Signature(Name, ModelName);

    /// <inheritdoc/>
    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

    /// <summary>
    /// Creates a provider for an OpenAI-shaped embeddings endpoint.
    /// </summary>
    /// <param name="name">Display name for telemetry, e.g. "Voyage".</param>
    /// <param name="endpoint">Base URL, e.g. https://api.voyageai.com/v1.</param>
    /// <param name="apiKey">Bearer API key.</param>
    /// <param name="model">Embedding model name.</param>
    /// <param name="dimensions">Vector dimensionality the model produces.</param>
    /// <param name="apiPath">Path relative to the endpoint; defaults to <c>embeddings</c>.</param>
    /// <param name="documentInputType">Value sent as <c>input_type</c> when embedding documents, or null to omit it.</param>
    /// <param name="queryInputType">Value sent as <c>input_type</c> by <see cref="EmbedQueryAsync"/>, or null to omit it.</param>
    /// <param name="timeoutSeconds">HTTP request timeout in seconds.</param>
    /// <exception cref="ArgumentException">Thrown when a required string argument is null or empty.</exception>
    public OpenAICompatibleEmbeddingProvider(
        string name,
        string endpoint,
        string apiKey,
        string model,
        int dimensions,
        string? apiPath = null,
        string? documentInputType = null,
        string? queryInputType = null,
        int timeoutSeconds = 60)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(model);

        Name = name;
        ModelName = model;
        Dimensions = dimensions;
        this.apiPath = apiPath ?? "embeddings";
        this.documentInputType = documentInputType;
        this.queryInputType = queryInputType;

        httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        if (!string.IsNullOrEmpty(apiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a provider over a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Pre-configured client; its <c>BaseAddress</c> must be set.</param>
    /// <param name="name">Display name for telemetry.</param>
    /// <param name="model">Embedding model name.</param>
    /// <param name="dimensions">Vector dimensionality.</param>
    /// <param name="apiPath">Path relative to the base address.</param>
    /// <param name="documentInputType">Value sent as <c>input_type</c> for documents, or null to omit it.</param>
    /// <param name="queryInputType">Value sent as <c>input_type</c> for queries, or null to omit it.</param>
    /// <remarks>Test seam: lets a stubbed <see cref="HttpMessageHandler"/> intercept requests.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> is null.</exception>
    public OpenAICompatibleEmbeddingProvider(
        HttpClient httpClient,
        string name,
        string model,
        int dimensions,
        string? apiPath = null,
        string? documentInputType = null,
        string? queryInputType = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        this.httpClient = httpClient;
        Name = name;
        ModelName = model;
        Dimensions = dimensions;
        this.apiPath = apiPath ?? "embeddings";
        this.documentInputType = documentInputType;
        this.queryInputType = queryInputType;
        ownsHttpClient = false;
    }

    /// <summary>Creates a Voyage AI embedding provider.</summary>
    /// <param name="apiKey">Voyage API key.</param>
    /// <param name="model">Model name, default <c>voyage-3.5</c>.</param>
    /// <param name="dimensions">Vector dimensionality, default 1024.</param>
    /// <param name="endpoint">Endpoint override.</param>
    /// <returns>A configured provider.</returns>
    /// <remarks>Voyage models are asymmetric, so documents are embedded with <c>input_type=document</c>
    /// and <see cref="EmbedQueryAsync"/> uses <c>input_type=query</c>.</remarks>
    public static OpenAICompatibleEmbeddingProvider ForVoyage(
        string apiKey,
        string model = "voyage-3.5",
        int dimensions = 1024,
        string? endpoint = null) =>
        new("Voyage", endpoint ?? "https://api.voyageai.com/v1", apiKey, model, dimensions,
            documentInputType: "document", queryInputType: "query");

    /// <summary>Creates a Mistral embedding provider.</summary>
    /// <param name="apiKey">Mistral API key.</param>
    /// <param name="model">Model name, default <c>mistral-embed</c>.</param>
    /// <param name="dimensions">Vector dimensionality, default 1024.</param>
    /// <param name="endpoint">Endpoint override.</param>
    /// <returns>A configured provider.</returns>
    /// <remarks>Mistral embeddings are symmetric; no <c>input_type</c> is sent.</remarks>
    public static OpenAICompatibleEmbeddingProvider ForMistral(
        string apiKey,
        string model = "mistral-embed",
        int dimensions = 1024,
        string? endpoint = null) =>
        new("Mistral", endpoint ?? "https://api.mistral.ai/v1", apiKey, model, dimensions);

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var embeddings = await EmbedManyAsync([text], documentInputType, cancellationToken).ConfigureAwait(false);
        return embeddings[0];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var list = texts.ToList();
        if (list.Count == 0) return [];

        return await EmbedManyAsync(list, documentInputType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Embeds a search query, using the query-side input type where the model is asymmetric.
    /// </summary>
    /// <param name="text">The query text.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The query embedding vector.</returns>
    /// <remarks>
    /// Deliberately a member of this class rather than of <see cref="IEmbeddingProvider"/>: adding it
    /// to the interface would break every consumer that implements it, for a hint only some models
    /// use. Symmetric models return the same vector this way as through <see cref="EmbedAsync"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is blank.</exception>
    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var embeddings = await EmbedManyAsync([text], queryInputType ?? documentInputType, cancellationToken).ConfigureAwait(false);
        return embeddings[0];
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (ownsHttpClient) httpClient.Dispose();
    }

    private async Task<IReadOnlyList<float[]>> EmbedManyAsync(
        IReadOnlyList<string> texts,
        string? inputType,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var request = new Dictionary<string, object>
        {
            ["model"] = ModelName,
            ["input"] = texts
        };
        if (inputType is not null) request["input_type"] = inputType;

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(apiPath, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<EmbeddingsResponse>(responseJson)
            ?? throw new InvalidOperationException($"{Name} returned an unreadable embeddings response.");

        var data = parsed.Data;
        if (data is null || data.Count != texts.Count)
        {
            throw new InvalidOperationException(
                $"{Name} returned {data?.Count ?? 0} embeddings for {texts.Count} inputs.");
        }

        // Order by the service's index rather than trusting response order: a silently transposed
        // batch would pair every chunk with another chunk's vector, and nothing downstream could
        // detect it.
        var vectors = new float[texts.Count][];
        foreach (var item in data)
        {
            if (item.Index < 0 || item.Index >= vectors.Length || item.Embedding is null)
            {
                throw new InvalidOperationException($"{Name} returned an embedding with an out-of-range index.");
            }

            vectors[item.Index] = item.Embedding;
        }

        stopwatch.Stop();
        RaiseCompleted(texts, stopwatch.Elapsed, parsed.Usage?.TotalTokens);

        return vectors;
    }

    private void RaiseCompleted(IReadOnlyList<string> texts, TimeSpan duration, int? reportedTokens)
    {
        OnEmbeddingCompleted?.Invoke(this, new EmbeddingCompletedEventArgs
        {
            TokenCount = reportedTokens ?? texts.Sum(text => (int)Math.Ceiling(text.Length / 4.0)),
            TextCount = texts.Count,
            Duration = duration,
            ModelName = ModelName,
            ProviderName = Name
        });
    }

    private sealed class EmbeddingsResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingsDatum>? Data { get; set; }

        [JsonPropertyName("usage")]
        public EmbeddingsUsage? Usage { get; set; }
    }

    private sealed class EmbeddingsDatum
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    private sealed class EmbeddingsUsage
    {
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
