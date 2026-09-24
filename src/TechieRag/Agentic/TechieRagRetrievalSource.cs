using TechieRag.Models;

namespace TechieRag.Agentic;

/// <summary>
/// An <see cref="IRetrievalSource"/> over an <see cref="ITechieRag"/> instance (REQ-RAG-015 / BRD-83).
/// </summary>
/// <remarks>
/// Searches through <see cref="ITechieRag.SearchAsync(string, SearchOptions?, CancellationToken)"/> with
/// the rerank switch stated explicitly, so the status classification in <see cref="KnowledgeBaseTools"/>
/// knows which score scale it is reading. A decorator that implements <see cref="ITechieRag"/> without
/// overriding that overload drops the switch (the interface default ignores it) and gets cosine scores.
/// </remarks>
public sealed class TechieRagRetrievalSource : IRetrievalSource
{
    private readonly ITechieRag rag;
    private readonly bool rerank;

    /// <summary>Creates a retrieval source over a TechieRag instance.</summary>
    /// <param name="rag">The instance to search.</param>
    /// <param name="rerank">Whether each search is reranked; match <see cref="RetrievalToolOptions.Rerank"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rag"/> is null.</exception>
    public TechieRagRetrievalSource(ITechieRag rag, bool rerank = false)
    {
        ArgumentNullException.ThrowIfNull(rag);
        this.rag = rag;
        this.rerank = rerank;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK, string? documentId, CancellationToken cancellationToken) =>
        rag.SearchAsync(query, new SearchOptions { TopK = topK, DocumentFilter = documentId, Rerank = rerank }, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken) =>
        rag.ListDocumentsAsync(cancellationToken);
}
