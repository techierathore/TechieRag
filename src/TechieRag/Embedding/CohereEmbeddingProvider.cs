using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TechieRag.Abstractions;

namespace TechieRag.Embedding;

/// <summary>
/// Embedding provider for the Cohere <c>/v2/embed</c> API (REQ-RAG-035).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Cohere's embed API is not OpenAI-shaped — inputs go in <c>texts</c>, an
/// <c>input_type</c> is mandatory, and vectors come back grouped by embedding type as
/// <c>embeddings.float</c> — so it needs its own implementation rather than a base-URL change.</para>
/// <para><b>Asymmetric by design:</b> Cohere's models are trained so that a document and a query
/// about it embed differently. Documents use <c>search_document</c>; <see cref="EmbedQueryAsync"/>
/// uses <c>search_query</c>. Sending the wrong one measurably degrades retrieval, which is why the
/// hint is mandatory in the API rather than optional.</para>
/// <para><b>Order:</b> Cohere returns vectors positionally, matching the order of <c>texts</c>. The
/// count is checked so a short response fails loudly instead of misaligning chunks and vectors.</para>
/// <para><b>Dependencies:</b> raw <see cref="HttpClient"/> and <c>System.Text.Json</c> only.</para>
/// </remarks>
public sealed class CohereEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private const string DocumentInputType = "search_document";
    private const string QueryInputType = "search_query";

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    /// <inheritdoc/>
    public string Name => "Cohere";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public int Dimensions { get; }

    /// <inheritdoc/>
    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

    /// <summary>
    /// Creates a Cohere embedding provider.
    /// </summary>
    /// <param name="apiKey">Cohere API key.</param>
    /// <param name="model">Embedding model, default <c>embed-v4.0</c>.</param>
    /// <param name="dimensions">Vector dimensionality, default 1536 for embed-v4.0.</param>
    /// <param name="endpoint">Endpoint override, default https://api.cohere.com.</param>
    /// <param name="timeoutSeconds">HTTP request timeout in seconds.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="apiKey"/> is null or empty.</exception>
    public CohereEmbeddingProvider(
        string apiKey,
        string model = "embed-v4.0",
        int dimensions = 1536,
        string? endpoint = null,
        int timeoutSeconds = 60)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        ModelName = model;
        Dimensions = dimensions;

        httpClient = new HttpClient
        {
            BaseAddress = new Uri((endpoint ?? "https://api.cohere.com").TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a Cohere embedding provider over a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Pre-configured client; its <c>BaseAddress</c> must be set.</param>
    /// <param name="model">Embedding model name.</param>
    /// <param name="dimensions">Vector dimensionality.</param>
    /// <remarks>Test seam: lets a stubbed <see cref="HttpMessageHandler"/> intercept requests.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> is null.</exception>
    public CohereEmbeddingProvider(HttpClient httpClient, string model = "embed-v4.0", int dimensions = 1536)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        this.httpClient = httpClient;
        ModelName = model;
        Dimensions = dimensions;
        ownsHttpClient = false;
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var embeddings = await EmbedManyAsync([text], DocumentInputType, cancellationToken).ConfigureAwait(false);
        return embeddings[0];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var list = texts.ToList();
        if (list.Count == 0) return [];

        return await EmbedManyAsync(list, DocumentInputType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Embeds a search query using Cohere's <c>search_query</c> input type.
    /// </summary>
    /// <param name="text">The query text.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The query embedding vector.</returns>
    /// <remarks>
    /// A member of this class rather than of <see cref="IEmbeddingProvider"/>, so the published
    /// interface is not widened for a hint only some models use.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is blank.</exception>
    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var embeddings = await EmbedManyAsync([text], QueryInputType, cancellationToken).ConfigureAwait(false);
        return embeddings[0];
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (ownsHttpClient) httpClient.Dispose();
    }

    private async Task<IReadOnlyList<float[]>> EmbedManyAsync(
        IReadOnlyList<string> texts,
        string inputType,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var request = new Dictionary<string, object>
        {
            ["model"] = ModelName,
            ["texts"] = texts,
            ["input_type"] = inputType,
            ["embedding_types"] = new[] { "float" }
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync("/v2/embed", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var vectors = ParseEmbeddings(responseJson, texts.Count);

        stopwatch.Stop();
        OnEmbeddingCompleted?.Invoke(this, new EmbeddingCompletedEventArgs
        {
            TokenCount = texts.Sum(text => (int)Math.Ceiling(text.Length / 4.0)),
            TextCount = texts.Count,
            Duration = stopwatch.Elapsed,
            ModelName = ModelName,
            ProviderName = Name
        });

        return vectors;
    }

    /// <summary>
    /// Reads the <c>embeddings.float</c> array out of a Cohere response.
    /// </summary>
    /// <param name="responseJson">The raw response body.</param>
    /// <param name="expectedCount">How many vectors were requested.</param>
    /// <returns>The vectors in request order.</returns>
    /// <exception cref="InvalidOperationException">The response was unreadable or the wrong length.</exception>
    private static IReadOnlyList<float[]> ParseEmbeddings(string responseJson, int expectedCount)
    {
        using var document = JsonDocument.Parse(responseJson);

        if (!document.RootElement.TryGetProperty("embeddings", out var embeddings)
            || !embeddings.TryGetProperty("float", out var floats)
            || floats.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Cohere returned an unreadable embeddings response.");
        }

        var vectors = new List<float[]>(expectedCount);
        foreach (var vector in floats.EnumerateArray())
        {
            var values = new float[vector.GetArrayLength()];
            var index = 0;
            foreach (var value in vector.EnumerateArray())
            {
                values[index++] = value.GetSingle();
            }

            vectors.Add(values);
        }

        if (vectors.Count != expectedCount)
        {
            throw new InvalidOperationException($"Cohere returned {vectors.Count} embeddings for {expectedCount} inputs.");
        }

        return vectors;
    }
}
