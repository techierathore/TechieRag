using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechieRag.Abstractions;

namespace TechieRag.Embedding;

/// <summary>
/// Embedding provider implementation for Azure OpenAI Service.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Generates text embeddings using Azure OpenAI Service,
/// providing enterprise-grade cloud embedding capabilities with Azure security and compliance.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when EmbeddingSource.AzureOpenAI is configured.
/// Called by TechieRagClient during ingestion (to embed chunks) and search (to embed queries).</para>
/// <para><b>API:</b> <c>POST {endpoint}/openai/deployments/{deployment}/embeddings?api-version=2024-10-21</c> with the
/// key in the <c>api-key</c> header, over a raw <see cref="HttpClient"/> and <c>System.Text.Json</c> like every other
/// provider (ADR-003). Until 1.3.0 this class used the Azure.AI.OpenAI SDK, which sent the same request; dropping it
/// took the SDK and its OpenAI and System.ClientModel libraries out of the core package (REQ-RAG-168, TrStudio
/// TR-RAG-006).</para>
/// <para><b>Dependencies:</b> Requires Azure OpenAI resource with deployed embedding model.</para>
/// </remarks>
public class AzureOpenAIEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    /// <summary>The Azure OpenAI data-plane API version this provider sends; the one Azure.AI.OpenAI 2.1.0 sent.</summary>
    public const string ApiVersion = "2024-10-21";

    /// <inheritdoc/>
    public string Name => "Azure OpenAI";

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

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly Uri embeddingsUri;
    private readonly string apiKey;
    private bool disposed;

    /// <inheritdoc/>
    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

    /// <summary>
    /// Creates a new Azure OpenAI embedding provider instance.
    /// </summary>
    /// <param name="endpoint">Azure OpenAI endpoint URL (e.g., https://your-resource.openai.azure.com).</param>
    /// <param name="apiKey">API key for authentication.</param>
    /// <param name="deploymentName">Name of the deployed embedding model.</param>
    /// <param name="dimensions">Vector dimensions (default: 1536 for text-embedding-ada-002, 3072 for text-embedding-3-large).</param>
    /// <exception cref="ArgumentException">Thrown when endpoint, apiKey, or deploymentName is null or empty, or the endpoint is not an absolute URL.</exception>
    /// <remarks>
    /// <para><b>Common Models:</b></para>
    /// <list type="bullet">
    /// <item>text-embedding-ada-002: 1536 dimensions</item>
    /// <item>text-embedding-3-small: 1536 dimensions (default)</item>
    /// <item>text-embedding-3-large: 3072 dimensions</item>
    /// </list>
    /// </remarks>
    public AzureOpenAIEmbeddingProvider(string endpoint, string apiKey, string deploymentName, int dimensions = 1536)
        : this(endpoint, apiKey, deploymentName, dimensions, handler: null)
    {
    }

    /// <summary>Creates the provider over a given HTTP handler; tests pass a stub.</summary>
    /// <param name="endpoint">Azure OpenAI endpoint URL.</param>
    /// <param name="apiKey">API key for authentication.</param>
    /// <param name="deploymentName">Name of the deployed embedding model.</param>
    /// <param name="dimensions">Vector dimensions.</param>
    /// <param name="handler">The handler, or null for the default network stack.</param>
    internal AzureOpenAIEmbeddingProvider(string endpoint, string apiKey, string deploymentName, int dimensions, HttpMessageHandler? handler)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(deploymentName))
            throw new ArgumentException("Deployment name cannot be null or empty.", nameof(deploymentName));
        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            throw new ArgumentException("Endpoint must be an absolute URL.", nameof(endpoint));

        ModelName = deploymentName;
        Dimensions = dimensions;
        this.apiKey = apiKey;
        embeddingsUri = new Uri(baseUri, $"openai/deployments/{Uri.EscapeDataString(deploymentName)}/embeddings?api-version={ApiVersion}");
        httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        httpClient.Timeout = DefaultTimeout;
        ownsHttpClient = true;
    }

    /// <summary>
    /// Generates an embedding vector for a single text string.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A float array representing the embedding vector.</returns>
    /// <exception cref="ArgumentException">Thrown when text is null or empty.</exception>
    /// <exception cref="HttpRequestException">Thrown when the Azure OpenAI request fails; <see cref="Models.EmbeddingUnavailableException"/> when the service cannot be reached.</exception>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));

        var vectors = await EmbedBatchAsync([text], cancellationToken).ConfigureAwait(false);
        return vectors[0];
    }

    /// <summary>
    /// Generates embedding vectors for multiple texts in a single batch request.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of float arrays, one per input text, in the same order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when texts is null.</exception>
    /// <exception cref="HttpRequestException">Thrown when the Azure OpenAI request fails; <see cref="Models.EmbeddingUnavailableException"/> when the service cannot be reached.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the service answers with an unreadable or incomplete body.</exception>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ObjectDisposedException.ThrowIf(disposed, this);

        var textList = texts.ToList();
        if (textList.Count == 0)
            return Array.Empty<float[]>();

        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, embeddingsUri)
        {
            Content = JsonContent.Create(new EmbeddingRequest(textList))
        };
        request.Headers.TryAddWithoutValidation("api-key", apiKey);

        using var response = await EmbeddingFailures.SendAsync(httpClient, request, Name, embeddingsUri, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Azure OpenAI embedding request failed: {(int)response.StatusCode} ({response.ReasonPhrase}). {Trim(body)}",
                inner: null,
                response.StatusCode);
        }

        EmbeddingResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Azure OpenAI returned an invalid embedding response.", exception);
        }

        if (payload?.Data is not { } data || data.Count != textList.Count || data.Any(item => item.Embedding is null))
        {
            throw new InvalidOperationException("Azure OpenAI returned an invalid batch embedding response.");
        }

        var ordered = data.OrderBy(item => item.Index).Select(item => item.Embedding!).ToList();
        stopwatch.Stop();
        RaiseEmbeddingCompleted(string.Concat(textList), textList.Count, stopwatch.Elapsed, payload.Usage?.PromptTokens);
        return ordered;
    }

    /// <summary>Keeps an error body short enough for a message.</summary>
    private static string Trim(string body) => body.Length <= 500 ? body : body[..500] + "…";

    /// <summary>Raises the OnEmbeddingCompleted event with usage metrics.</summary>
    /// <param name="text">The text(s) that were embedded.</param>
    /// <param name="textCount">Number of texts in the batch.</param>
    /// <param name="duration">Time taken.</param>
    /// <param name="actualTokenCount">The token count the service reported, if any.</param>
    private void RaiseEmbeddingCompleted(string text, int textCount, TimeSpan duration, int? actualTokenCount)
    {
        OnEmbeddingCompleted?.Invoke(this, new EmbeddingCompletedEventArgs
        {
            TokenCount = actualTokenCount ?? EstimateTokenCount(text),
            TextCount = textCount,
            Duration = duration,
            ModelName = ModelName,
            ProviderName = Name
        });
    }

    /// <summary>Estimates token count when the service reports none (about 4 characters a token).</summary>
    /// <param name="text">The text.</param>
    /// <returns>Estimated token count.</returns>
    private static int EstimateTokenCount(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    /// <summary>
    /// Disposes the provider and its HTTP client.
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ownsHttpClient) httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record EmbeddingRequest([property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingItem>? Data { get; set; }

        [JsonPropertyName("usage")]
        public EmbeddingUsage? Usage { get; set; }
    }

    private sealed class EmbeddingItem
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    private sealed class EmbeddingUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; set; }
    }
}
