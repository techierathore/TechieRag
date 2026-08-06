using System.ClientModel;
using System.Diagnostics;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;
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
/// <para><b>API:</b> Uses Azure.AI.OpenAI SDK with EmbeddingsClient for embedding operations.</para>
/// <para><b>Dependencies:</b> Requires Azure OpenAI resource with deployed embedding model.</para>
/// </remarks>
public class AzureOpenAIEmbeddingProvider : IEmbeddingProvider, IDisposable
{
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

    private readonly EmbeddingClient embeddingClient;
    private readonly string endpoint;
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
    /// <exception cref="ArgumentException">Thrown when endpoint, apiKey, or deploymentName is null or empty.</exception>
    /// <remarks>
    /// <para><b>Common Models:</b></para>
    /// <list type="bullet">
    /// <item>text-embedding-ada-002: 1536 dimensions</item>
    /// <item>text-embedding-3-small: 1536 dimensions (default)</item>
    /// <item>text-embedding-3-large: 3072 dimensions</item>
    /// </list>
    /// </remarks>
    public AzureOpenAIEmbeddingProvider(string endpoint, string apiKey, string deploymentName, int dimensions = 1536)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint cannot be null or empty.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(deploymentName))
            throw new ArgumentException("Deployment name cannot be null or empty.", nameof(deploymentName));

        this.endpoint = endpoint.TrimEnd('/');
        ModelName = deploymentName;
        Dimensions = dimensions;

        var azureClient = new AzureOpenAIClient(new Uri(this.endpoint), new ApiKeyCredential(apiKey));
        embeddingClient = azureClient.GetEmbeddingClient(deploymentName);
    }

    /// <summary>
    /// Creates a new Azure OpenAI embedding provider instance with a pre-configured EmbeddingClient.
    /// </summary>
    /// <param name="embeddingClient">Pre-configured EmbeddingClient instance.</param>
    /// <param name="modelName">Model/deployment name for telemetry purposes.</param>
    /// <param name="dimensions">Vector dimensions.</param>
    /// <exception cref="ArgumentNullException">Thrown when embeddingClient is null.</exception>
    /// <remarks>
    /// <para><b>Use Case:</b> Allows reuse of existing Azure OpenAI client instances,
    /// useful when multiple providers share the same connection.</para>
    /// </remarks>
    public AzureOpenAIEmbeddingProvider(EmbeddingClient embeddingClient, string modelName, int dimensions = 1536)
    {
        this.embeddingClient = embeddingClient ?? throw new ArgumentNullException(nameof(embeddingClient));
        endpoint = "pre-configured";
        ModelName = modelName;
        Dimensions = dimensions;
    }

    /// <summary>
    /// Generates an embedding vector for a single text input.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A float array representing the embedding vector.</returns>
    /// <exception cref="ArgumentException">Thrown when text is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the Azure OpenAI API returns an invalid response.</exception>
    /// <remarks>
    /// <para><b>Flow:</b> Sends request to Azure OpenAI embedding endpoint via SDK,
    /// receives vector response, and raises telemetry event.</para>
    /// </remarks>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or empty.", nameof(text));

        ObjectDisposedException.ThrowIf(disposed, this);

        var stopwatch = Stopwatch.StartNew();

        ClientResult<OpenAIEmbedding> result;
        try
        {
            result = await embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        }
        catch (ClientResultException ex)
        {
            throw new HttpRequestException($"Azure OpenAI embedding request failed: {ex.Message}", ex);
        }

        var embedding = result.Value;
        if (embedding == null)
        {
            throw new InvalidOperationException("Azure OpenAI returned an invalid embedding response.");
        }

        var vector = embedding.ToFloats().ToArray();

        stopwatch.Stop();

        RaiseEmbeddingCompleted(text, 1, stopwatch.Elapsed, null);

        return vector;
    }

    /// <summary>
    /// Generates embedding vectors for multiple texts in a batch operation.
    /// </summary>
    /// <param name="texts">Collection of texts to embed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of embedding vectors corresponding to each input text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when texts is null.</exception>
    /// <remarks>
    /// <para><b>Performance:</b> Azure OpenAI supports batch embedding operations,
    /// which are more efficient than individual calls for multiple texts.</para>
    /// </remarks>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts == null)
            throw new ArgumentNullException(nameof(texts));

        ObjectDisposedException.ThrowIf(disposed, this);

        var textList = texts.ToList();
        if (textList.Count == 0)
            return Array.Empty<float[]>();

        var stopwatch = Stopwatch.StartNew();

        ClientResult<OpenAIEmbeddingCollection> result;
        try
        {
            result = await embeddingClient.GenerateEmbeddingsAsync(textList, cancellationToken: cancellationToken);
        }
        catch (ClientResultException ex)
        {
            throw new HttpRequestException($"Azure OpenAI batch embedding request failed: {ex.Message}", ex);
        }

        var embeddings = result.Value;
        if (embeddings == null || embeddings.Count != textList.Count)
        {
            throw new InvalidOperationException("Azure OpenAI returned an invalid batch embedding response.");
        }

        // Ensure results are in the correct order (by index)
        var orderedResults = embeddings
            .OrderBy(e => e.Index)
            .Select(e => e.ToFloats().ToArray())
            .ToList();

        stopwatch.Stop();

        var totalText = string.Join("", textList);
        RaiseEmbeddingCompleted(totalText, textList.Count, stopwatch.Elapsed, null);

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
    /// approximation for English text. For more accurate counts, consider using a tokenizer.</para>
    /// </remarks>
    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Disposes resources used by this provider.
    /// </summary>
    /// <remarks>
    /// <para><b>Note:</b> The EmbeddingClient is managed by the Azure SDK and does not
    /// require explicit disposal, but this method is provided for interface compliance.</para>
    /// </remarks>
    public void Dispose()
    {
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
