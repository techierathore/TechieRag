namespace TechieRag.Models;

/// <summary>
/// Represents a chunk of text extracted from a document with its vector embedding.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Core data structure for storing document content. Each document is split
/// into multiple chunks for more precise retrieval during semantic search operations.</para>
/// <para><b>Code Flow:</b> Created by IDocumentProcessor during ingestion. Vector is populated
/// by IEmbeddingProvider. Stored and retrieved via IVectorStore.</para>
/// <para><b>Design:</b> Uses a combination of mutable properties for data that changes during
/// processing (Id, Vector) and required properties for essential fields (DocumentId, Text).</para>
/// </remarks>
public class TextChunk
{
    /// <summary>
    /// Gets or sets the unique identifier for this chunk.
    /// </summary>
    /// <remarks>
    /// Automatically initialized with a new GUID. Can be overwritten during upsert operations
    /// or when loading from persistent storage.
    /// </remarks>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets or sets the ID of the parent document this chunk belongs to.
    /// </summary>
    /// <remarks>
    /// This is a required property that must be set during initialization.
    /// Used to group chunks by document for filtering and deletion operations.
    /// </remarks>
    public required string DocumentId { get; set; }

    /// <summary>
    /// Gets or sets the text content of this chunk.
    /// </summary>
    /// <remarks>
    /// This is a required property that must be set during initialization.
    /// Contains the actual text that will be embedded and searched against.
    /// </remarks>
    public required string Text { get; set; }

    /// <summary>
    /// Gets or sets the vector embedding of the text.
    /// </summary>
    /// <remarks>
    /// Null when first created by document processors. Populated by IEmbeddingProvider
    /// during ingestion before being stored in the vector store.
    /// Dimension typically 1024 for BGE-M3 model.
    /// </remarks>
    public float[]? Vector { get; set; }

    /// <summary>
    /// Gets or sets the page number in the source document.
    /// </summary>
    /// <remarks>
    /// Applicable for document types with pages (PDF, DOCX).
    /// Null for documents without page concepts (plain text, code files).
    /// </remarks>
    public int? PageNumber { get; set; }

    /// <summary>
    /// Gets or sets the sequential index of this chunk within the document.
    /// </summary>
    /// <remarks>
    /// Zero-based index indicating the position of this chunk relative to other chunks
    /// from the same document. Useful for reconstructing document order.
    /// </remarks>
    public int? ChunkIndex { get; set; }

    /// <summary>
    /// Gets or sets additional metadata associated with this chunk.
    /// </summary>
    /// <remarks>
    /// Flexible key-value storage for custom metadata like source file path,
    /// section headings, language, or any application-specific data.
    /// Initialized to an empty dictionary by default.
    /// </remarks>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp when this chunk was created.
    /// </summary>
    /// <remarks>
    /// Automatically initialized to the current UTC time.
    /// Useful for tracking ingestion timing and data freshness.
    /// </remarks>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
