using Markdig;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Processors.Chunking;

namespace TechieRag.Processors;

/// <summary>
/// Document processor for Markdown files using Markdig library.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Parses Markdown files, converts them to plain text,
/// and splits into chunks suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b>
/// 1) Reads Markdown content from stream
/// 2) Parses Markdown using Markdig and converts to plain text; when the configured chunker is
/// the <see cref="MarkdownChunker"/>, heading markers and code fences are kept so the chunker can
/// split on heading boundaries
/// 3) Uses TextChunker to split text into appropriately sized chunks
/// 4) Creates TextChunk objects with metadata
/// </para>
/// <para><b>Dependencies:</b> Requires Markdig NuGet package for Markdown parsing.</para>
/// <para><b>Design Choice:</b> Converts to plain text rather than HTML to produce
/// cleaner chunks for embedding without HTML artifacts.</para>
/// </remarks>
public class MarkdownProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list containing ".md" and ".markdown".</value>
    public IReadOnlyList<string> SupportedExtensions => [".md", ".markdown"];

    /// <summary>
    /// Markdig pipeline configured for plain text output.
    /// </summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Processes a Markdown file stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The Markdown file content stream.</param>
    /// <param name="fileName">The original file name (used for metadata).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the Markdown file.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Read Markdown content from stream</description></item>
    /// <item><description>Parse and convert to plain text using Markdig</description></item>
    /// <item><description>Chunk text using TextChunker with configured options</description></item>
    /// <item><description>Create TextChunk objects with chunk index metadata</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var processor = new MarkdownProcessor();
    /// using var stream = File.OpenRead("document.md");
    /// var chunks = await processor.ProcessAsync(stream, "document.md");
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
        var markdown = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return chunks;
        }

        // Parse Markdown and convert to plain text. The markdown strategy splits on heading
        // boundaries and keeps code fences whole, so it gets the structure-preserving text
        // (heading markers and fences kept, inline formatting stripped); every other strategy
        // gets the plain text exactly as before (REQ-RAG-071).
        var plainText = options.Chunker is MarkdownChunker
            ? ConvertMarkdownToStructuredText(markdown)
            : ConvertMarkdownToPlainText(markdown);

        if (string.IsNullOrWhiteSpace(plainText))
        {
            return chunks;
        }

        var textChunks = TextChunker.ChunkText(
            plainText,
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

        return chunks;
    }

    /// <summary>
    /// Converts Markdown content to plain text.
    /// </summary>
    /// <param name="markdown">The Markdown content to convert.</param>
    /// <returns>Plain text representation of the Markdown content.</returns>
    /// <remarks>
    /// Uses Markdig to parse the Markdown and extracts plain text by converting
    /// to HTML first, then stripping all HTML tags. This ensures proper handling
    /// of complex Markdown structures.
    /// </remarks>
    private static string ConvertMarkdownToPlainText(string markdown)
    {
        // Use Markdig to convert to plain text
        var document = Markdown.Parse(markdown, Pipeline);
        return ExtractPlainText(document);
    }

    /// <summary>
    /// Converts Markdown content to text that keeps the document's structure for the
    /// markdown chunking strategy.
    /// </summary>
    /// <param name="markdown">The Markdown content to convert.</param>
    /// <returns>Text with ATX heading markers (<c>#</c> to <c>######</c>) and fenced code blocks
    /// preserved, and inline formatting (emphasis, links, images) reduced to its text.</returns>
    /// <remarks>
    /// Setext headings (underlined with <c>===</c> or <c>---</c>) are emitted as ATX headings and
    /// indented code blocks as fenced ones, so <see cref="MarkdownChunker"/> recognises both.
    /// </remarks>
    private static string ConvertMarkdownToStructuredText(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        var writer = new System.IO.StringWriter();
        ExtractStructuredTextFromBlock(document, writer);
        return writer.ToString().Trim();
    }

    /// <summary>
    /// Recursively writes Markdown blocks as structure-preserving text.
    /// </summary>
    /// <param name="block">The Markdown block to process.</param>
    /// <param name="writer">The text writer to write extracted text to.</param>
    private static void ExtractStructuredTextFromBlock(Markdig.Syntax.MarkdownObject block, System.IO.TextWriter writer)
    {
        switch (block)
        {
            case Markdig.Syntax.HeadingBlock heading:
                writer.WriteLine();
                writer.Write(new string('#', Math.Clamp(heading.Level, 1, 6)));
                writer.Write(' ');
                if (heading.Inline != null)
                {
                    ExtractTextFromInline(heading.Inline, writer);
                }
                writer.WriteLine();
                break;

            case Markdig.Syntax.CodeBlock code:
                var info = (code as Markdig.Syntax.FencedCodeBlock)?.Info;
                writer.WriteLine();
                writer.WriteLine($"```{info}");
                writer.WriteLine(code.Lines.ToString());
                writer.WriteLine("```");
                break;

            case Markdig.Syntax.LeafBlock:
                ExtractTextFromBlock(block, writer);
                break;

            case Markdig.Syntax.ContainerBlock containerBlock:
                foreach (var child in containerBlock)
                {
                    ExtractStructuredTextFromBlock(child, writer);
                }
                break;
        }
    }

    /// <summary>
    /// Extracts plain text from a parsed Markdown document.
    /// </summary>
    /// <param name="document">The parsed Markdown document.</param>
    /// <returns>Plain text extracted from the document.</returns>
    private static string ExtractPlainText(Markdig.Syntax.MarkdownDocument document)
    {
        var writer = new System.IO.StringWriter();
        ExtractTextFromBlock(document, writer);
        return writer.ToString().Trim();
    }

    /// <summary>
    /// Recursively extracts text from Markdown blocks.
    /// </summary>
    /// <param name="block">The Markdown block to process.</param>
    /// <param name="writer">The text writer to write extracted text to.</param>
    private static void ExtractTextFromBlock(Markdig.Syntax.MarkdownObject block, System.IO.TextWriter writer)
    {
        switch (block)
        {
            case Markdig.Syntax.LeafBlock leafBlock:
                if (leafBlock.Inline != null)
                {
                    ExtractTextFromInline(leafBlock.Inline, writer);
                }
                writer.WriteLine();
                break;

            case Markdig.Syntax.ContainerBlock containerBlock:
                foreach (var child in containerBlock)
                {
                    ExtractTextFromBlock(child, writer);
                }
                break;
        }
    }

    /// <summary>
    /// Extracts text from inline Markdown elements.
    /// </summary>
    /// <param name="inline">The inline element to process.</param>
    /// <param name="writer">The text writer to write extracted text to.</param>
    private static void ExtractTextFromInline(Markdig.Syntax.Inlines.Inline inline, System.IO.TextWriter writer)
    {
        switch (inline)
        {
            case Markdig.Syntax.Inlines.LiteralInline literal:
                writer.Write(literal.Content);
                break;

            case Markdig.Syntax.Inlines.ContainerInline container:
                var child = container.FirstChild;
                while (child != null)
                {
                    ExtractTextFromInline(child, writer);
                    child = child.NextSibling;
                }
                break;

            case Markdig.Syntax.Inlines.LineBreakInline:
                writer.Write(' ');
                break;
        }
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
            ["processorType"] = nameof(MarkdownProcessor)
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
