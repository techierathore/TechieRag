using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechieRag.Abstractions;

namespace TechieRag.Embedding;

/// <summary>
/// Embedding provider implementation for Ollama local model server.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Generates text embeddings using models running on a local Ollama server,
/// enabling offline operation and privacy-sensitive scenarios.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when EmbeddingSource.Ollama is configured.
/// Called by TechieRagClient during ingestion (to embed chunks) and search (to embed queries).</para>
/// <para><b>API:</b> POST to /api/embeddings with { "model": "...", "prompt": "..." }
/// Returns { "embedding": [...] }</para>
/// <para><b>Dependencies:</b> Requires Ollama to be running locally with an embedding model pulled.</para>
/// </remarks>
public class OllamaEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    /// <inheritdoc/>
    public string Name => "Ollama";

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
    /// Creates a new Ollama embedding provider instance with default settings.
    /// </summary>
    /// <remarks>
    /// <para>Uses default endpoint http://localhost:11434 and model bge-m3.</para>
    /// </remarks>
    public OllamaEmbeddingProvider()
        : this("http://localhost:11434", "bge-m3", 1024)
    {
    }

    /// <summary>
    /// Creates a new Ollama embedding provider instance.
    /// </summary>
    /// <param name="endpoint">Ollama API endpoint (e.g., http://localhost:11434).</param>
    /// <param name="model">Model name to use for embeddings (default: bge-m3).</param>
    /// <param name="dimensions">Vector dimensions (default: 1024 for BGE-M3).</param>
    public OllamaEmbeddingProvider(string endpoint, string model = "bge-m3", int dimensions = 1024)
    {
        this.endpoint = endpoint.TrimEnd('/');
        ModelName = model;
        Dimensions = dimensions;
        httpClient = new HttpClient { BaseAddress = new Uri(this.endpoint) };
        ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a new Ollama embedding provider instance with a custom HttpClient.
    /// </summary>
    /// <param name="httpClient">Pre-configured HttpClient instance.</param>
    /// <param name="model">Model name to use for embeddings (default: bge-m3).</param>
    /// <param name="dimensions">Vector dimensions (default: 1024 for BGE-M3).</param>
    /// <remarks>
    /// <para><b>Note:</b> The provided HttpClient will not be disposed when this provider is disposed.</para>
    /// </remarks>
    public OllamaEmbeddingProvider(HttpClient httpClient, string model = "bge-m3", int dimensions = 1024)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        endpoint = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "http://localhost:11434";
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
    /// <exception cref="HttpRequestException">Thrown when the Ollama API request fails.</exception>
    /// <remarks>
    /// <para><b>Flow:</b> Sends POST request to Ollama's /api/embeddings endpoint,
    /// receives vector response, and raises telemetry event.</para>
    /// </remarks>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));

        var stopwatch = Stopwatch.StartNew();

        var request = new OllamaEmbeddingRequest
        {
            Model = ModelName,
            Prompt = text
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync("/api/embeddings", request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"Failed to connect to Ollama at {endpoint}. Ensure Ollama is running.", ex);
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: cancellationToken);

        if (result?.Embedding == null)
        {
            throw new InvalidOperationException("Ollama returned an invalid embedding response.");
        }

        stopwatch.Stop();

        RaiseEmbeddingCompleted(text, 1, stopwatch.Elapsed);

        return result.Embedding;
    }

    /// <summary>
    /// Generates embedding vectors for multiple texts sequentially.
    /// </summary>
    /// <param name="texts">Collection of texts to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of embedding vectors corresponding to each input text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when texts is null.</exception>
    /// <remarks>
    /// <para><b>Note:</b> Ollama processes embeddings one at a time. For better performance
    /// with many texts, consider using a batch-capable provider.</para>
    /// </remarks>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts == null)
            throw new ArgumentNullException(nameof(texts));

        var textList = texts.ToList();
        if (textList.Count == 0)
            return Array.Empty<float[]>();

        var stopwatch = Stopwatch.StartNew();
        var results = new List<float[]>(textList.Count);
        var totalText = string.Empty;

        foreach (var text in textList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new OllamaEmbeddingRequest
            {
                Model = ModelName,
                Prompt = text
            };

            var response = await httpClient.PostAsJsonAsync("/api/embeddings", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: cancellationToken);

            if (result?.Embedding == null)
            {
                throw new InvalidOperationException("Ollama returned an invalid embedding response.");
            }

            results.Add(result.Embedding);
            totalText += text;
        }

        stopwatch.Stop();

        RaiseEmbeddingCompleted(totalText, textList.Count, stopwatch.Elapsed);

        return results;
    }

    /// <summary>
    /// Raises the OnEmbeddingCompleted event with telemetry data.
    /// </summary>
    /// <param name="text">The text(s) that were embedded.</param>
    /// <param name="textCount">Number of texts embedded.</param>
    /// <param name="duration">Duration of the embedding operation.</param>
    private void RaiseEmbeddingCompleted(string text, int textCount, TimeSpan duration)
    {
        OnEmbeddingCompleted?.Invoke(this, new EmbeddingCompletedEventArgs
        {
            TokenCount = EstimateTokenCount(text),
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

        // Rough estimation: ~4 characters per token for English text
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
    /// Request model for Ollama embedding API.
    /// </summary>
    private sealed class OllamaEmbeddingRequest
    {
        /// <summary>
        /// Gets or sets the model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the text prompt to embed.
        /// </summary>
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response model for Ollama embedding API.
    /// </summary>
    private sealed class OllamaEmbeddingResponse
    {
        /// <summary>
        /// Gets or sets the embedding vector.
        /// </summary>
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
