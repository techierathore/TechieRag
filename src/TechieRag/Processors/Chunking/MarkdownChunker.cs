using System.Text;
using TechieRag.Abstractions;

namespace TechieRag.Processors.Chunking;

/// <summary>
/// Markdown/code-aware chunking strategy that splits on heading boundaries and keeps
/// fenced code blocks intact.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Structure-aware chunking for markdown documents and technical
/// content: each chunk stays within one heading section, section headings are preserved at
/// the start of their chunks, and fenced code blocks (``` ... ```) are never split.</para>
/// <para><b>Fallback:</b> Oversized prose within a section falls back to recursive
/// chunking; an oversized code fence is emitted whole as a single chunk.</para>
/// </remarks>
public class MarkdownChunker : IChunker
{
    /// <inheritdoc/>
    public string Name => "Markdown";

    /// <inheritdoc/>
    public IEnumerable<string> Chunk(string text, int maxChunkSize, int chunkOverlap)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunkSize);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkOverlap);

        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var block in SplitIntoBlocks(text))
        {
            if (block.IsCodeFence)
            {
                // Keep code fences whole, even if oversized
                yield return block.Text;
                continue;
            }

            if (block.Text.Length <= maxChunkSize)
            {
                yield return block.Text;
                continue;
            }

            // Oversized prose section: recursive fallback, prefixed with its heading
            foreach (var sub in TextChunker.ChunkText(block.Body, maxChunkSize, chunkOverlap))
            {
                yield return string.IsNullOrEmpty(block.Heading) ? sub : $"{block.Heading}\n{sub}";
            }
        }
    }

    private sealed record MarkdownBlock(string Text, string Heading, string Body, bool IsCodeFence);

    private static List<MarkdownBlock> SplitIntoBlocks(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<MarkdownBlock>();
        var heading = string.Empty;
        var body = new StringBuilder();
        var fence = new StringBuilder();
        var inFence = false;

        void FlushSection()
        {
            var bodyText = body.ToString().Trim();
            body.Clear();
            if (bodyText.Length == 0 && heading.Length == 0) return;
            var full = heading.Length > 0
                ? (bodyText.Length > 0 ? $"{heading}\n{bodyText}" : heading)
                : bodyText;
            if (full.Length > 0) blocks.Add(new MarkdownBlock(full, heading, bodyText, false));
        }

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```"))
            {
                if (!inFence)
                {
                    FlushSection();
                    inFence = true;
                    fence.Clear();
                    fence.AppendLine(line);
                }
                else
                {
                    fence.Append(line);
                    blocks.Add(new MarkdownBlock(fence.ToString().Trim(), heading, fence.ToString().Trim(), true));
                    inFence = false;
                }
                continue;
            }

            if (inFence)
            {
                fence.AppendLine(line);
                continue;
            }

            if (IsHeading(trimmed))
            {
                FlushSection();
                heading = trimmed;
                continue;
            }

            body.AppendLine(line);
        }

        if (inFence)
        {
            // Unterminated fence: emit what we have as a code block
            blocks.Add(new MarkdownBlock(fence.ToString().Trim(), heading, fence.ToString().Trim(), true));
        }

        FlushSection();
        return blocks;
    }

    private static bool IsHeading(string line)
    {
        if (!line.StartsWith('#')) return false;
        var level = 0;
        while (level < line.Length && line[level] == '#') level++;
        return level <= 6 && level < line.Length && line[level] == ' ';
    }
}
