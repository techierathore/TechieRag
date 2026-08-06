using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TechieRag.Abstractions;

namespace TechieRag.Embedding;

/// <summary>
/// Embedding provider for the Google Gemini <c>embedContent</c> API (REQ-RAG-035).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gemini's embedding API shares nothing with the OpenAI shape: the model is
/// part of the URL, inputs are wrapped as <c>content.parts[].text</c>, vectors come back as
/// <c>embedding.values</c>, and batching uses a different method entirely
/// (<c>:batchEmbedContents</c>). It therefore needs its own implementation.</para>
/// <para><b>Task type:</b> Gemini's embedding models are asymmetric and accept a
/// <c>taskType</c>. Documents use <c>RETRIEVAL_DOCUMENT</c> and <see cref="EmbedQueryAsync"/> uses
/// <c>RETRIEVAL_QUERY</c>.</para>
/// <para><b>Credentials:</b> Gemini authenticates with an API key. It is sent in the
/// <c>x-goog-api-key</c> header rather than as a query-string parameter, so it cannot end up in a
/// proxy access log or an exception message that quotes the request URI.</para>
/// <para><b>Dependencies:</b> raw <see cref="HttpClient"/> and <c>System.Text.Json</c> only.</para>
/// </remarks>
public sealed class GoogleGeminiEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private const string DocumentTaskType = "RETRIEVAL_DOCUMENT";
    private const string QueryTaskType = "RETRIEVAL_QUERY";

    private readonly HttpClient httpClient;
    private readonly string apiVersion;
    private readonly bool ownsHttpClient;

    /// <inheritdoc/>
    public string Name => "GoogleGemini";

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
    /// Creates a Gemini embedding provider.
    /// </summary>
    /// <param name="apiKey">Google AI API key.</param>
    /// <param name="model">Embedding model, default <c>gemini-embedding-001</c>.</param>
    /// <param name="dimensions">Vector dimensionality, default 3072 for gemini-embedding-001.</param>
    /// <param name="endpoint">Endpoint override, default https://generativelanguage.googleapis.com.</param>
    /// <param name="apiVersion">API version segment, default <c>v1beta</c>.</param>
    /// <param name="timeoutSeconds">HTTP request timeout in seconds.</param>
    /// <exception cref="ArgumentException">Thrown when a required string argument is null or empty.</exception>
    public GoogleGeminiEmbeddingProvider(
        string apiKey,
        string model = "gemini-embedding-001",
        int dimensions = 3072,
        string? endpoint = null,
        string apiVersion = "v1beta",
        int timeoutSeconds = 60)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        ModelName = model;
        Dimensions = dimensions;
        this.apiVersion = apiVersion;

        httpClient = new HttpClient
        {
            BaseAddress = new Uri((endpoint ?? "https://generativelanguage.googleapis.com").TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-goog-api-key", apiKey);

        ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a Gemini embedding provider over a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">Pre-configured client; its <c>BaseAddress</c> must be set.</param>
    /// <param name="model">Embedding model name.</param>
    /// <param name="dimensions">Vector dimensionality.</param>
    /// <param name="apiVersion">API version segment.</param>
    /// <remarks>Test seam: lets a stubbed <see cref="HttpMessageHandler"/> intercept requests.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> is null.</exception>
    public GoogleGeminiEmbeddingProvider(
        HttpClient httpClient,
        string model = "gemini-embedding-001",
        int dimensions = 3072,
        string apiVersion = "v1beta")
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        this.httpClient = httpClient;
        ModelName = model;
        Dimensions = dimensions;
        this.apiVersion = apiVersion;
        ownsHttpClient = false;
    }

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var embeddings = await EmbedManyAsync([text], DocumentTaskType, cancellationToken).ConfigureAwait(false);
        return embeddings[0];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var list = texts.ToList();
        if (list.Count == 0) return [];

        return await EmbedManyAsync(list, DocumentTaskType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Embeds a search query using Gemini's <c>RETRIEVAL_QUERY</c> task type.
    /// </summary>
    /// <param name="text">The query text.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The query embedding vector.</returns>
    /// <remarks>A member of this class rather than of <see cref="IEmbeddingProvider"/>, so the
    /// published interface is not widened.</remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="text"/> is blank.</exception>
    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var embeddings = await EmbedManyAsync([text], QueryTaskType, cancellationToken).ConfigureAwait(false);
        return embeddings[0];
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (ownsHttpClient) httpClient.Dispose();
    }

    private async Task<IReadOnlyList<float[]>> EmbedManyAsync(
        IReadOnlyList<string> texts,
        string taskType,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var qualifiedModel = ModelName.StartsWith("models/", StringComparison.Ordinal) ? ModelName : $"models/{ModelName}";
        var requests = texts.Select(text => new Dictionary<string, object>
        {
            ["model"] = qualifiedModel,
            ["taskType"] = taskType,
            ["content"] = new Dictionary<string, object>
            {
                ["parts"] = new[] { new Dictionary<string, object> { ["text"] = text } }
            }
        }).ToList();

        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { ["requests"] = requests });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var path = $"/{apiVersion}/{qualifiedModel}:batchEmbedContents";
        var response = await httpClient.PostAsync(path, content, cancellationToken).ConfigureAwait(false);
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
    /// Reads the <c>embeddings[].values</c> arrays out of a Gemini batch response.
    /// </summary>
    /// <param name="responseJson">The raw response body.</param>
    /// <param name="expectedCount">How many vectors were requested.</param>
    /// <returns>The vectors in request order.</returns>
    /// <exception cref="InvalidOperationException">The response was unreadable or the wrong length.</exception>
    private static IReadOnlyList<float[]> ParseEmbeddings(string responseJson, int expectedCount)
    {
        using var document = JsonDocument.Parse(responseJson);

        if (!document.RootElement.TryGetProperty("embeddings", out var embeddings)
            || embeddings.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Google Gemini returned an unreadable embeddings response.");
        }

        var vectors = new List<float[]>(expectedCount);
        foreach (var embedding in embeddings.EnumerateArray())
        {
            if (!embedding.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Google Gemini returned an embedding with no values.");
            }

            var vector = new float[values.GetArrayLength()];
            var index = 0;
            foreach (var value in values.EnumerateArray())
            {
                vector[index++] = value.GetSingle();
            }

            vectors.Add(vector);
        }

        if (vectors.Count != expectedCount)
        {
            throw new InvalidOperationException($"Google Gemini returned {vectors.Count} embeddings for {expectedCount} inputs.");
        }

        return vectors;
    }
}
