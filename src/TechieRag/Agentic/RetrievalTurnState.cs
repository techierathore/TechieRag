using TechieRag.Models;

namespace TechieRag.Agentic;

/// <summary>
/// The knowledge-base tools' memory for one conversation: the search budget of the current turn, the
/// citation refs handed out so far, and the typed results the current turn retrieved (REQ-RAG-015 / BRD-83).
/// </summary>
/// <remarks>
/// <para><b>Refs outlive a turn; the budget and the collected results do not.</b> A ref (S1, S2, ...)
/// is assigned once per chunk and reused whenever that chunk is returned again, in this turn or a
/// later one, so a follow-up answer can cite a passage by the ref the model already saw. Call
/// <see cref="BeginTurn"/> at the start of each user turn to reset the budget and
/// <see cref="Collected"/>.</para>
/// <para><see cref="Collected"/> keeps the full <see cref="SearchResult"/>s, so a host renders typed
/// citations from them rather than parsing the tool's JSON back. Thread-safe: a model may issue
/// parallel tool calls.</para>
/// </remarks>
public sealed class RetrievalTurnState
{
    private readonly object gate = new();
    private readonly Dictionary<string, string> refsByChunkId = new(StringComparer.Ordinal);
    private readonly List<SearchResult> collected = new();
    private readonly HashSet<string> collectedChunkIds = new(StringComparer.Ordinal);
    private readonly List<RetrievalTrace> searches = new();
    private int nextRef = 1;
    private int searchesUsed;

    /// <summary>Gets the searches made in the current turn.</summary>
    public int SearchesUsed
    {
        get { lock (gate) { return searchesUsed; } }
    }

    /// <summary>Gets the distinct results retrieved in the current turn, in the order first returned.</summary>
    public IReadOnlyList<SearchResult> Collected
    {
        get { lock (gate) { return collected.ToList(); } }
    }

    /// <summary>Gets a trace of each search made in the current turn.</summary>
    public IReadOnlyList<RetrievalTrace> Searches
    {
        get { lock (gate) { return searches.ToList(); } }
    }

    /// <summary>Starts a new user turn: resets the search budget, <see cref="Collected"/> and
    /// <see cref="Searches"/>, and keeps the refs already handed out.</summary>
    public void BeginTurn()
    {
        lock (gate)
        {
            searchesUsed = 0;
            collected.Clear();
            collectedChunkIds.Clear();
            searches.Clear();
        }
    }

    /// <summary>Gets the ref assigned to a chunk, or null if it was never returned.</summary>
    /// <param name="chunkId">The chunk id.</param>
    /// <returns>The ref, for example <c>S3</c>, or null.</returns>
    public string? GetRef(string chunkId)
    {
        lock (gate)
        {
            return refsByChunkId.TryGetValue(chunkId, out var reference) ? reference : null;
        }
    }

    /// <summary>Takes one search from the budget.</summary>
    /// <param name="maxSearches">The per-turn budget.</param>
    /// <returns>The searches used after this one, or null when the budget was already spent.</returns>
    internal int? TryUseSearch(int maxSearches)
    {
        lock (gate)
        {
            if (searchesUsed >= maxSearches) return null;
            searchesUsed++;
            return searchesUsed;
        }
    }

    /// <summary>Assigns (or reuses) the ref for a result and records it as collected this turn.</summary>
    /// <param name="result">The result.</param>
    /// <returns>Its ref.</returns>
    internal string Collect(SearchResult result)
    {
        lock (gate)
        {
            var chunkId = result.Chunk.Id;
            if (!refsByChunkId.TryGetValue(chunkId, out var reference))
            {
                reference = "S" + nextRef++;
                refsByChunkId[chunkId] = reference;
            }

            if (collectedChunkIds.Add(chunkId))
            {
                collected.Add(result);
            }

            return reference;
        }
    }

    /// <summary>Records one search's trace.</summary>
    /// <param name="trace">The trace.</param>
    internal void Record(RetrievalTrace trace)
    {
        lock (gate)
        {
            searches.Add(trace);
        }
    }
}
