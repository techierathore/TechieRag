using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Reranking;

/// <summary>
/// API reranker implementation using the Jina AI Rerank endpoint.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Second-stage reranking via Jina's hosted reranker models
/// (e.g. "jina-reranker-v2-base-multilingual").</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when RerankSource.Jina is configured.
/// Sends the query plus candidate chunk texts to <c>/v1/rerank</c> and reorders the
/// results by the returned relevance scores.</para>
/// <para><b>Dependencies:</b> Raw HttpClient + System.Text.Json only.</para>
/// </remarks>
public class JinaReranker : IReranker
{
    private readonly HttpClient httpClient;
    private readonly string model;
    private readonly ILogger<JinaReranker> logger;

    /// <inheritdoc/>
    public string Name => "Jina";

    /// <summary>
    /// Creates a new Jina reranker instance.
    /// </summary>
    /// <param name="apiKey">Jina AI API key.</param>
    /// <param name="model">Rerank model name (default "jina-reranker-v2-base-multilingual").</param>
    /// <param name="endpoint">API endpoint (defaults to https://api.jina.ai).</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentException">Thrown when apiKey is null or empty.</exception>
    public JinaReranker(string apiKey, string model = "jina-reranker-v2-base-multilingual", string? endpoint = null, ILogger<JinaReranker>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        this.model = model;
        this.logger = logger ?? NullLogger<JinaReranker>.Instance;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri((endpoint ?? "https://api.jina.ai").TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(60)
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Creates a Jina reranker with a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>Test seam: allows a stubbed <see cref="HttpMessageHandler"/> to intercept requests.</remarks>
    /// <param name="httpClient">Pre-configured HTTP client (BaseAddress must be set).</param>
    /// <param name="model">Rerank model name.</param>
    /// <param name="logger">Logger instance.</param>
    internal JinaReranker(HttpClient httpClient, string model = "jina-reranker-v2-base-multilingual", ILogger<JinaReranker>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        this.model = model;
        this.logger = logger ?? NullLogger<JinaReranker>.Instance;
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

        var response = await httpClient.PostAsync("/v1/rerank", content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<JinaRerankResponse>(responseJson)
            ?? throw new InvalidOperationException("Failed to parse Jina rerank response");

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

        logger.LogDebug("Jina reranked {InputCount} candidates to {OutputCount} results", results.Count, reranked.Count);
        return reranked;
    }

    private class JinaRerankResponse
    {
        [JsonPropertyName("results")]
        public List<JinaRerankResult>? Results { get; set; }
    }

    private class JinaRerankResult
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("relevance_score")]
        public double RelevanceScore { get; set; }
    }
}
