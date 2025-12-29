using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.VectorStores;

/// <summary>
/// SQLite-vec vector store implementation for embedded database scenarios.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a zero-configuration embedded vector database using SQLite
/// with optional sqlite-vec extension support for vector similarity search. This is the default
/// vector store for TechieRag, suitable for development and single-machine deployments.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when VectorStoreType.SqliteVec is configured.
/// InitializeAsync creates tables on first use. UpsertAsync/UpsertBatchAsync store chunks with vectors
/// as BLOB data. SearchAsync performs similarity search when sqlite-vec is available.</para>
/// <para><b>Dependencies:</b>
/// - Microsoft.Data.Sqlite for database connectivity
/// - Dapper for simplified data access
/// - sqlite-vec extension (optional) for vector similarity search</para>
/// <para><b>Fallback Mode:</b> When sqlite-vec extension is not available, vectors are stored
/// as BLOB data in the Chunks table. SearchAsync returns empty results in fallback mode.</para>
/// </remarks>
public class SqliteVecStore : IVectorStore
{
    /// <inheritdoc/>
    public string Name => "SQLite-vec";

    private readonly string connectionString;
    private readonly int dimensions;
    private bool initialized;
    private bool sqliteVecAvailable;

    /// <summary>
    /// Creates a new SQLite-vec vector store instance.
    /// </summary>
    /// <param name="connectionString">SQLite connection string (e.g., "Data Source=techierag.db").</param>
    /// <param name="dimensions">Vector dimensions (default: 1024 for BGE-M3 model).</param>
    /// <remarks>
    /// <para><b>Connection String:</b> Uses standard Microsoft.Data.Sqlite connection string format.
    /// The database file will be created automatically if it doesn't exist.</para>
    /// <para><b>Dimensions:</b> Must match the embedding model's output dimensions.
    /// BGE-M3 produces 1024-dimensional vectors by default.</para>
    /// </remarks>
    public SqliteVecStore(string connectionString, int dimensions = 1024)
    {
        this.connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        this.dimensions = dimensions;
    }

    /// <summary>
    /// Initializes the database schema, creating tables if they don't exist.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    /// <remarks>
    /// <para><b>Tables Created:</b></para>
    /// <list type="bullet">
    /// <item><description>Documents - stores document metadata (Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata)</description></item>
    /// <item><description>Chunks - stores text chunks with vectors as BLOB (Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt)</description></item>
    /// </list>
    /// <para><b>Note:</b> The ChunksVec virtual table for sqlite-vec is not created automatically
    /// as the extension may not be available at build time. Vector search requires the sqlite-vec
    /// extension to be loaded at runtime.</para>
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Try to load sqlite-vec extension (may not be available)
        sqliteVecAvailable = TryLoadSqliteVecExtension(connection);

        // Create Documents table
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Documents (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                SourcePath TEXT NOT NULL,
                ChunkCount INTEGER DEFAULT 0,
                IngestedAt TEXT NOT NULL,
                Metadata TEXT
            )");

        // Create Chunks table with Vector stored as BLOB
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Chunks (
                Id TEXT PRIMARY KEY,
                DocumentId TEXT NOT NULL,
                Text TEXT NOT NULL,
                Vector BLOB,
                PageNumber INTEGER,
                ChunkIndex INTEGER,
                Metadata TEXT,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (DocumentId) REFERENCES Documents(Id) ON DELETE CASCADE
            )");

        // Create index for efficient document-based queries
        await connection.ExecuteAsync(@"
            CREATE INDEX IF NOT EXISTS IdxChunksDocument ON Chunks(DocumentId)");

        // Enable foreign key constraints
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON");

        initialized = true;
    }

    /// <summary>
    /// Attempts to load the sqlite-vec extension for vector operations.
    /// </summary>
    /// <param name="connection">The open SQLite connection.</param>
    /// <returns>True if the extension was loaded successfully; otherwise, false.</returns>
    private static bool TryLoadSqliteVecExtension(SqliteConnection connection)
    {
        try
        {
            // sqlite-vec extension loading would happen here
            // For now, we operate in fallback mode without the extension
            // connection.LoadExtension("vec0");
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Inserts or updates a single text chunk with its vector embedding.
    /// </summary>
    /// <param name="chunk">The chunk containing text, vector, and metadata.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The ID of the upserted chunk.</returns>
    /// <remarks>
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item><description>Ensures database is initialized</description></item>
    /// <item><description>Serializes vector to byte array and metadata to JSON</description></item>
    /// <item><description>Uses INSERT OR REPLACE to handle both insert and update</description></item>
    /// <item><description>Updates the parent document's chunk count</description></item>
    /// </list>
    /// </remarks>
    public async Task<string> UpsertAsync(TextChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // First, ensure the document exists (to satisfy foreign key constraint)
        var docName = chunk.Metadata.TryGetValue("DocumentName", out var name) ? name?.ToString() ?? "Unknown" : "Unknown";
        var sourcePath = chunk.Metadata.TryGetValue("SourcePath", out var path) ? path?.ToString() ?? "Unknown" : "Unknown";

        await connection.ExecuteAsync(@"
            INSERT OR IGNORE INTO Documents (Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata)
            VALUES (@Id, @Name, @SourcePath, 0, @IngestedAt, @Metadata)",
            new
            {
                Id = chunk.DocumentId,
                Name = docName,
                SourcePath = sourcePath,
                IngestedAt = DateTime.UtcNow.ToString("o"),
                Metadata = "{}"
            });

        var vectorBytes = chunk.Vector != null ? SerializeVector(chunk.Vector) : null;
        var metadataJson = JsonSerializer.Serialize(chunk.Metadata);

        await connection.ExecuteAsync(@"
            INSERT OR REPLACE INTO Chunks (Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt)
            VALUES (@Id, @DocumentId, @Text, @Vector, @PageNumber, @ChunkIndex, @Metadata, @CreatedAt)",
            new
            {
                chunk.Id,
                chunk.DocumentId,
                chunk.Text,
                Vector = vectorBytes,
                chunk.PageNumber,
                chunk.ChunkIndex,
                Metadata = metadataJson,
                CreatedAt = chunk.CreatedAt.ToString("o")
            });

        // Update document chunk count
        await UpdateDocumentChunkCountAsync(connection, chunk.DocumentId);

        return chunk.Id;
    }

    /// <summary>
    /// Inserts or updates multiple chunks in a batch operation for efficiency.
    /// </summary>
    /// <param name="chunks">Collection of chunks to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of IDs for all upserted chunks.</returns>
    /// <remarks>
    /// <para><b>Performance:</b> Uses a transaction to batch all operations,
    /// significantly improving performance for large ingestion jobs.</para>
    /// </remarks>
    public async Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        await InitializeAsync(cancellationToken);

        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
            return Array.Empty<string>();

        var ids = new List<string>();
        var documentIds = new HashSet<string>();

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();

        try
        {
            // First, ensure all referenced documents exist (to satisfy foreign key constraint)
            foreach (var chunk in chunkList)
            {
                documentIds.Add(chunk.DocumentId);
            }

            foreach (var docId in documentIds)
            {
                var firstChunk = chunkList.First(c => c.DocumentId == docId);
                var docName = firstChunk.Metadata.TryGetValue("DocumentName", out var name) ? name?.ToString() ?? "Unknown" : "Unknown";
                var sourcePath = firstChunk.Metadata.TryGetValue("SourcePath", out var path) ? path?.ToString() ?? "Unknown" : "Unknown";

                await connection.ExecuteAsync(@"
                    INSERT OR IGNORE INTO Documents (Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata)
                    VALUES (@Id, @Name, @SourcePath, 0, @IngestedAt, @Metadata)",
                    new
                    {
                        Id = docId,
                        Name = docName,
                        SourcePath = sourcePath,
                        IngestedAt = DateTime.UtcNow.ToString("o"),
                        Metadata = "{}"
                    },
                    transaction);
            }

            // Now insert the chunks
            foreach (var chunk in chunkList)
            {
                var vectorBytes = chunk.Vector != null ? SerializeVector(chunk.Vector) : null;
                var metadataJson = JsonSerializer.Serialize(chunk.Metadata);

                await connection.ExecuteAsync(@"
                    INSERT OR REPLACE INTO Chunks (Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt)
                    VALUES (@Id, @DocumentId, @Text, @Vector, @PageNumber, @ChunkIndex, @Metadata, @CreatedAt)",
                    new
                    {
                        chunk.Id,
                        chunk.DocumentId,
                        chunk.Text,
                        Vector = vectorBytes,
                        chunk.PageNumber,
                        chunk.ChunkIndex,
                        Metadata = metadataJson,
                        CreatedAt = chunk.CreatedAt.ToString("o")
                    },
                    transaction);

                ids.Add(chunk.Id);
            }

            // Update chunk counts for all affected documents
            foreach (var documentId in documentIds)
            {
                await UpdateDocumentChunkCountAsync(connection, documentId, transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return ids;
    }

    /// <summary>
    /// Performs vector similarity search to find chunks most similar to the query vector.
    /// </summary>
    /// <param name="queryVector">The embedding vector of the search query.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="documentFilter">Optional document ID to restrict search scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked search results ordered by similarity score.</returns>
    /// <remarks>
    /// <para><b>Note:</b> This implementation requires the sqlite-vec extension for actual
    /// vector similarity search. When the extension is not available, this method returns
    /// an empty result set. Future versions will implement fallback cosine similarity
    /// calculation in managed code.</para>
    /// <para><b>Algorithm (when sqlite-vec available):</b> Uses sqlite-vec's MATCH operator
    /// for approximate nearest neighbor search. Results are sorted by distance and
    /// converted to similarity scores (score = 1 - distance).</para>
    /// </remarks>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        string? documentFilter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        await InitializeAsync(cancellationToken);

        // When sqlite-vec is not available, return empty results
        // A full implementation would require loading the sqlite-vec extension
        // or implementing cosine similarity in managed code
        if (!sqliteVecAvailable)
        {
            // Fallback: compute cosine similarity in managed code
            return await ComputeSimilarityFallbackAsync(queryVector, topK, documentFilter, cancellationToken);
        }

        // sqlite-vec search would be implemented here when extension is available
        return Array.Empty<SearchResult>();
    }

    /// <summary>
    /// Computes cosine similarity in managed code as a fallback when sqlite-vec is not available.
    /// </summary>
    /// <param name="queryVector">The query embedding vector.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="documentFilter">Optional document ID filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results ranked by similarity score.</returns>
    private async Task<IReadOnlyList<SearchResult>> ComputeSimilarityFallbackAsync(
        float[] queryVector,
        int topK,
        string? documentFilter,
        CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = documentFilter is null
            ? "SELECT Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt FROM Chunks WHERE Vector IS NOT NULL"
            : "SELECT Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt FROM Chunks WHERE Vector IS NOT NULL AND DocumentId = @DocumentFilter";

        var rows = await connection.QueryAsync<ChunkRow>(sql, new { DocumentFilter = documentFilter });

        var results = new List<(TextChunk Chunk, float Score)>();

        foreach (var row in rows)
        {
            if (row.Vector == null || row.Vector.Length == 0)
                continue;

            var storedVector = DeserializeVector(row.Vector);
            var similarity = ComputeCosineSimilarity(queryVector, storedVector);

            results.Add((row.ToTextChunk(), similarity));
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .Select(r => new SearchResult { Chunk = r.Chunk, Score = r.Score })
            .ToList();
    }

    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// </summary>
    /// <param name="vectorA">First vector.</param>
    /// <param name="vectorB">Second vector.</param>
    /// <returns>Cosine similarity score between 0 and 1.</returns>
    private static float ComputeCosineSimilarity(float[] vectorA, float[] vectorB)
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

    /// <summary>
    /// Deletes a specific chunk by its ID.
    /// </summary>
    /// <param name="chunkId">The ID of the chunk to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    /// <remarks>
    /// <para><b>Note:</b> This does not automatically update the parent document's chunk count.
    /// For complete document removal, use <see cref="DeleteByDocumentAsync"/> instead.</para>
    /// </remarks>
    public async Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(chunkId);
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Get the document ID before deletion to update chunk count
        var documentId = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT DocumentId FROM Chunks WHERE Id = @ChunkId",
            new { ChunkId = chunkId });

        await connection.ExecuteAsync("DELETE FROM Chunks WHERE Id = @ChunkId", new { ChunkId = chunkId });

        // Update chunk count if document was found
        if (!string.IsNullOrEmpty(documentId))
        {
            await UpdateDocumentChunkCountAsync(connection, documentId);
        }
    }

    /// <summary>
    /// Deletes all chunks belonging to a specific document.
    /// </summary>
    /// <param name="documentId">The document ID whose chunks should be deleted.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    /// <remarks>
    /// <para><b>Cascade:</b> Due to foreign key constraints, deleting a document
    /// will automatically delete all associated chunks.</para>
    /// </remarks>
    public async Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable foreign keys to ensure cascade delete
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON");

        // Delete document (cascades to chunks)
        await connection.ExecuteAsync("DELETE FROM Documents WHERE Id = @DocumentId", new { DocumentId = documentId });

        // Also explicitly delete chunks in case foreign keys didn't cascade
        await connection.ExecuteAsync("DELETE FROM Chunks WHERE DocumentId = @DocumentId", new { DocumentId = documentId });
    }

    /// <summary>
    /// Lists all documents in the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of documents with their metadata.</returns>
    /// <remarks>
    /// <para><b>Order:</b> Documents are returned ordered by ingestion time, most recent first.</para>
    /// </remarks>
    public async Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<DocumentRow>(
            "SELECT Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata FROM Documents ORDER BY IngestedAt DESC");

        return rows.Select(r => r.ToDocument()).ToList();
    }

    /// <summary>
    /// Retrieves statistics about the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Statistics including counts and storage size.</returns>
    /// <remarks>
    /// <para><b>Storage Size:</b> Calculated using SQLite's page_count and page_size pragmas,
    /// which provides the actual file size of the database.</para>
    /// </remarks>
    public async Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var documentCount = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM Documents");
        var chunkCount = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM Chunks");

        // Get database file size using SQLite pragmas
        var pageCount = await connection.QuerySingleAsync<long>("SELECT page_count FROM pragma_page_count()");
        var pageSize = await connection.QuerySingleAsync<long>("SELECT page_size FROM pragma_page_size()");
        var sizeBytes = pageCount * pageSize;

        // Get last ingestion time
        var lastIngestion = await connection.QueryFirstOrDefaultAsync<string>(
            "SELECT MAX(IngestedAt) FROM Documents");
        DateTime? lastIngestionTime = null;
        if (!string.IsNullOrEmpty(lastIngestion))
        {
            lastIngestionTime = DateTime.Parse(lastIngestion);
        }

        return new IngestionStats
        {
            TotalDocuments = documentCount,
            TotalChunks = chunkCount,
            VectorStoreSizeBytes = sizeBytes,
            LastIngestionTime = lastIngestionTime,
            VectorStoreName = Name,
            EmbeddingProviderName = string.Empty // Set by caller
        };
    }

    /// <summary>
    /// Clears all data from the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous clear operation.</returns>
    /// <remarks>
    /// <para><b>Warning:</b> This operation is irreversible and deletes all documents and chunks.</para>
    /// <para><b>Optimization:</b> After clearing, VACUUM is run to reclaim disk space.</para>
    /// </remarks>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync("DELETE FROM Chunks");
        await connection.ExecuteAsync("DELETE FROM Documents");

        // Reclaim disk space
        await connection.ExecuteAsync("VACUUM");
    }

    /// <summary>
    /// Updates the chunk count for a document based on actual chunks in the database.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="documentId">The document ID to update.</param>
    /// <param name="transaction">Optional transaction to participate in.</param>
    private static async Task UpdateDocumentChunkCountAsync(
        SqliteConnection connection,
        string documentId,
        SqliteTransaction? transaction = null)
    {
        var count = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM Chunks WHERE DocumentId = @DocumentId",
            new { DocumentId = documentId },
            transaction);

        await connection.ExecuteAsync(
            "UPDATE Documents SET ChunkCount = @Count WHERE Id = @DocumentId",
            new { Count = count, DocumentId = documentId },
            transaction);
    }

    /// <summary>
    /// Serializes a float array to bytes for SQLite BLOB storage.
    /// </summary>
    /// <param name="vector">The vector to serialize.</param>
    /// <returns>Byte array representation of the vector.</returns>
    /// <remarks>
    /// <para><b>Format:</b> Uses Buffer.BlockCopy for efficient binary conversion.
    /// Each float (4 bytes) is copied directly to the byte array in native format.</para>
    /// </remarks>
    private static byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Deserializes bytes from SQLite BLOB storage back to a float array.
    /// </summary>
    /// <param name="bytes">The byte array to deserialize.</param>
    /// <returns>The deserialized float array.</returns>
    /// <remarks>
    /// <para><b>Format:</b> Inverse of <see cref="SerializeVector"/>.
    /// Expects bytes in native float format as produced by Buffer.BlockCopy.</para>
    /// </remarks>
    private static float[] DeserializeVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    /// <summary>
    /// Internal row class for mapping Chunk database rows.
    /// </summary>
    private class ChunkRow
    {
        public string Id { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public byte[]? Vector { get; set; }
        public int? PageNumber { get; set; }
        public int? ChunkIndex { get; set; }
        public string? Metadata { get; set; }
        public string CreatedAt { get; set; } = string.Empty;

        /// <summary>
        /// Converts this row to a TextChunk model.
        /// </summary>
        public TextChunk ToTextChunk()
        {
            var metadata = string.IsNullOrEmpty(Metadata)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(Metadata) ?? new Dictionary<string, object>();

            return new TextChunk
            {
                Id = Id,
                DocumentId = DocumentId,
                Text = Text,
                Vector = Vector != null ? DeserializeVector(Vector) : null,
                PageNumber = PageNumber,
                ChunkIndex = ChunkIndex,
                Metadata = metadata,
                CreatedAt = DateTime.TryParse(CreatedAt, out var dt) ? dt : DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Internal row class for mapping Document database rows.
    /// </summary>
    private class DocumentRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public int ChunkCount { get; set; }
        public string IngestedAt { get; set; } = string.Empty;
        public string? Metadata { get; set; }

        /// <summary>
        /// Converts this row to a Document model.
        /// </summary>
        public Document ToDocument()
        {
            var metadata = string.IsNullOrEmpty(Metadata)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(Metadata) ?? new Dictionary<string, object>();

            return new Document
            {
                Id = Id,
                Name = Name,
                SourcePath = SourcePath,
                ChunkCount = ChunkCount,
                IngestedAt = DateTime.TryParse(IngestedAt, out var dt) ? dt : DateTime.UtcNow,
                Metadata = metadata
            };
        }
    }
}
