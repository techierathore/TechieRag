using System.Text;
using System.Text.Json;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Processors;

/// <summary>
/// Document processor for JSON files.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Parses JSON files and formats them for readability,
/// then splits into chunks suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b>
/// 1) Reads JSON content from stream
/// 2) Parses and pretty-prints JSON for better readability
/// 3) Uses TextChunker to split text into appropriately sized chunks
/// 4) Creates TextChunk objects with metadata
/// </para>
/// <para><b>Design:</b> Formats JSON with indentation to make the structure
/// more apparent in chunks, improving embedding quality for structured data.</para>
/// </remarks>
public class JsonProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list containing ".json".</value>
    public IReadOnlyList<string> SupportedExtensions => [".json"];

    /// <summary>
    /// JSON serializer options for pretty-printing.
    /// </summary>
    private static readonly JsonSerializerOptions PrettyPrintOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Processes a JSON file stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The JSON file content stream.</param>
    /// <param name="fileName">The original file name (used for metadata).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the JSON file.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Read JSON content from stream</description></item>
    /// <item><description>Parse and reformat JSON with indentation</description></item>
    /// <item><description>Chunk text using TextChunker with configured options</description></item>
    /// <item><description>Create TextChunk objects with chunk index metadata</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <exception cref="JsonException">Thrown when JSON parsing fails.</exception>
    /// <example>
    /// <code>
    /// var processor = new JsonProcessor();
    /// using var stream = File.OpenRead("data.json");
    /// var chunks = await processor.ProcessAsync(stream, "data.json");
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
        var json = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return chunks;
        }

        // Format JSON for better readability
        var formattedJson = FormatJson(json);

        if (string.IsNullOrWhiteSpace(formattedJson))
        {
            return chunks;
        }

        var textChunks = TextChunker.ChunkText(
            formattedJson,
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
    /// Formats JSON with indentation for improved readability.
    /// </summary>
    /// <param name="json">The JSON string to format.</param>
    /// <returns>Formatted JSON string with indentation.</returns>
    /// <remarks>
    /// If the JSON is invalid, returns the original content as-is to allow
    /// processing to continue with the raw text.
    /// </remarks>
    private static string FormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, PrettyPrintOptions);
        }
        catch (JsonException)
        {
            // If JSON is malformed, return as-is for text processing
            return json;
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
            ["processorType"] = nameof(JsonProcessor)
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
