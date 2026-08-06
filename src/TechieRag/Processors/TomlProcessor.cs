using System.Text;
using TechieRag.Abstractions;
using TechieRag.Models;
using Tomlyn;
using Tomlyn.Model;

namespace TechieRag.Processors;

/// <summary>
/// Document processor for TOML files using Tomlyn library.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Parses TOML files and formats them for readability,
/// then splits into chunks suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b>
/// 1) Reads TOML content from stream
/// 2) Parses TOML using Tomlyn and formats for readability
/// 3) Uses TextChunker to split text into appropriately sized chunks
/// 4) Creates TextChunk objects with metadata
/// </para>
/// <para><b>Dependencies:</b> Requires Tomlyn NuGet package for TOML parsing.</para>
/// <para><b>Design:</b> Formats parsed TOML with consistent formatting to improve
/// embedding quality for configuration data.</para>
/// </remarks>
public class TomlProcessor : IDocumentProcessor
{
    /// <summary>
    /// Gets the list of file extensions this processor supports.
    /// </summary>
    /// <value>A read-only list containing ".toml".</value>
    public IReadOnlyList<string> SupportedExtensions => [".toml"];

    /// <summary>
    /// Processes a TOML file stream and returns text chunks ready for embedding.
    /// </summary>
    /// <param name="content">The TOML file content stream.</param>
    /// <param name="fileName">The original file name (used for metadata).</param>
    /// <param name="options">Optional processing configuration for chunk size and overlap.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of text chunks extracted from the TOML file.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Read TOML content from stream</description></item>
    /// <item><description>Parse TOML using Tomlyn</description></item>
    /// <item><description>Format parsed content for readability</description></item>
    /// <item><description>Chunk text using TextChunker with configured options</description></item>
    /// <item><description>Create TextChunk objects with chunk index metadata</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when content or fileName is null.</exception>
    /// <example>
    /// <code>
    /// var processor = new TomlProcessor();
    /// using var stream = File.OpenRead("config.toml");
    /// var chunks = await processor.ProcessAsync(stream, "config.toml");
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
        var toml = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(toml))
        {
            return chunks;
        }

        // Format TOML for better readability
        var formattedToml = FormatToml(toml);

        if (string.IsNullOrWhiteSpace(formattedToml))
        {
            return chunks;
        }

        var textChunks = TextChunker.ChunkText(
            formattedToml,
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
    /// Parses and formats TOML content.
    /// </summary>
    /// <param name="toml">The TOML string to parse and format.</param>
    /// <returns>Formatted TOML string.</returns>
    /// <remarks>
    /// If the TOML is invalid, returns the original content as-is to allow
    /// processing to continue with the raw text.
    /// </remarks>
    private static string FormatToml(string toml)
    {
        try
        {
            var model = Toml.ToModel(toml);
            return Toml.FromModel(model);
        }
        catch (TomlException)
        {
            // If TOML is malformed, return as-is for text processing
            return toml;
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
            ["processorType"] = nameof(TomlProcessor)
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
