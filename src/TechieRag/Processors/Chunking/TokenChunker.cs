using System.Text;
using TechieRag.Abstractions;

namespace TechieRag.Processors.Chunking;

/// <summary>
/// Chunking strategy that packs chunks by estimated token count rather than characters,
/// splitting on word boundaries.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps every chunk within a predictable token budget for embedding
/// models with fixed sequence limits.</para>
/// <para><b>Estimation:</b> Uses the standard ~4 characters-per-token heuristic (the same
/// estimator used by the LLM providers). The character-based <c>maxChunkSize</c> and
/// <c>chunkOverlap</c> arguments are converted to token budgets by dividing by four.</para>
/// </remarks>
public class TokenChunker : IChunker
{
    private const double CharsPerToken = 4.0;

    /// <inheritdoc/>
    public string Name => "Token";

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

        var maxTokens = Math.Max(1, (int)(maxChunkSize / CharsPerToken));
        var overlapTokens = (int)(chunkOverlap / CharsPerToken);

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var current = new List<string>();
        var currentTokens = 0;

        foreach (var word in words)
        {
            var wordTokens = EstimateTokens(word);

            if (currentTokens + wordTokens > maxTokens && current.Count > 0)
            {
                yield return string.Join(" ", current);

                var overlap = TakeOverlapWords(current, overlapTokens);
                current = overlap;
                currentTokens = current.Sum(EstimateTokens);
            }

            current.Add(word);
            currentTokens += wordTokens;
        }

        if (current.Count > 0)
        {
            yield return string.Join(" ", current);
        }
    }

    private static int EstimateTokens(string word) =>
        Math.Max(1, (int)Math.Ceiling((word.Length + 1) / CharsPerToken));

    private static List<string> TakeOverlapWords(List<string> words, int overlapTokens)
    {
        if (overlapTokens <= 0) return [];

        var overlap = new List<string>();
        var tokens = 0;

        for (var i = words.Count - 1; i >= 0; i--)
        {
            var wordTokens = EstimateTokens(words[i]);
            if (tokens + wordTokens > overlapTokens) break;
            overlap.Insert(0, words[i]);
            tokens += wordTokens;
        }

        return overlap;
    }
}
