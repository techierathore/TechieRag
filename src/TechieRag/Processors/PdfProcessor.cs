using System.Text;
using TechieRag.Abstractions;
using TechieRag.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TechieRag.Processors;

/// <summary>
/// Document processor for PDF files using PdfPig library.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Extracts text content from PDF documents and splits it into
/// chunks suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b>
/// 1) Opens PDF document from stream using PdfPig
/// 2) Iterates through pages extracting text content
/// 3) Uses TextChunker to split text into appropriately sized chunks
/// 4) Creates TextChunk objects with page number metadata
/// </para>
/// <para><b>Dependencies:</b> Requires PdfPig NuGet package for PDF parsing.</para>
/// <para><b>Limitations:</b> Text extraction quality depends on PDF structure.
/// Scanned documents without OCR will not yield text content.</para>
/// </remarks>
public class PdfProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list containing ".pdf".</value>
    public IReadOnlyList<string> SupportedExtensions => [".pdf"];

    /// <summary>
    /// Processes a PDF document stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The PDF document content stream.</param>
    /// <param name="fileName">The original file name (used for metadata).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the PDF document.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Copy stream to MemoryStream (PdfPig requires seekable stream)</description></item>
    /// <item><description>Open PDF document using PdfPig</description></item>
    /// <item><description>Extract text from each page</description></item>
    /// <item><description>Chunk text using TextChunker with configured options</description></item>
    /// <item><description>Create TextChunk objects with page number and chunk index metadata</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var processor = new PdfProcessor();
    /// using var stream = File.OpenRead("document.pdf");
    /// var chunks = await processor.ProcessAsync(stream, "document.pdf");
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

        // PdfPig requires a seekable stream, so copy to MemoryStream if needed
        using var memoryStream = new MemoryStream();
        content.CopyTo(memoryStream);
        memoryStream.Position = 0;

        using var document = PdfDocument.Open(memoryStream);
        var chunkIndex = 0;

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageText = ExtractPageText(page);
            if (string.IsNullOrWhiteSpace(pageText))
            {
                continue;
            }

            var textChunks = TextChunker.ChunkText(
                pageText,
                options.MaxChunkSize,
                options.ChunkOverlap);

            foreach (var chunkText in textChunks)
            {
                var chunk = new TextChunk
                {
                    DocumentId = documentId,
                    Text = chunkText,
                    PageNumber = page.Number,
                    ChunkIndex = chunkIndex++,
                    Metadata = CreateMetadata(fileName, page.Number, options.Metadata)
                };

                chunks.Add(chunk);
            }
        }

        return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
    }

    /// <summary>
    /// Extracts text content from a PDF page.
    /// </summary>
    /// <param name="page">The PDF page to extract text from.</param>
    /// <returns>Extracted text content with normalized whitespace.</returns>
    private static string ExtractPageText(Page page)
    {
        var text = page.Text;

        // Normalize whitespace
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Clean up common PDF artifacts
        var builder = new StringBuilder(text.Length);
        var lastCharWasSpace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastCharWasSpace)
                {
                    builder.Append(' ');
                    lastCharWasSpace = true;
                }
            }
            else
            {
                builder.Append(c);
                lastCharWasSpace = false;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Creates metadata dictionary for a text chunk.
    /// </summary>
    /// <param name="fileName">The source file name.</param>
    /// <param name="pageNumber">The page number in the source document.</param>
    /// <param name="additionalMetadata">Additional metadata from processing options.</param>
    /// <returns>Dictionary containing chunk metadata.</returns>
    private static Dictionary<string, object> CreateMetadata(
        string fileName,
        int pageNumber,
        Dictionary<string, object>? additionalMetadata)
    {
        var metadata = new Dictionary<string, object>
        {
            ["sourceFile"] = fileName,
            ["pageNumber"] = pageNumber,
            ["processorType"] = nameof(PdfProcessor)
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
