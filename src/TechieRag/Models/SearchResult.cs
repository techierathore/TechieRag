namespace TechieRag.Models;

/// <summary>
/// Represents a single search result from a semantic search operation.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pairs a matched text chunk with its relevance score,
/// enabling ranked presentation of search results to users.</para>
/// <para><b>Code Flow:</b> Created by IVectorStore.SearchAsync during vector similarity search.
/// Returned to callers via ITechieRag.SearchAsync for display or further processing.</para>
/// <para><b>Design:</b> Uses init-only required properties to ensure all search results
/// are fully populated and immutable after creation.</para>
/// </remarks>
public class SearchResult
{
    /// <summary>
    /// Gets the matched text chunk.
    /// </summary>
    /// <remarks>
    /// This is a required property that must be set during initialization.
    /// Contains the full TextChunk data including text content, metadata,
    /// and document reference information.
    /// </remarks>
    public required TextChunk Chunk { get; init; }

    /// <summary>
    /// Gets the similarity score for this result.
    /// </summary>
    /// <remarks>
    /// This is a required property that must be set during initialization.
    /// Score typically ranges from 0 to 1, where higher values indicate
    /// greater semantic similarity to the search query.
    /// The exact score interpretation depends on the vector store implementation
    /// and the distance metric used (cosine similarity, Euclidean distance, etc.).
    /// </remarks>
    public required float Score { get; init; }
}
