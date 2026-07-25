using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Reranking;

/// <summary>
/// API reranker implementation using the Cohere Rerank v2 endpoint.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> High-quality second-stage reranking via Cohere's hosted
/// cross-encoder models (e.g. "rerank-v3.5").</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when RerankSource.Cohere is configured.
/// Sends the query plus candidate chunk texts to <c>/v2/rerank</c> and reorders the
/// results by the returned relevance scores.</para>
/// <para><b>Dependencies:</b> Raw HttpClient + System.Text.Json only.</para>
/// </remarks>
public class CohereReranker : IReranker
{
    private readonly HttpClient httpClient;
    private readonly string model;
    private readonly ILogger<CohereReranker> logger;

    /// <inheritdoc/>
    public string Name => "Cohere";

    /// <summary>
    /// Creates a new Cohere reranker instance.
    /// </summary>
    /// <param name="apiKey">Cohere API key.</param>
    /// <param name="model">Rerank model name (default "rerank-v3.5").</param>
    /// <param name="endpoint">API endpoint (defaults to https://api.cohere.com).</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentException">Thrown when apiKey is null or empty.</exception>
    public CohereReranker(string apiKey, string model = "rerank-v3.5", string? endpoint = null, ILogger<CohereReranker>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        this.model = model;
        this.logger = logger ?? NullLogger<CohereReranker>.Instance;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri((endpoint ?? "https://api.cohere.com").TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(60)
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Creates a Cohere reranker with a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>Test seam: allows a stubbed <see cref="HttpMessageHandler"/> to intercept requests.</remarks>
    /// <param name="httpClient">Pre-configured HTTP client (BaseAddress must be set).</param>
    /// <param name="model">Rerank model name.</param>
    /// <param name="logger">Logger instance.</param>
    internal CohereReranker(HttpClient httpClient, string model = "rerank-v3.5", ILogger<CohereReranker>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        this.model = model;
        this.logger = logger ?? NullLogger<CohereReranker>.Instance;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0) return results;

        var request = new
        {
            model,
            query,
            documents = results.Select(r => r.Chunk.Text).ToList(),
            top_n = Math.Min(topN, results.Count)
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("/v2/rerank", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<CohereRerankResponse>(responseJson)
            ?? throw new InvalidOperationException("Failed to parse Cohere rerank response");

        var reranked = new List<SearchResult>();
        foreach (var item in parsed.Results ?? [])
        {
            if (item.Index < 0 || item.Index >= results.Count) continue;
            reranked.Add(new SearchResult
            {
                Chunk = results[item.Index].Chunk,
                Score = (float)item.RelevanceScore
            });
        }

        logger.LogDebug("Cohere reranked {InputCount} candidates to {OutputCount} results", results.Count, reranked.Count);
        return reranked;
    }

    private class CohereRerankResponse
    {
        [JsonPropertyName("results")]
        public List<CohereRerankResult>? Results { get; set; }
    }

    private class CohereRerankResult
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("relevance_score")]
        public double RelevanceScore { get; set; }
    }
}
