using TechieRag.Models;

namespace TechieRag.Agentic;

/// <summary>
/// An <see cref="IRetrievalSource"/> over two delegates, for a host that searches through its own
/// scope, for example a workspace-scoped search (REQ-RAG-015 / BRD-83).
/// </summary>
public sealed class DelegateRetrievalSource : IRetrievalSource
{
    private readonly Func<string, int, string?, CancellationToken, Task<IReadOnlyList<SearchResult>>> search;
    private readonly Func<CancellationToken, Task<IReadOnlyList<Document>>> listDocuments;

    /// <summary>Creates a retrieval source over delegates.</summary>
    /// <param name="search">Search: (query, topK, documentId, cancellationToken) → results, best first.</param>
    /// <param name="listDocuments">List documents; null lists none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="search"/> is null.</exception>
    public DelegateRetrievalSource(
        Func<string, int, string?, CancellationToken, Task<IReadOnlyList<SearchResult>>> search,
        Func<CancellationToken, Task<IReadOnlyList<Document>>>? listDocuments = null)
    {
        ArgumentNullException.ThrowIfNull(search);
        this.search = search;
        this.listDocuments = listDocuments ?? (_ => Task.FromResult<IReadOnlyList<Document>>([]));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int topK, string? documentId, CancellationToken cancellationToken) =>
        search(query, topK, documentId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken) =>
        listDocuments(cancellationToken);
}
