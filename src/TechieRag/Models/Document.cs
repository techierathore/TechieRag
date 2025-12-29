namespace TechieRag.Models;

/// <summary>
/// Represents an ingested document in the vector store.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Tracks document-level metadata and provides a reference for
/// managing all chunks belonging to a document. Acts as the parent entity for TextChunk objects.</para>
/// <para><b>Code Flow:</b> Created by TechieRagClient during document ingestion.
/// Stored in the vector store alongside chunks. Retrieved via ListDocumentsAsync for UI display.</para>
/// <para><b>Design:</b> Uses init-only properties to ensure immutability after construction,
/// reflecting that document metadata should not change after ingestion.</para>
/// </remarks>
public class Document
{
    /// <summary>
    /// Gets the unique identifier for this document.
    /// </summary>
    /// <remarks>
    /// This is a required property that must be set during initialization.
    /// Typically a GUID or hash-based identifier generated during ingestion.
    /// Referenced by TextChunk.DocumentId for parent-child relationship.
    /// </remarks>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the display name of the document.
    /// </summary>
    /// <remarks>
    /// This is a required property that must be set during initialization.
    /// Typically the original file name without path, used for display in UI.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the original file path or source URL of the document.
    /// </summary>
    /// <remarks>
    /// This is a required property that must be set during initialization.
    /// Full path to the source file for file-based ingestion, or a descriptive
    /// identifier for text-based ingestion (e.g., "inline:user-input").
    /// </remarks>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Gets the number of chunks this document was split into.
    /// </summary>
    /// <remarks>
    /// Set after document processing completes. Useful for displaying
    /// document statistics and estimating storage requirements.
    /// </remarks>
    public int ChunkCount { get; init; }

    /// <summary>
    /// Gets the timestamp when this document was ingested.
    /// </summary>
    /// <remarks>
    /// Records the UTC time when ingestion completed successfully.
    /// Useful for tracking data freshness and re-ingestion schedules.
    /// </remarks>
    public DateTime IngestedAt { get; init; }

    /// <summary>
    /// Gets additional metadata associated with the document.
    /// </summary>
    /// <remarks>
    /// Flexible key-value storage for custom metadata like file size,
    /// author, content type, or any application-specific data.
    /// Initialized to an empty dictionary by default.
    /// </remarks>
    public Dictionary<string, object> Metadata { get; init; } = new();
}
