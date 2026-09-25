using TechieRag.Models;

namespace TechieRag.Agentic;

/// <summary>
/// What the knowledge-base tools search (REQ-RAG-015 / BRD-83).
/// </summary>
/// <remarks>
/// A seam rather than a direct <see cref="ITechieRag"/> dependency, because a host usually searches
/// through its own scope: a workspace's pinned documents, a per-turn document selection, a workspace
/// similarity threshold. <see cref="TechieRagRetrievalSource"/> covers the plain case;
/// <see cref="DelegateRetrievalSource"/> covers everything else without a new type.
/// </remarks>
public interface IRetrievalSource
{
    /// <summary>Searches for passages relevant to a query.</summary>
    /// <param name="query">The model's query.</param>
    /// <param name="topK">How many passages to return.</param>
    /// <param name="documentId">Restrict to one document, or null for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The results, best first; scores on the store's scale (cosine unless reranked).</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK, string? documentId, CancellationToken cancellationToken);

    /// <summary>Lists the documents the source can search.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The documents.</returns>
    Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken);
}
