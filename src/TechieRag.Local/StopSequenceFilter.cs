using System.Text;

namespace TechieRag.Local;

/// <summary>
/// Cuts generated text at the first stop sequence, holding back only the tail that could still turn
/// into one, so streamed text never shows part of a stop sequence (REQ-RAG-059 / BRD-98).
/// </summary>
internal sealed class StopSequenceFilter
{
    private readonly IReadOnlyList<string> stopSequences;
    private readonly int longest;
    private readonly StringBuilder held = new();

    /// <summary>Creates a filter.</summary>
    /// <param name="stopSequences">The sequences that end the answer; empty ones are ignored.</param>
    public StopSequenceFilter(IEnumerable<string> stopSequences)
    {
        this.stopSequences = stopSequences.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        longest = this.stopSequences.Count == 0 ? 0 : this.stopSequences.Max(s => s.Length);
    }

    /// <summary>Gets whether a stop sequence has been seen; nothing after it is emitted.</summary>
    public bool Stopped { get; private set; }

    /// <summary>
    /// Adds a generated piece and returns the text that is now safe to emit.
    /// </summary>
    /// <param name="piece">The decoded piece.</param>
    /// <returns>Text to emit, possibly empty.</returns>
    public string Push(string piece)
    {
        if (Stopped || string.IsNullOrEmpty(piece))
        {
            return string.Empty;
        }

        held.Append(piece);
        var text = held.ToString();
        var cut = FirstStopIndex(text);
        if (cut >= 0)
        {
            Stopped = true;
            held.Clear();
            return text[..cut];
        }

        var keep = Math.Min(text.Length, Math.Max(0, longest - 1));
        while (keep > 0 && !IsPrefixOfStop(text[^keep..]))
        {
            keep--;
        }

        held.Clear().Append(text, text.Length - keep, keep);
        return text[..^keep];
    }

    /// <summary>Returns whatever is held back once generation has ended.</summary>
    /// <returns>The remaining text; empty after a stop.</returns>
    public string Flush()
    {
        var rest = Stopped ? string.Empty : held.ToString();
        held.Clear();
        return rest;
    }

    private int FirstStopIndex(string text)
    {
        var first = -1;
        foreach (var stop in stopSequences)
        {
            var index = text.IndexOf(stop, StringComparison.Ordinal);
            if (index >= 0 && (first < 0 || index < first))
            {
                first = index;
            }
        }

        return first;
    }

    private bool IsPrefixOfStop(string tail) =>
        stopSequences.Any(s => s.StartsWith(tail, StringComparison.Ordinal));
}
