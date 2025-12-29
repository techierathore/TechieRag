using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Processors;

/// <summary>
/// Document processor for plain text files.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Processes plain text files and splits them into chunks
/// suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b>
/// 1) Reads text content from stream
/// 2) Uses TextChunker to split text into appropriately sized chunks
/// 3) Creates TextChunk objects with metadata
/// </para>
/// <para><b>Simplicity:</b> This is the simplest processor as text files require
/// no format conversion, only chunking.</para>
/// </remarks>
public class TextProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list containing ".txt".</value>
    public IReadOnlyList<string> SupportedExtensions => [".txt"];

    /// <summary>
    /// Processes a plain text file stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The text file content stream.</param>
    /// <param name="fileName">The original file name (used for metadata).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the text file.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Read entire text content from stream</description></item>
    /// <item><description>Chunk text using TextChunker with configured options</description></item>
    /// <item><description>Create TextChunk objects with chunk index metadata</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var processor = new TextProcessor();
    /// using var stream = File.OpenRead("document.txt");
    /// var chunks = await processor.ProcessAsync(stream, "document.txt");
    /// </code>
    /// </example>
    public async Task<IReadOnlyList<TextChunk>> ProcessAsync(
        Stream content,
        string fileName,
        DocumentProcessingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileName);

        options ??= new DocumentProcessingOptions();
        var chunks = new List<TextChunk>();
        var documentId = Path.GetFileNameWithoutExtension(fileName);

        using var reader = new StreamReader(content);
        var text = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
        {
            return chunks;
        }

        var textChunks = TextChunker.ChunkText(
            text,
            options.MaxChunkSize,
            options.ChunkOverlap);

        var chunkIndex = 0;
        foreach (var chunkText in textChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = new TextChunk
            {
                DocumentId = documentId,
                Text = chunkText,
                ChunkIndex = chunkIndex++,
                Metadata = CreateMetadata(fileName, options.Metadata)
            };

            chunks.Add(chunk);
        }

        return chunks;
    }

    /// <summary>
    /// Creates metadata dictionary for a text chunk.
    /// </summary>
    /// <param name="fileName">The source file name.</param>
    /// <param name="additionalMetadata">Additional metadata from processing options.</param>
    /// <returns>Dictionary containing chunk metadata.</returns>
    private static Dictionary<string, object> CreateMetadata(
        string fileName,
        Dictionary<string, object>? additionalMetadata)
    {
        var metadata = new Dictionary<string, object>
        {
            ["sourceFile"] = fileName,
            ["processorType"] = nameof(TextProcessor)
        };

        if (additionalMetadata != null)
        {
            foreach (var kvp in additionalMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return metadata;
    }
}
