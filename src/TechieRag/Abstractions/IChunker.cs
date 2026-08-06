namespace TechieRag.Abstractions;

/// <summary>
/// Abstraction for text chunking strategies used during document ingestion.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Allows the chunking algorithm to be swapped per ingestion —
/// recursive (default), token-based, markdown/code-aware, or sentence-boundary chunking.</para>
/// <para><b>Code Flow:</b> Selected via <see cref="TechieRag.ChunkingStrategy"/> configuration or
/// TechieRagBuilder.WithCustomChunker. Document processors invoke the configured chunker
/// through <see cref="Models"/> processing options; TechieRagClient uses it for raw text ingestion.</para>
/// <para><b>Implementations:</b> RecursiveChunker, TokenChunker, MarkdownChunker, SentenceChunker.</para>
/// </remarks>
public interface IChunker
{
    /// <summary>
    /// Gets the display name of this chunking strategy.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Splits text into chunks suitable for embedding.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <param name="maxChunkSize">Maximum size of each chunk in characters.</param>
    /// <param name="chunkOverlap">Number of overlapping characters between consecutive chunks.</param>
    /// <returns>An enumerable of text chunks in document order.</returns>
    /// <remarks>
    /// <para><b>Contract:</b> Implementations must never return empty or whitespace-only chunks
    /// and must preserve the original reading order of the text.</para>
    /// </remarks>
    IEnumerable<string> Chunk(string text, int maxChunkSize, int chunkOverlap);
}
