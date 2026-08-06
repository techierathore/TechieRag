using TechieRag.Models;

namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for document parsing and chunking operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a unified interface for extracting text from various document
/// formats and splitting them into chunks suitable for embedding.</para>
/// <para><b>Code Flow:</b> TechieRagClient selects the appropriate processor based on file extension,
/// then calls ProcessAsync to extract and chunk the document content.</para>
/// <para><b>Implementations:</b> PdfProcessor, DocxProcessor, XlsxProcessor, PptxProcessor,
/// CsvProcessor, TextProcessor, MarkdownProcessor, HtmlProcessor, JsonProcessor, TomlProcessor,
/// CodeProcessor</para>
/// </remarks>
public interface IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports (e.g., ".pdf", ".docx").
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Processes a document stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The document content stream.</param>
    /// <param name="fileName">The original file name (used for metadata and extension detection).</param>
    /// <param name="options">Optional processing configuration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the document.</returns>
    /// <remarks>
    /// <para><b>Flow:</b> 1) Parse document format, 2) Extract text content,
    /// 3) Split into semantic chunks, 4) Return with page/position metadata.</para>
    /// </remarks>
    Task<IReadOnlyList<TextChunk>> ProcessAsync(
        Stream content,
        string fileName,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for document processing operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Allows customization of chunking behavior per document or globally.</para>
/// </remarks>
public class DocumentProcessingOptions
{
    /// <summary>
    /// Gets or sets the maximum size of each text chunk in characters.
    /// </summary>
    /// <remarks>Default is 500 characters, balancing context and retrieval precision.</remarks>
    public int MaxChunkSize { get; set; } = 500;

    /// <summary>
    /// Gets or sets the number of overlapping characters between consecutive chunks.
    /// </summary>
    /// <remarks>Overlap helps maintain context across chunk boundaries.</remarks>
    public int ChunkOverlap { get; set; } = 50;

    /// <summary>
    /// Gets or sets the language hint for language-specific processing.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets additional metadata to attach to all chunks from this document.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the chunking strategy to use for this document.
    /// </summary>
    /// <remarks>Null uses the default recursive strategy (backward compatible).</remarks>
    public IChunker? Chunker { get; set; }
}
