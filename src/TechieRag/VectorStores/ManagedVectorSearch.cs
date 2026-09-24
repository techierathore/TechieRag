using System.Numerics;

namespace TechieRag.VectorStores;

/// <summary>
/// The arithmetic behind <see cref="SqliteVecStore"/>'s managed similarity search (REQ-RAG-056).
/// </summary>
/// <remarks>
/// Kept apart from the store so it can be tested without a database, and so the ranking rule
/// (score descending, ties in scan order) is written down once.
/// </remarks>
internal static class ManagedVectorSearch
{
    /// <summary>One scored row.</summary>
    /// <param name="RowId">The SQLite <c>rowid</c> of the chunk.</param>
    /// <param name="Score">The cosine similarity.</param>
    /// <param name="Sequence">The row's position in the scan, used to break ties.</param>
    internal readonly record struct Candidate(long RowId, float Score, long Sequence);

    /// <summary>
    /// Computes the Euclidean length of a vector, in double precision.
    /// </summary>
    /// <param name="vector">The vector.</param>
    /// <returns>The square root of the sum of squares.</returns>
    internal static double Magnitude(ReadOnlySpan<float> vector)
    {
        var (_, sumOfSquares) = DotAndSquares(vector, vector);
        return Math.Sqrt(sumOfSquares);
    }

    /// <summary>
    /// Computes the cosine similarity of a query and a stored vector.
    /// </summary>
    /// <param name="query">The query vector.</param>
    /// <param name="queryMagnitude">The query's length from <see cref="Magnitude"/>, computed once per search.</param>
    /// <param name="stored">The stored vector.</param>
    /// <returns>The similarity; 0 when the widths differ or either vector is zero.</returns>
    internal static float CosineSimilarity(ReadOnlySpan<float> query, double queryMagnitude, ReadOnlySpan<float> stored)
    {
        if (query.Length != stored.Length)
        {
            return 0f;
        }

        var (dot, storedSquares) = DotAndSquares(query, stored);
        var magnitude = (float)(queryMagnitude * Math.Sqrt(storedSquares));
        return magnitude == 0f ? 0f : dot / magnitude;
    }

    /// <summary>
    /// Computes <c>a·b</c> and <c>b·b</c> in one SIMD pass.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector, same length as <paramref name="a"/>.</param>
    /// <returns>The dot product and the sum of squares of <paramref name="b"/>.</returns>
    private static (float Dot, float SquaresOfB) DotAndSquares(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var width = Vector<float>.Count;
        var dotAccumulator = Vector<float>.Zero;
        var squaresAccumulator = Vector<float>.Zero;
        var i = 0;

        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= a.Length - width; i += width)
            {
                var left = new Vector<float>(a.Slice(i, width));
                var right = new Vector<float>(b.Slice(i, width));
                dotAccumulator += left * right;
                squaresAccumulator += right * right;
            }
        }

        var dot = Vector.Dot(dotAccumulator, Vector<float>.One);
        var squares = Vector.Dot(squaresAccumulator, Vector<float>.One);

        for (; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            squares += b[i] * b[i];
        }

        return (dot, squares);
    }

    /// <summary>
    /// Keeps the best <c>k</c> candidates seen so far in a bounded heap.
    /// </summary>
    /// <remarks>
    /// Order is score descending; equal scores keep scan order (the earlier row first), which is
    /// what the old full <c>OrderByDescending</c> — a stable sort over the scan — produced.
    /// </remarks>
    internal sealed class TopKCollector
    {
        private readonly int capacity;
        private readonly PriorityQueue<Candidate, Candidate> heap;

        /// <summary>Creates a collector that keeps <paramref name="capacity"/> candidates.</summary>
        /// <param name="capacity">How many to keep; at least 1.</param>
        internal TopKCollector(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
            this.capacity = capacity;
            heap = new PriorityQueue<Candidate, Candidate>(Math.Min(capacity, 4096) + 1, WorstFirst.Instance);
        }

        /// <summary>Offers one scored row.</summary>
        /// <param name="rowId">The row id.</param>
        /// <param name="score">Its score.</param>
        /// <param name="sequence">Its scan position; increases with every call.</param>
        internal void Add(long rowId, float score, long sequence)
        {
            var candidate = new Candidate(rowId, score, sequence);
            if (heap.Count < capacity)
            {
                heap.Enqueue(candidate, candidate);
                return;
            }

            // A later row replaces the current worst only when it is strictly better.
            if (WorstFirst.Instance.Compare(candidate, heap.Peek()) > 0)
            {
                heap.DequeueEnqueue(candidate, candidate);
            }
        }

        /// <summary>Returns the kept candidates, best first.</summary>
        /// <returns>The ranked list.</returns>
        internal IReadOnlyList<Candidate> ToRankedList()
        {
            var ranked = new List<Candidate>(heap.Count);
            while (heap.Count > 0)
            {
                ranked.Add(heap.Dequeue());
            }

            ranked.Reverse();
            return ranked;
        }
    }

    /// <summary>
    /// Orders candidates worst first: lower score first, and on equal score the later row first.
    /// </summary>
    private sealed class WorstFirst : IComparer<Candidate>
    {
        internal static readonly WorstFirst Instance = new();

        public int Compare(Candidate x, Candidate y)
        {
            var byScore = x.Score.CompareTo(y.Score);
            return byScore != 0 ? byScore : y.Sequence.CompareTo(x.Sequence);
        }
    }
}
