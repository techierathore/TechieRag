using System.Text;
using HtmlAgilityPack;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Processors;

/// <summary>
/// Document processor for HTML files using HtmlAgilityPack.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Extracts text content from HTML documents by stripping tags
/// and splits into chunks suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b>
/// 1) Reads HTML content from stream
/// 2) Parses HTML using HtmlAgilityPack
/// 3) Strips HTML tags and extracts text content
/// 4) Uses TextChunker to split text into appropriately sized chunks
/// 5) Creates TextChunk objects with metadata
/// </para>
/// <para><b>Dependencies:</b> Requires HtmlAgilityPack NuGet package for HTML parsing.</para>
/// <para><b>Design:</b> Removes script, style, and other non-content elements before
/// extracting text to produce clean chunks.</para>
/// </remarks>
public class HtmlProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list containing ".html" and ".htm".</value>
    public IReadOnlyList<string> SupportedExtensions => [".html", ".htm"];

    /// <summary>
    /// HTML tags that should be completely removed (including their content).
    /// </summary>
    private static readonly HashSet<string> TagsToRemove = new(StringComparer.OrdinalIgnoreCase)
    {
        "script",
        "style",
        "noscript",
        "svg",
        "canvas",
        "iframe",
        "object",
        "embed",
        "head"
    };

    /// <summary>
    /// Processes an HTML file stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The HTML file content stream.</param>
    /// <param name="fileName">The original file name (used for metadata).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the HTML file.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Read HTML content from stream</description></item>
    /// <item><description>Parse HTML using HtmlAgilityPack</description></item>
    /// <item><description>Remove script, style, and other non-content elements</description></item>
    /// <item><description>Extract text from remaining content</description></item>
    /// <item><description>Chunk text using TextChunker with configured options</description></item>
    /// <item><description>Create TextChunk objects with chunk index metadata</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var processor = new HtmlProcessor();
    /// using var stream = File.OpenRead("page.html");
    /// var chunks = await processor.ProcessAsync(stream, "page.html");
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
        var html = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(html))
        {
            return chunks;
        }

        var plainText = ExtractTextFromHtml(html);

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
    /// Extracts plain text from HTML content.
    /// </summary>
    /// <param name="html">The HTML content to process.</param>
    /// <returns>Plain text extracted from the HTML with tags removed.</returns>
    private static string ExtractTextFromHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove unwanted elements
        RemoveUnwantedNodes(doc);

        // Extract text from body, or document root if no body
        var body = doc.DocumentNode.SelectSingleNode("//body");
        var rootNode = body ?? doc.DocumentNode;

        var text = ExtractTextFromNode(rootNode);
        return NormalizeWhitespace(text);
    }

    /// <summary>
    /// Removes script, style, and other non-content nodes from the document.
    /// </summary>
    /// <param name="doc">The HTML document to process.</param>
    private static void RemoveUnwantedNodes(HtmlDocument doc)
    {
        var nodesToRemove = new List<HtmlNode>();

        foreach (var tagName in TagsToRemove)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//{tagName}");
            if (nodes != null)
            {
                nodesToRemove.AddRange(nodes);
            }
        }

        // Also remove HTML comments
        var comments = doc.DocumentNode.SelectNodes("//comment()");
        if (comments != null)
        {
            nodesToRemove.AddRange(comments);
        }

        foreach (var node in nodesToRemove)
        {
            node.Remove();
        }
    }

    /// <summary>
    /// Recursively extracts text from an HTML node.
    /// </summary>
    /// <param name="node">The HTML node to extract text from.</param>
    /// <returns>Text content of the node and its children.</returns>
    private static string ExtractTextFromNode(HtmlNode node)
    {
        var builder = new StringBuilder();

        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                var text = HtmlEntity.DeEntitize(child.InnerText);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.Append(text);
                }
            }
            else if (child.NodeType == HtmlNodeType.Element)
            {
                // Add line breaks for block elements
                if (IsBlockElement(child.Name))
                {
                    if (builder.Length > 0 && !builder.ToString().EndsWith('\n'))
                    {
                        builder.AppendLine();
                    }
                }

                builder.Append(ExtractTextFromNode(child));

                if (IsBlockElement(child.Name))
                {
                    builder.AppendLine();
                }
                else if (child.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendLine();
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Determines if an HTML element is a block-level element.
    /// </summary>
    /// <param name="tagName">The HTML tag name.</param>
    /// <returns>True if the element is block-level; otherwise false.</returns>
    private static bool IsBlockElement(string tagName)
    {
        return tagName.ToLowerInvariant() switch
        {
            "p" or "div" or "section" or "article" or "header" or "footer" or
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or
            "ul" or "ol" or "li" or "table" or "tr" or "td" or "th" or
            "blockquote" or "pre" or "hr" or "address" or "figure" or
            "figcaption" or "main" or "nav" or "aside" => true,
            _ => false
        };
    }

    /// <summary>
    /// Normalizes whitespace in extracted text.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>Text with normalized whitespace.</returns>
    private static string NormalizeWhitespace(string text)
    {
        // Replace multiple spaces with single space
        var builder = new StringBuilder(text.Length);
        var lastWasWhitespace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasWhitespace)
                {
                    builder.Append(c == '\n' ? '\n' : ' ');
                    lastWasWhitespace = true;
                }
            }
            else
            {
                builder.Append(c);
                lastWasWhitespace = false;
            }
        }

        // Replace multiple newlines with double newline
        var result = builder.ToString();
        while (result.Contains("\n\n\n"))
        {
            result = result.Replace("\n\n\n", "\n\n");
        }

        return result.Trim();
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
            ["processorType"] = nameof(HtmlProcessor)
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
