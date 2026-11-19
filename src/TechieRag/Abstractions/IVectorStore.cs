using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for vector database storage operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a unified interface for storing and retrieving vector embeddings
/// across different vector database implementations (SQLite-vec, PGVector, Qdrant).</para>
/// <para><b>Code Flow:</b> Instantiated by TechieRagBuilder based on configuration. Called by
/// TechieRagClient during ingestion (UpsertAsync) and search (SearchAsync) operations.</para>
/// <para><b>Implementations:</b> SqliteVecStore, PgVectorStore, QdrantStore</para>
/// </remarks>
public interface IVectorStore
{
    /// <summary>
    /// Gets the display name of this vector store implementation.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Initializes the vector store, creating tables/collections if needed.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates a single text chunk with its vector embedding.
    /// </summary>
    /// <param name="chunk">The chunk containing text, vector, and metadata.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The ID of the upserted chunk.</returns>
    Task<string> UpsertAsync(TextChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates multiple chunks in a batch operation for efficiency.
    /// </summary>
    /// <param name="chunks">Collection of chunks to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of IDs for all upserted chunks.</returns>
    Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs vector similarity search to find chunks most similar to the query vector.
    /// </summary>
    /// <param name="queryVector">The embedding vector of the search query.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="documentFilter">Optional document ID to restrict search scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked search results ordered by similarity score.</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryVector, int topK = 5, string? documentFilter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs one vector similarity search restricted to a set of documents (REQ-RAG-114 / BRD-174).
    /// </summary>
    /// <param name="queryVector">The embedding vector of the search query.</param>
    /// <param name="topK">Maximum number of results to return across all the documents.</param>
    /// <param name="documentIds">The documents to search; an empty set returns no results.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked search results from any of the documents, highest score first.</returns>
    /// <remarks>
    /// <para><b>Default implementation</b> (ADR-005 additive change): one <see cref="SearchAsync"/>
    /// per document, merged by score and cut to <paramref name="topK"/>. The built-in stores override
    /// it with a single filtered query; a third-party store should too.</para>
    /// </remarks>
    async Task<IReadOnlyList<SearchResult>> SearchDocumentsAsync(
        float[] queryVector,
        int topK,
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (documentIds.Count == 0 || topK <= 0)
        {
            return [];
        }

        var merged = new List<SearchResult>();
        foreach (var documentId in documentIds.Distinct(StringComparer.Ordinal))
        {
            merged.AddRange(await SearchAsync(queryVector, topK, documentId, cancellationToken).ConfigureAwait(false));
        }

        return merged.OrderByDescending(result => result.Score).Take(topK).ToList();
    }

    /// <summary>
    /// Performs one vector similarity search over the chunks whose metadata matches a filter, optionally within a set
    /// of documents (REQ-RAG-169, TrStudio TR-RAG-003).
    /// </summary>
    /// <param name="queryVector">The embedding vector of the search query.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="documentIds">The documents to search, or null for all; an empty set returns no results.</param>
    /// <param name="metadataFilter">Key and text pairs every result's metadata must match; see <see cref="SearchOptions.MetadataFilter"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The best matching chunks, highest score first.</returns>
    /// <exception cref="ArgumentException">A filter key is empty or holds a double quote or backslash, or a value is null.</exception>
    /// <remarks>
    /// <para><b>Default implementation</b> (ADR-005 additive change): asks <see cref="SearchAsync"/> or
    /// <see cref="SearchDocumentsAsync"/> for four times <paramref name="topK"/> candidates (at least 32), keeps the
    /// matching ones, and asks for four times as many again until it has <paramref name="topK"/> matches or the
    /// store has no more chunks. The result is the same as a native filter, at the cost of the extra reads.
    /// SqliteVec and PgVector override it with a filter inside their own query; a third-party store should too.</para>
    /// </remarks>
    async Task<IReadOnlyList<SearchResult>> SearchMatchingAsync(
        float[] queryVector,
        int topK,
        IReadOnlyCollection<string>? documentIds,
        IReadOnlyDictionary<string, string> metadataFilter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadataFilter);
        VectorStores.MetadataMatch.Validate(metadataFilter);
        if (topK <= 0 || documentIds is { Count: 0 })
        {
            return [];
        }

        var ask = Math.Max(topK * 4, 32);
        while (true)
        {
            var candidates = documentIds is null
                ? await SearchAsync(queryVector, ask, null, cancellationToken).ConfigureAwait(false)
                : await SearchDocumentsAsync(queryVector, ask, documentIds, cancellationToken).ConfigureAwait(false);
            var matched = candidates.Where(result => VectorStores.MetadataMatch.Matches(result.Chunk, metadataFilter)).Take(topK).ToList();
            if (matched.Count >= topK || candidates.Count < ask || ask > int.MaxValue / 4)
            {
                return matched;
            }

            ask *= 4;
        }
    }

    /// <summary>
    /// Deletes a specific chunk by its ID.
    /// </summary>
    /// <param name="chunkId">The ID of the chunk to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all chunks belonging to a specific document.
    /// </summary>
    /// <param name="documentId">The document ID whose chunks should be deleted.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all documents in the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of documents with their metadata.</returns>
    Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves statistics about the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Statistics including counts and storage size.</returns>
    Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all data from the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous clear operation.</returns>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
