using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TechieRag.Abstractions;

namespace TechieRag.Embedding;

/// <summary>
/// Embedding provider implementation for LM Studio local model server.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Generates text embeddings using models running on a local LM Studio server,
/// enabling offline operation with OpenAI-compatible API.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when EmbeddingSource.LmStudio is configured.
/// Called by TechieRagClient during ingestion (to embed chunks) and search (to embed queries).</para>
/// <para><b>API:</b> Uses OpenAI-compatible API. POST to /v1/embeddings with
/// { "input": "...", "model": "..." }
/// Returns { "data": [{ "embedding": [...] }] }</para>
/// <para><b>Dependencies:</b> Requires LM Studio to be running locally with an embedding model loaded.</para>
/// </remarks>
public class LmStudioEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    /// <inheritdoc/>
    public string Name => "LM Studio";

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

    private readonly HttpClient httpClient;
    private readonly string endpoint;
    private readonly bool ownsHttpClient;

    /// <inheritdoc/>
    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

    /// <summary>
    /// Creates a new LM Studio embedding provider instance with default settings.
    /// </summary>
    /// <remarks>
    /// <para>Uses default endpoint http://localhost:1234.</para>
    /// </remarks>
    public LmStudioEmbeddingProvider()
        : this("http://localhost:1234", "default", 1024)
    {
    }

    /// <summary>
    /// Creates a new LM Studio embedding provider instance.
    /// </summary>
    /// <param name="endpoint">LM Studio API endpoint (e.g., http://localhost:1234).</param>
    /// <param name="model">Model name to use for embeddings (default: "default", uses currently loaded model).</param>
    /// <param name="dimensions">Vector dimensions (default: 1024).</param>
    public LmStudioEmbeddingProvider(string endpoint, string model = "default", int dimensions = 1024)
    {
        this.endpoint = endpoint.TrimEnd('/');
        ModelName = model;
        Dimensions = dimensions;
        httpClient = new HttpClient { BaseAddress = new Uri(this.endpoint) };
        ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a new LM Studio embedding provider instance with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">Pre-configured HttpClient instance.</param>
    /// <param name="model">Model name to use for embeddings (default: "default").</param>
    /// <param name="dimensions">Vector dimensions (default: 1024).</param>
    /// <remarks>
    /// <para><b>Note:</b> The provided HttpClient will not be disposed when this provider is disposed.</para>
    /// </remarks>
    public LmStudioEmbeddingProvider(HttpClient httpClient, string model = "default", int dimensions = 1024)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        endpoint = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "http://localhost:1234";
        ModelName = model;
        Dimensions = dimensions;
        ownsHttpClient = false;
    }

    /// <summary>
    /// Generates an embedding vector for a single text input.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A float array representing the embedding vector.</returns>
    /// <exception cref="ArgumentException">Thrown when text is null or empty.</exception>
    /// <exception cref="HttpRequestException">Thrown when the LM Studio API request fails.</exception>
    /// <remarks>
    /// <para><b>Flow:</b> Sends POST request to LM Studio's /v1/embeddings endpoint,
    /// receives vector response, and raises telemetry event.</para>
    /// </remarks>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));

        var stopwatch = Stopwatch.StartNew();

        var request = new OpenAiEmbeddingRequest
        {
            Input = text,
            Model = ModelName
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync("/v1/embeddings", request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"Failed to connect to LM Studio at {endpoint}. Ensure LM Studio is running with a loaded embedding model.", ex);
        }

        var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken: cancellationToken);

        if (result?.Data == null || result.Data.Count == 0 || result.Data[0].Embedding == null)
        {
            throw new InvalidOperationException("LM Studio returned an invalid embedding response.");
        }

        stopwatch.Stop();

        RaiseEmbeddingCompleted(text, 1, stopwatch.Elapsed, result.Usage?.TotalTokens);

        return result.Data[0].Embedding!;
    }

    /// <summary>
    /// Generates embedding vectors for multiple texts in a batch operation.
    /// </summary>
    /// <param name="texts">Collection of texts to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of embedding vectors corresponding to each input text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when texts is null.</exception>
    /// <remarks>
    /// <para><b>Note:</b> LM Studio's OpenAI-compatible API supports batch embedding.
    /// This method sends all texts in a single request for efficiency.</para>
    /// </remarks>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts == null)
            throw new ArgumentNullException(nameof(texts));

        var textList = texts.ToList();
        if (textList.Count == 0)
            return Array.Empty<float[]>();

        var stopwatch = Stopwatch.StartNew();

        var request = new OpenAiEmbeddingBatchRequest
        {
            Input = textList,
            Model = ModelName
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync("/v1/embeddings", request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"Failed to connect to LM Studio at {endpoint}. Ensure LM Studio is running with a loaded embedding model.", ex);
        }

        var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken: cancellationToken);

        if (result?.Data == null || result.Data.Count != textList.Count)
        {
            throw new InvalidOperationException("LM Studio returned an invalid batch embedding response.");
        }

        // Ensure results are in the correct order (by index)
        var orderedResults = result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding ?? throw new InvalidOperationException("Embedding was null"))
            .ToList();

        stopwatch.Stop();

        var totalText = string.Join("", textList);
        RaiseEmbeddingCompleted(totalText, textList.Count, stopwatch.Elapsed, result.Usage?.TotalTokens);

        return orderedResults;
    }

    /// <summary>
    /// Raises the OnEmbeddingCompleted event with telemetry data.
    /// </summary>
    /// <param name="text">The text(s) that were embedded.</param>
    /// <param name="textCount">Number of texts embedded.</param>
    /// <param name="duration">Duration of the embedding operation.</param>
    /// <param name="actualTokenCount">Actual token count from the API response, if available.</param>
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

    /// <summary>
    /// Estimates the token count for a given text.
    /// </summary>
    /// <param name="text">The text to estimate tokens for.</param>
    /// <returns>Approximate token count.</returns>
    /// <remarks>
    /// <para>Uses a simple heuristic of ~4 characters per token, which is a reasonable
    /// approximation for English text.</para>
    /// </remarks>
    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Disposes the HttpClient if it was created by this instance.
    /// </summary>
    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Request model for OpenAI-compatible embedding API (single input).
    /// </summary>
    private sealed class OpenAiEmbeddingRequest
    {
        /// <summary>
        /// Gets or sets the text to embed.
        /// </summary>
        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request model for OpenAI-compatible embedding API (batch input).
    /// </summary>
    private sealed class OpenAiEmbeddingBatchRequest
    {
        /// <summary>
        /// Gets or sets the list of texts to embed.
        /// </summary>
        [JsonPropertyName("input")]
        public List<string> Input { get; set; } = new();

        /// <summary>
        /// Gets or sets the model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response model for OpenAI-compatible embedding API.
    /// </summary>
    private sealed class OpenAiEmbeddingResponse
    {
        /// <summary>
        /// Gets or sets the list of embedding data items.
        /// </summary>
        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; set; } = new();

        /// <summary>
        /// Gets or sets the usage information.
        /// </summary>
        [JsonPropertyName("usage")]
        public UsageInfo? Usage { get; set; }
    }

    /// <summary>
    /// Individual embedding data in the response.
    /// </summary>
    private sealed class EmbeddingData
    {
        /// <summary>
        /// Gets or sets the index of this embedding in the batch.
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the embedding vector.
        /// </summary>
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    /// <summary>
    /// Token usage information from the API response.
    /// </summary>
    private sealed class UsageInfo
    {
        /// <summary>
        /// Gets or sets the number of prompt tokens.
        /// </summary>
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        /// <summary>
        /// Gets or sets the total number of tokens.
        /// </summary>
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
