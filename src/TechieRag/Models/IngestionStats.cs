namespace TechieRag.Models;

/// <summary>
/// Statistics about the current state of the vector store.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides metrics for monitoring ingestion status,
/// storage utilization, and system health. Used for displaying statistics in the UI
/// and for operational monitoring.</para>
/// <para><b>Code Flow:</b> Created by IVectorStore.GetStatsAsync by querying the
/// underlying database. Returned to callers via ITechieRag.GetStatsAsync.</para>
/// <para><b>Design:</b> Uses init-only properties to ensure statistics are
/// a point-in-time snapshot and cannot be modified after retrieval.</para>
/// </remarks>
public class IngestionStats
{
    /// <summary>
    /// Gets the total number of documents in the vector store.
    /// </summary>
    /// <remarks>
    /// Count of unique documents that have been ingested.
    /// Each document may contain multiple chunks.
    /// </remarks>
    public int TotalDocuments { get; init; }

    /// <summary>
    /// Gets the total number of chunks across all documents.
    /// </summary>
    /// <remarks>
    /// Sum of all text chunks stored in the vector store.
    /// This represents the total number of embeddings stored.
    /// </remarks>
    public int TotalChunks { get; init; }

    /// <summary>
    /// Gets the approximate storage size in bytes.
    /// </summary>
    /// <remarks>
    /// Estimated total storage consumed by the vector store,
    /// including text content, embeddings, and metadata.
    /// Implementation-specific: may be exact for SQLite, estimated for cloud stores.
    /// </remarks>
    public long VectorStoreSizeBytes { get; init; }

    /// <summary>
    /// Gets the timestamp of the most recent ingestion operation.
    /// </summary>
    /// <remarks>
    /// Null if no documents have been ingested yet.
    /// Useful for tracking when the knowledge base was last updated.
    /// </remarks>
    public DateTime? LastIngestionTime { get; init; }

    /// <summary>
    /// Gets the name of the vector store implementation in use.
    /// </summary>
    /// <remarks>
    /// Human-readable name identifying the vector store type,
    /// such as "SQLite-vec", "PGVector", or "Qdrant".
    /// Useful for displaying configuration information in the UI.
    /// </remarks>
    public string VectorStoreName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the name of the embedding provider in use.
    /// </summary>
    /// <remarks>
    /// Human-readable name identifying the embedding provider,
    /// such as "Ollama", "Azure OpenAI", or "ONNX".
    /// Useful for displaying configuration information in the UI.
    /// </remarks>
    public string EmbeddingProviderName { get; init; } = string.Empty;
}
