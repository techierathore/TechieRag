using System.Text;
using TechieRag.Abstractions;

namespace TechieRag.Processors.Chunking;

/// <summary>
/// Chunking strategy that never splits inside a sentence: whole sentences are accumulated
/// until the chunk size is reached, with sentence-level overlap.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Produces maximally readable chunks for prose-heavy content where
/// mid-sentence splits harm retrieval quality.</para>
/// <para><b>Edge Case:</b> A single sentence longer than the chunk size is emitted as its
/// own (oversized) chunk rather than being split.</para>
/// </remarks>
public class SentenceChunker : IChunker
{
    private static readonly char[] SentenceEndings = ['.', '!', '?'];

    /// <inheritdoc/>
    public string Name => "Sentence";

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

        var sentences = SplitIntoSentences(text);
        var current = new List<string>();
        var currentLength = 0;

        foreach (var sentence in sentences)
        {
            var addedLength = sentence.Length + (current.Count > 0 ? 1 : 0);

            if (currentLength + addedLength > maxChunkSize && current.Count > 0)
            {
                yield return string.Join(" ", current);

                // Sentence-level overlap: carry trailing sentences up to chunkOverlap chars
                var overlapSentences = TakeOverlapSentences(current, chunkOverlap);
                current = overlapSentences;
                currentLength = current.Sum(s => s.Length) + Math.Max(0, current.Count - 1);
            }

            current.Add(sentence);
            currentLength += sentence.Length + (current.Count > 1 ? 1 : 0);
        }

        if (current.Count > 0)
        {
            yield return string.Join(" ", current);
        }
    }

    private static List<string> TakeOverlapSentences(List<string> sentences, int chunkOverlap)
    {
        if (chunkOverlap <= 0) return [];

        var overlap = new List<string>();
        var length = 0;

        for (var i = sentences.Count - 1; i >= 0; i--)
        {
            var candidateLength = sentences[i].Length + (overlap.Count > 0 ? 1 : 0);
            if (length + candidateLength > chunkOverlap) break;
            overlap.Insert(0, sentences[i]);
            length += candidateLength;
        }

        return overlap;
    }

    private static List<string> SplitIntoSentences(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var sentences = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            current.Append(c);

            var isSentenceEnd = SentenceEndings.Contains(c) &&
                (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]));
            var isParagraphBreak = c == '\n' && i + 1 < text.Length && text[i + 1] == '\n';

            if (isSentenceEnd || isParagraphBreak)
            {
                var sentence = current.ToString().Trim();
                if (sentence.Length > 0) sentences.Add(sentence);
                current.Clear();
                if (isParagraphBreak) i++;
            }
        }

        var remainder = current.ToString().Trim();
        if (remainder.Length > 0) sentences.Add(remainder);

        return sentences;
    }
}
