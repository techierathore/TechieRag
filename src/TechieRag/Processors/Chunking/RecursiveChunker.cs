using TechieRag.Abstractions;

namespace TechieRag.Processors.Chunking;

/// <summary>
/// Default chunking strategy that recursively splits on paragraph, sentence, and word
/// boundaries with overlap — identical to the historical TextChunker behavior.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backward-compatible default for <see cref="IChunker"/>. Delegates to
/// <see cref="TextChunker.ChunkText"/> so existing ingestion output is unchanged.</para>
/// </remarks>
public class RecursiveChunker : IChunker
{
    /// <summary>Gets a shared reusable instance (the chunker is stateless).</summary>
    public static RecursiveChunker Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "Recursive";

    /// <inheritdoc/>
    public IEnumerable<string> Chunk(string text, int maxChunkSize, int chunkOverlap) =>
        TextChunker.ChunkText(text, maxChunkSize, chunkOverlap);
}
