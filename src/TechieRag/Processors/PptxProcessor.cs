using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using TechieRag.Abstractions;
using TechieRag.Models;
using DrawingText = DocumentFormat.OpenXml.Drawing.Text;

namespace TechieRag.Processors;

/// <summary>
/// Document processor for Microsoft PowerPoint PPTX presentations using OpenXml.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Extracts slide body text and speaker notes in slide order, then splits
/// the result into chunks suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b>
/// 1) Opens the presentation from a seekable stream using the OpenXml SDK
/// 2) Walks slides in presentation order, emitting a "Slide N" heading per slide
/// 3) Appends shape text runs, then any speaker notes under a "Notes:" label
/// 4) Uses TextChunker to split the flattened text into sized chunks
/// </para>
/// <para><b>Dependencies:</b> Requires the DocumentFormat.OpenXml NuGet package (shared with
/// <see cref="DocxProcessor"/> and <see cref="XlsxProcessor"/>).</para>
/// <para><b>Limitations:</b> Extracts text only; images, SmartArt graphics, embedded media and
/// slide transitions are ignored.</para>
/// </remarks>
public class PptxProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list containing ".pptx".</value>
    public IReadOnlyList<string> SupportedExtensions => [".pptx"];

    /// <summary>
    /// Processes a PowerPoint presentation stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The PPTX presentation content stream.</param>
    /// <param name="fileName">The original file name (used for metadata).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the presentation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var processor = new PptxProcessor();
    /// using var stream = File.OpenRead("deck.pptx");
    /// var chunks = await processor.ProcessAsync(stream, "deck.pptx");
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

        // OpenXml requires a seekable stream
        using var memoryStream = new MemoryStream();
        content.CopyTo(memoryStream);
        memoryStream.Position = 0;

        using var document = PresentationDocument.Open(memoryStream, false);
        var presentationPart = document.PresentationPart;
        if (presentationPart is null)
        {
            return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullText = ExtractPresentationText(presentationPart, cancellationToken);
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
        }

        BuildChunks(chunks, fullText, fileName, options, cancellationToken);
        return Task.FromResult<IReadOnlyList<TextChunk>>(chunks);
    }

    /// <summary>
    /// Flattens every slide in the presentation into readable text, in slide order.
    /// </summary>
    /// <param name="presentationPart">The presentation part to read.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Slide-by-slide text with a "Slide N" heading per slide.</returns>
    private static string ExtractPresentationText(
        PresentationPart presentationPart,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var slideNumber = 0;

        foreach (var slidePart in EnumerateSlidesInOrder(presentationPart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            slideNumber++;
            AppendSlide(builder, slideNumber, slidePart);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Enumerates slide parts following the presentation's slide id list order.
    /// </summary>
    /// <param name="presentationPart">The presentation part to read.</param>
    /// <returns>Slide parts in on-screen order.</returns>
    private static IEnumerable<SlidePart> EnumerateSlidesInOrder(PresentationPart presentationPart)
    {
        var slideIds = presentationPart.Presentation?.SlideIdList?.Elements<SlideId>();
        if (slideIds is null)
        {
            yield break;
        }

        foreach (var slideId in slideIds)
        {
            if (slideId.RelationshipId?.Value is not string relationshipId)
            {
                continue;
            }

            if (presentationPart.GetPartById(relationshipId) is SlidePart slidePart)
            {
                yield return slidePart;
            }
        }
    }

    /// <summary>
    /// Appends one slide's heading, body text and speaker notes to the builder.
    /// </summary>
    /// <param name="builder">The destination text builder.</param>
    /// <param name="slideNumber">The one-based slide position.</param>
    /// <param name="slidePart">The slide part to read.</param>
    private static void AppendSlide(StringBuilder builder, int slideNumber, SlidePart slidePart)
    {
        var body = CollectText(slidePart.Slide);
        var notes = slidePart.NotesSlidePart is null
            ? string.Empty
            : CollectText(slidePart.NotesSlidePart.NotesSlide);

        builder.AppendLine($"# Slide {slideNumber}");

        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.AppendLine(body);
        }

        if (!string.IsNullOrWhiteSpace(notes))
        {
            builder.AppendLine($"Notes: {notes}");
        }

        builder.AppendLine();
    }

    /// <summary>
    /// Collects all drawing text runs beneath an OpenXml element.
    /// </summary>
    /// <param name="root">The slide or notes-slide element to walk; null yields an empty string.</param>
    /// <returns>Text runs joined by newlines.</returns>
    private static string CollectText(DocumentFormat.OpenXml.OpenXmlElement? root)
    {
        if (root is null)
        {
            return string.Empty;
        }

        var lines = root.Descendants<DrawingText>()
            .Select(run => run.Text.Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Splits extracted text into chunks and appends them to the result list.
    /// </summary>
    /// <param name="chunks">The destination chunk list.</param>
    /// <param name="fullText">The extracted presentation text.</param>
    /// <param name="fileName">The original file name.</param>
    /// <param name="options">The processing options in effect.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private static void BuildChunks(
        List<TextChunk> chunks,
        string fullText,
        string fileName,
        DocumentProcessingOptions options,
        CancellationToken cancellationToken)
    {
        var documentId = Path.GetFileNameWithoutExtension(fileName);
        var textChunks = TextChunker.ChunkText(
            fullText,
            options.MaxChunkSize,
            options.ChunkOverlap,
            options.Chunker);

        var chunkIndex = 0;
        foreach (var chunkText in textChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            chunks.Add(new TextChunk
            {
                DocumentId = documentId,
                Text = chunkText,
                ChunkIndex = chunkIndex++,
                Metadata = CreateMetadata(fileName, options.Metadata)
            });
        }
    }

    /// <summary>
    /// Creates the metadata dictionary attached to every chunk from this presentation.
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
            ["processorType"] = nameof(PptxProcessor)
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
