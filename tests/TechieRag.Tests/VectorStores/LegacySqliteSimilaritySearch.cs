using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// A verbatim copy of <c>SqliteVecStore</c>'s search before REQ-RAG-056 (the "old path"), kept only
/// as the reference the new search must rank identically to.
/// </summary>
/// <remarks>
/// Every row is read with its text and metadata, the metadata JSON-parsed, the vector copied into a
/// new array, scored with a scalar loop, and the whole list sorted. Nothing in the library uses this.
/// </remarks>
internal static class LegacySqliteSimilaritySearch
{
    /// <summary>Runs the old search and returns the chunk ids with their scores, best first.</summary>
    /// <param name="connectionString">The store's connection string.</param>
    /// <param name="queryVector">The query.</param>
    /// <param name="topK">How many to return.</param>
    /// <param name="documentFilter">Optional document id.</param>
    /// <returns>(chunk id, score) pairs.</returns>
    internal static async Task<IReadOnlyList<(string Id, float Score)>> SearchAsync(
        string connectionString,
        float[] queryVector,
        int topK,
        string? documentFilter = null)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = documentFilter is null
            ? "SELECT Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt FROM Chunks WHERE Vector IS NOT NULL"
            : "SELECT Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt FROM Chunks WHERE Vector IS NOT NULL AND DocumentId = @DocumentFilter";

        var rows = await connection.QueryAsync<Row>(sql, new { DocumentFilter = documentFilter });
        var results = new List<(string Id, float Score, Dictionary<string, object>? Metadata)>();

        foreach (var row in rows)
        {
            if (row.Vector == null || row.Vector.Length == 0)
                continue;

            var stored = new float[row.Vector.Length / sizeof(float)];
            Buffer.BlockCopy(row.Vector, 0, stored, 0, row.Vector.Length);
            var metadata = string.IsNullOrEmpty(row.Metadata)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(row.Metadata);
            results.Add((row.Id, Cosine(queryVector, stored), metadata));
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .Select(r => (r.Id, r.Score))
            .ToList();
    }

    private static float Cosine(float[] vectorA, float[] vectorB)
    {
        if (vectorA.Length != vectorB.Length)
            return 0f;

        float dotProduct = 0f;
        float magnitudeA = 0f;
        float magnitudeB = 0f;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            magnitudeA += vectorA[i] * vectorA[i];
            magnitudeB += vectorB[i] * vectorB[i];
        }

        var magnitude = (float)(Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
        if (magnitude == 0f)
            return 0f;

        return dotProduct / magnitude;
    }

    private sealed class Row
    {
        public string Id { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public byte[]? Vector { get; set; }
        public int? PageNumber { get; set; }
        public int? ChunkIndex { get; set; }
        public string? Metadata { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
    }
}
