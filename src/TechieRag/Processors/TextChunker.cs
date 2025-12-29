using System.Text;

namespace TechieRag.Processors;

/// <summary>
/// Provides shared text chunking utilities for document processors.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Centralizes the logic for splitting text into semantic chunks
/// that are suitable for embedding and retrieval operations.</para>
/// <para><b>Code Flow:</b> All document processors use this utility after extracting
/// raw text from their respective formats. The chunks are then wrapped in TextChunk
/// objects with appropriate metadata.</para>
/// <para><b>Design:</b> Uses sentence and paragraph boundaries when possible to
/// create semantically meaningful chunks, with configurable overlap for context
/// preservation across boundaries.</para>
/// </remarks>
public static class TextChunker
{
    /// <summary>
    /// Sentence-ending punctuation marks used for boundary detection.
    /// </summary>
    private static readonly char[] SentenceEndings = ['.', '!', '?'];

    /// <summary>
    /// Splits text into chunks of approximately the specified maximum size with overlap.
    /// </summary>
    /// <param name="text">The text to chunk.</param>
    /// <param name="maxSize">Maximum size of each chunk in characters.</param>
    /// <param name="overlap">Number of overlapping characters between consecutive chunks.</param>
    /// <returns>An enumerable of text chunks.</returns>
    /// <remarks>
    /// <para><b>Algorithm:</b></para>
    /// <list type="number">
    /// <item><description>Split text into paragraphs (double newline separated)</description></item>
    /// <item><description>For each paragraph, split into sentences</description></item>
    /// <item><description>Accumulate sentences until maxSize is reached</description></item>
    /// <item><description>Create overlap by including trailing content from previous chunk</description></item>
    /// </list>
    /// <para><b>Edge Cases:</b> Handles single large sentences by splitting at word
    /// boundaries when they exceed maxSize.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var chunks = TextChunker.ChunkText(documentText, maxSize: 500, overlap: 50);
    /// foreach (var chunk in chunks)
    /// {
    ///     // Process each chunk
    /// }
    /// </code>
    /// </example>
    public static IEnumerable<string> ChunkText(string text, int maxSize, int overlap)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSize);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);

        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        // Normalize line endings and clean up excessive whitespace
        text = NormalizeText(text);

        if (text.Length <= maxSize)
        {
            yield return text;
            yield break;
        }

        // Split into sentences for more semantic chunking
        var sentences = SplitIntoSentences(text);
        var currentChunk = new StringBuilder();
        var previousChunkEnd = string.Empty;

        foreach (var sentence in sentences)
        {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrEmpty(trimmedSentence))
            {
                continue;
            }

            // If this single sentence exceeds max size, split it by words
            if (trimmedSentence.Length > maxSize)
            {
                // First yield any accumulated content
                if (currentChunk.Length > 0)
                {
                    var chunkText = currentChunk.ToString().Trim();
                    if (!string.IsNullOrEmpty(chunkText))
                    {
                        yield return chunkText;
                        previousChunkEnd = GetOverlapText(chunkText, overlap);
                    }
                    currentChunk.Clear();
                }

                // Split the large sentence by words
                foreach (var wordChunk in ChunkByWords(trimmedSentence, maxSize, overlap))
                {
                    yield return wordChunk;
                    previousChunkEnd = GetOverlapText(wordChunk, overlap);
                }
                continue;
            }

            // Check if adding this sentence would exceed the limit
            var proposedLength = currentChunk.Length + (currentChunk.Length > 0 ? 1 : 0) + trimmedSentence.Length;

            if (proposedLength > maxSize && currentChunk.Length > 0)
            {
                // Yield current chunk
                var chunkText = currentChunk.ToString().Trim();
                if (!string.IsNullOrEmpty(chunkText))
                {
                    yield return chunkText;
                    previousChunkEnd = GetOverlapText(chunkText, overlap);
                }

                // Start new chunk with overlap
                currentChunk.Clear();
                if (!string.IsNullOrEmpty(previousChunkEnd))
                {
                    currentChunk.Append(previousChunkEnd);
                    if (!previousChunkEnd.EndsWith(' '))
                    {
                        currentChunk.Append(' ');
                    }
                }
            }

            // Add sentence to current chunk
            if (currentChunk.Length > 0 && !currentChunk.ToString().EndsWith(' '))
            {
                currentChunk.Append(' ');
            }
            currentChunk.Append(trimmedSentence);
        }

        // Yield any remaining content
        if (currentChunk.Length > 0)
        {
            var finalChunk = currentChunk.ToString().Trim();
            if (!string.IsNullOrEmpty(finalChunk))
            {
                yield return finalChunk;
            }
        }
    }

    /// <summary>
    /// Normalizes text by standardizing line endings and reducing excessive whitespace.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    /// <returns>Normalized text with consistent whitespace.</returns>
    private static string NormalizeText(string text)
    {
        // Replace various line endings with standard newline
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // Replace multiple consecutive spaces with single space
        while (text.Contains("  "))
        {
            text = text.Replace("  ", " ");
        }

        // Replace more than two consecutive newlines with two
        while (text.Contains("\n\n\n"))
        {
            text = text.Replace("\n\n\n", "\n\n");
        }

        return text.Trim();
    }

    /// <summary>
    /// Splits text into sentences based on sentence-ending punctuation.
    /// </summary>
    /// <param name="text">The text to split into sentences.</param>
    /// <returns>List of sentences.</returns>
    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var currentSentence = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            currentSentence.Append(c);

            // Check for sentence ending
            if (SentenceEndings.Contains(c))
            {
                // Look ahead for quotes or closing punctuation
                while (i + 1 < text.Length && (text[i + 1] == '"' || text[i + 1] == '\'' || text[i + 1] == ')'))
                {
                    i++;
                    currentSentence.Append(text[i]);
                }

                // Check if this is really end of sentence (followed by space/newline/end)
                if (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]))
                {
                    var sentence = currentSentence.ToString().Trim();
                    if (!string.IsNullOrEmpty(sentence))
                    {
                        sentences.Add(sentence);
                    }
                    currentSentence.Clear();
                }
            }
            // Also split on paragraph breaks
            else if (c == '\n' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                var sentence = currentSentence.ToString().Trim();
                if (!string.IsNullOrEmpty(sentence))
                {
                    sentences.Add(sentence);
                }
                currentSentence.Clear();
                // Skip the second newline
                i++;
            }
        }

        // Add any remaining text as final sentence
        if (currentSentence.Length > 0)
        {
            var sentence = currentSentence.ToString().Trim();
            if (!string.IsNullOrEmpty(sentence))
            {
                sentences.Add(sentence);
            }
        }

        return sentences;
    }

    /// <summary>
    /// Chunks text by word boundaries when it exceeds maximum size.
    /// </summary>
    /// <param name="text">The text to chunk by words.</param>
    /// <param name="maxSize">Maximum chunk size.</param>
    /// <param name="overlap">Overlap size between chunks.</param>
    /// <returns>Enumerable of word-boundary chunks.</returns>
    private static IEnumerable<string> ChunkByWords(string text, int maxSize, int overlap)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new StringBuilder();

        foreach (var word in words)
        {
            // If single word exceeds max, just yield it as is
            if (word.Length > maxSize)
            {
                if (currentChunk.Length > 0)
                {
                    yield return currentChunk.ToString().Trim();
                    currentChunk.Clear();
                }
                yield return word;
                continue;
            }

            var proposedLength = currentChunk.Length + (currentChunk.Length > 0 ? 1 : 0) + word.Length;

            if (proposedLength > maxSize && currentChunk.Length > 0)
            {
                var chunkText = currentChunk.ToString().Trim();
                yield return chunkText;

                // Start new chunk with overlap
                currentChunk.Clear();
                var overlapText = GetOverlapText(chunkText, overlap);
                if (!string.IsNullOrEmpty(overlapText))
                {
                    currentChunk.Append(overlapText);
                    currentChunk.Append(' ');
                }
            }

            if (currentChunk.Length > 0)
            {
                currentChunk.Append(' ');
            }
            currentChunk.Append(word);
        }

        if (currentChunk.Length > 0)
        {
            yield return currentChunk.ToString().Trim();
        }
    }

    /// <summary>
    /// Extracts the overlap text from the end of a chunk.
    /// </summary>
    /// <param name="text">The text to extract overlap from.</param>
    /// <param name="overlap">The desired overlap size.</param>
    /// <returns>The overlap text, trimmed to word boundaries.</returns>
    private static string GetOverlapText(string text, int overlap)
    {
        if (overlap <= 0 || string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.Length <= overlap)
        {
            return text;
        }

        // Start from overlap characters from end, find word boundary
        var startIndex = text.Length - overlap;

        // Find the next word boundary (space) after startIndex
        while (startIndex < text.Length && text[startIndex] != ' ')
        {
            startIndex++;
        }

        // Skip the space
        if (startIndex < text.Length)
        {
            startIndex++;
        }

        if (startIndex >= text.Length)
        {
            return string.Empty;
        }

        return text[startIndex..].Trim();
    }
}
