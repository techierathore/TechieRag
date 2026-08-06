using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Processors;

/// <summary>
/// Document processor for Microsoft Word DOCX files using OpenXml.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Extracts text content from Word documents and splits it into
/// chunks suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b>
/// 1) Opens DOCX document from stream using OpenXml SDK
/// 2) Extracts text from paragraphs preserving document structure
/// 3) Uses TextChunker to split text into appropriately sized chunks
/// 4) Creates TextChunk objects with metadata
/// </para>
/// <para><b>Dependencies:</b> Requires DocumentFormat.OpenXml NuGet package.</para>
/// <para><b>Limitations:</b> Extracts text only; formatting, images, and embedded
/// objects are not processed.</para>
/// </remarks>
public class DocxProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list containing ".docx".</value>
    public IReadOnlyList<string> SupportedExtensions => [".docx"];

    /// <summary>
    /// Processes a Word document stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The DOCX document content stream.</param>
    /// <param name="fileName">The original file name (used for metadata).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the Word document.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Copy stream to MemoryStream (OpenXml requires seekable stream)</description></item>
    /// <item><description>Open DOCX document using OpenXml SDK</description></item>
    /// <item><description>Extract text from all paragraphs in document body</description></item>
    /// <item><description>Chunk text using TextChunker with configured options</description></item>
    /// <item><description>Create TextChunk objects with chunk index metadata</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var processor = new DocxProcessor();
    /// using var stream = File.OpenRead("document.docx");
    /// var chunks = await processor.ProcessAsync(stream, "document.docx");
    /// </code>
    /// </example>
    public Task<IReadOnlyList<TextChunk>> ProcessAsync(
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

        // OpenXml requires a seekable stream
        using var memoryStream = new MemoryStream();
        content.CopyTo(memoryStream);
        memoryStream.Position = 0;

        using var document = WordprocessingDocument.Open(memoryStream, false);
        var body = document.MainDocumentPart?.Document?.Body;

        if (body == null)
        {
            return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullText = ExtractDocumentText(body);
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
        }

        var textChunks = TextChunker.ChunkText(
            fullText,
            options.MaxChunkSize,
            options.ChunkOverlap,
            options.Chunker);

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

        return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
    }

    /// <summary>
    /// Extracts text content from the Word document body.
    /// </summary>
    /// <param name="body">The document body element.</param>
    /// <returns>Extracted text with paragraphs separated by double newlines.</returns>
    private static string ExtractDocumentText(Body body)
    {
        var builder = new StringBuilder();
        var paragraphs = body.Descendants<Paragraph>();

        foreach (var paragraph in paragraphs)
        {
            var text = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine();
                }
                builder.Append(text.Trim());
            }
        }

        return builder.ToString();
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
            ["processorType"] = nameof(DocxProcessor)
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
