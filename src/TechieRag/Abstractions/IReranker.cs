using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for a second-stage reranking service that reorders vector search results
/// by cross-encoder or API-based relevance scoring.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Vector similarity search is fast but approximate; a reranker scores
/// each candidate chunk directly against the query for higher-precision retrieval.</para>
/// <para><b>Code Flow:</b> Configured via TechieRagBuilder.WithReranker or the
/// <c>TechieRag:Rerank</c> configuration section. TechieRagClient invokes the reranker after
/// vector search when reranking is enabled.</para>
/// <para><b>Implementations:</b> CohereReranker, JinaReranker (API, core package) and
/// OnnxCrossEncoderReranker (local, TechieRag.Embedded package).</para>
/// </remarks>
public interface IReranker
{
    /// <summary>
    /// Gets the display name of this reranker implementation.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Reorders the given search results by relevance to the query and returns the top results.
    /// </summary>
    /// <param name="query">The natural language query the results were retrieved for.</param>
    /// <param name="results">The candidate search results from the vector store.</param>
    /// <param name="topN">Maximum number of reranked results to return.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Reranked results ordered by descending relevance, with scores replaced
    /// by the reranker's relevance scores.</returns>
    /// <exception cref="HttpRequestException">Thrown by API rerankers when the service call fails.</exception>
    Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        int topN,
        CancellationToken cancellationToken = default);
}
