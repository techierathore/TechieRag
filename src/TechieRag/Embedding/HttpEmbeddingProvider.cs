using System.Diagnostics;
using System.IO;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechieRag.Abstractions;

namespace TechieRag.Embedding;

/// <summary>
/// Generic HTTP embedding provider that supports multiple API formats.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Connects to any HTTP-based embedding service including
/// ONNX containers, custom deployments, or any OpenAI/Ollama compatible API.</para>
/// <para><b>Supported Formats:</b></para>
/// <list type="bullet">
/// <item><description>OpenAI: POST /v1/embeddings - used by vLLM, text-embeddings-inference, etc.</description></item>
/// <item><description>Ollama: POST /api/embeddings - used by Ollama</description></item>
/// <item><description>Simple: POST /embed - simple JSON format for custom deployments</description></item>
/// </list>
/// <para><b>Use Cases:</b></para>
/// <list type="bullet">
/// <item><description>ONNX models deployed in Docker containers</description></item>
/// <item><description>TechieRag.Embedded exposed as a web service</description></item>
/// <item><description>Custom embedding microservices</description></item>
/// <item><description>Any OpenAI-compatible embedding API</description></item>
/// </list>
/// </remarks>
public class HttpEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    /// <inheritdoc/>
    public string Name => "HTTP";

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
    private readonly string apiPath;
    private readonly HttpApiFormat apiFormat;
    private readonly bool ownsHttpClient;
    private readonly int requestDelayMs;

    /// <inheritdoc/>
    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

    /// <summary>
    /// Creates a new HTTP embedding provider instance.
    /// </summary>
    /// <param name="endpoint">Base URL of the embedding service (e.g., http://localhost:7997).</param>
    /// <param name="apiFormat">API format to use (OpenAI, Ollama, or Simple).</param>
    /// <param name="model">Model name to send in requests (default: bge-m3).</param>
    /// <param name="dimensions">Vector dimensions (default: 1024 for BGE-M3).</param>
    /// <param name="apiPath">Custom API path (optional, uses format default if not specified).</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="timeoutSeconds">HTTP request timeout in seconds (default: 60 seconds).</param>
    /// <param name="requestDelayMs">Delay between requests in milliseconds (default: 200ms to prevent server overload).</param>
    public HttpEmbeddingProvider(
        string endpoint,
        HttpApiFormat apiFormat = HttpApiFormat.OpenAI,
        string model = "bge-m3",
        int dimensions = 1024,
        string? apiPath = null,
        string? apiKey = null,
        int timeoutSeconds = 60,
        int requestDelayMs = 200)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);

        this.endpoint = endpoint.TrimEnd('/');
        this.apiFormat = apiFormat;
        this.apiPath = apiPath ?? GetDefaultApiPath(apiFormat);
        ModelName = model;
        Dimensions = dimensions;
        this.requestDelayMs = requestDelayMs;

        // Create HttpClient with extended timeout for embedding operations
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 10,
            EnableMultipleHttp2Connections = true
        };

        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(this.endpoint),
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
    /// Creates a new HTTP embedding provider with a pre-configured HttpClient.
    /// </summary>
    /// <param name="httpClient">Pre-configured HttpClient instance.</param>
    /// <param name="apiFormat">API format to use.</param>
    /// <param name="model">Model name.</param>
    /// <param name="dimensions">Vector dimensions.</param>
    /// <param name="apiPath">Custom API path.</param>
    /// <param name="requestDelayMs">Delay between requests in milliseconds.</param>
    public HttpEmbeddingProvider(
        HttpClient httpClient,
        HttpApiFormat apiFormat = HttpApiFormat.OpenAI,
        string model = "bge-m3",
        int dimensions = 1024,
        string? apiPath = null,
        int requestDelayMs = 200)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        endpoint = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "";
        this.apiFormat = apiFormat;
        this.apiPath = apiPath ?? GetDefaultApiPath(apiFormat);
        ModelName = model;
        Dimensions = dimensions;
        this.requestDelayMs = requestDelayMs;
        ownsHttpClient = false;
    }

    /// <summary>
    /// Gets the default API path for each format.
    /// </summary>
    private static string GetDefaultApiPath(HttpApiFormat format) => format switch
    {
        HttpApiFormat.OpenAI => "/v1/embeddings",
        HttpApiFormat.Ollama => "/api/embeddings",
        HttpApiFormat.Simple => "/embed",
        _ => "/v1/embeddings"
    };

    /// <inheritdoc/>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));

        var stopwatch = Stopwatch.StartNew();

        float[] embedding = apiFormat switch
        {
            HttpApiFormat.OpenAI => await EmbedOpenAIFormatAsync(text, cancellationToken),
            HttpApiFormat.Ollama => await EmbedOllamaFormatAsync(text, cancellationToken),
            HttpApiFormat.Simple => await EmbedSimpleFormatAsync(text, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported API format: {apiFormat}")
        };

        stopwatch.Stop();
        RaiseEmbeddingCompleted(text, 1, stopwatch.Elapsed, null);

        return embedding;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts == null)
            throw new ArgumentNullException(nameof(texts));

        var textList = texts.ToList();
        if (textList.Count == 0)
            return Array.Empty<float[]>();

        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<float[]> embeddings = apiFormat switch
        {
            HttpApiFormat.OpenAI => await EmbedBatchOpenAIFormatAsync(textList, cancellationToken),
            HttpApiFormat.Ollama => await EmbedBatchOllamaFormatAsync(textList, cancellationToken),
            HttpApiFormat.Simple => await EmbedBatchSimpleFormatAsync(textList, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported API format: {apiFormat}")
        };

        stopwatch.Stop();
        var totalText = string.Join("", textList);
        RaiseEmbeddingCompleted(totalText, textList.Count, stopwatch.Elapsed, null);

        return embeddings;
    }

    #region OpenAI Format

    private async Task<float[]> EmbedOpenAIFormatAsync(string text, CancellationToken cancellationToken)
    {
        var request = new OpenAiRequest { Input = text, Model = ModelName };

        var response = await SendRequestAsync(request, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken: cancellationToken);

        if (result?.Data == null || result.Data.Count == 0 || result.Data[0].Embedding == null)
        {
            throw new InvalidOperationException("HTTP embedding service returned an invalid OpenAI-format response.");
        }

        return result.Data[0].Embedding!;
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchOpenAIFormatAsync(List<string> texts, CancellationToken cancellationToken)
    {
        // Process sequentially with delay between requests to prevent overwhelming the server
        // Batch requests often cause issues with ONNX/custom containers
        var results = new List<float[]>(texts.Count);
        for (int i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Add delay between requests to prevent overwhelming the server
            if (i > 0 && requestDelayMs > 0)
            {
                await Task.Delay(requestDelayMs, cancellationToken);
            }

            results.Add(await EmbedOpenAIFormatAsync(texts[i], cancellationToken));
        }
        return results;
    }

    #endregion

    #region Ollama Format

    private async Task<float[]> EmbedOllamaFormatAsync(string text, CancellationToken cancellationToken)
    {
        var request = new OllamaRequest { Prompt = text, Model = ModelName };

        var response = await SendRequestAsync(request, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: cancellationToken);

        if (result?.Embedding == null)
        {
            throw new InvalidOperationException("HTTP embedding service returned an invalid Ollama-format response.");
        }

        return result.Embedding;
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchOllamaFormatAsync(List<string> texts, CancellationToken cancellationToken)
    {
        // Ollama doesn't support batch, process sequentially with delay
        var results = new List<float[]>(texts.Count);

        for (int i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Add delay between requests to prevent overwhelming the server
            if (i > 0 && requestDelayMs > 0)
            {
                await Task.Delay(requestDelayMs, cancellationToken);
            }

            results.Add(await EmbedOllamaFormatAsync(texts[i], cancellationToken));
        }

        return results;
    }

    #endregion

    #region Simple Format

    private async Task<float[]> EmbedSimpleFormatAsync(string text, CancellationToken cancellationToken)
    {
        var request = new SimpleRequest { Text = text };

        var response = await SendRequestAsync(request, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<SimpleResponse>(cancellationToken: cancellationToken);

        if (result?.Embedding == null)
        {
            throw new InvalidOperationException("HTTP embedding service returned an invalid Simple-format response.");
        }

        return result.Embedding;
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchSimpleFormatAsync(List<string> texts, CancellationToken cancellationToken)
    {
        // Process sequentially with delay between requests to prevent overwhelming the server
        var results = new List<float[]>(texts.Count);
        for (int i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Add delay between requests to prevent overwhelming the server
            if (i > 0 && requestDelayMs > 0)
            {
                await Task.Delay(requestDelayMs, cancellationToken);
            }

            results.Add(await EmbedSimpleFormatAsync(texts[i], cancellationToken));
        }
        return results;
    }

    #endregion

    private async Task<HttpResponseMessage> SendRequestAsync(object request, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 500;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await httpClient.PostAsJsonAsync(apiPath, request, cancellationToken);
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // User requested cancellation - don't retry
                throw;
            }
            catch (OperationCanceledException ex) when (attempt < maxRetries)
            {
                // This is a timeout (TaskCanceledException), not user cancellation - retry
                lastException = ex;
                var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                lastException = ex;
                // Exponential backoff: 500ms, 1000ms, 2000ms
                var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxRetries && IsTransientError(ex))
            {
                lastException = ex;
                var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(delay, cancellationToken);
            }
        }

        // Final attempt - let the exception propagate
        try
        {
            var response = await httpClient.PostAsJsonAsync(apiPath, request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User cancellation - just rethrow
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Timeout - wrap in a more descriptive exception
            throw new HttpRequestException(
                $"HTTP embedding service at {endpoint}{apiPath} timed out after {maxRetries} retries. " +
                $"Consider increasing timeout or checking server load.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException(
                $"Failed to connect to HTTP embedding service at {endpoint}{apiPath} after {maxRetries} retries. " +
                $"Ensure the service is running and accessible. Error: {ex.Message}", ex);
        }
    }

    private static bool IsTransientError(Exception ex)
    {
        // Check for common transient network errors
        return ex is IOException ||
               ex is SocketException ||
               (ex.InnerException != null && IsTransientError(ex.InnerException));
    }

    private void RaiseEmbeddingCompleted(string text, int textCount, TimeSpan duration, int? actualTokenCount)
    {
        OnEmbeddingCompleted?.Invoke(this, new EmbeddingCompletedEventArgs
        {
            TokenCount = actualTokenCount ?? EstimateTokenCount(text),
            TextCount = textCount,
            Duration = duration,
            ModelName = ModelName,
            ProviderName = $"{Name} ({apiFormat})"
        });
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    #region Request/Response Models

    // OpenAI format
    private sealed class OpenAiRequest
    {
        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;
    }

    private sealed class OpenAiBatchRequest
    {
        [JsonPropertyName("input")]
        public List<string> Input { get; set; } = new();

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;
    }

    private sealed class OpenAiResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAiEmbeddingData> Data { get; set; } = new();

        [JsonPropertyName("usage")]
        public OpenAiUsage? Usage { get; set; }
    }

    private sealed class OpenAiEmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    private sealed class OpenAiUsage
    {
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    // Ollama format
    private sealed class OllamaRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;
    }

    private sealed class OllamaResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    // Simple format
    private sealed class SimpleRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class SimpleBatchRequest
    {
        [JsonPropertyName("texts")]
        public List<string> Texts { get; set; } = new();
    }

    private sealed class SimpleResponse
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    private sealed class SimpleBatchResponse
    {
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }

    #endregion
}
