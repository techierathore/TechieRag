using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Persistence;

namespace TechieRag.VectorStores;

/// <summary>
/// Embedded SQLite vector store: zero configuration, one file, the same code on Windows, macOS,
/// Mac Catalyst, Android and iOS.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The default vector store for TechieRag, suitable for development,
/// desktop and phone apps and single-machine deployments.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when VectorStoreType.SqliteVec is configured.
/// InitializeAsync creates tables on first use. UpsertAsync/UpsertBatchAsync store chunks with vectors
/// as raw float32 BLOBs. SearchAsync is an exact, vectorised cosine scan in managed code.</para>
/// <para><b>Search is managed code, on every platform, by design (REQ-RAG-056 / BRD-93).</b> The
/// sqlite-vec extension is NOT used: it is a native library that would have to be shipped and
/// loaded per platform, and phones and the Mac Catalyst sandbox forbid loading arbitrary native
/// extensions. The managed path is exact (not approximate), needs nothing beyond
/// Microsoft.Data.Sqlite, and was measured at 1,000, 10,000 and 50,000 chunks — the timings are in
/// the UsageGuide's "Platform notes". The type keeps its historical name (and <see cref="Name"/>
/// keeps "SQLite-vec") so existing configuration and stored statistics stay valid.</para>
/// <para><b>Dependencies:</b> Microsoft.Data.Sqlite only. Parameters and rows are mapped by hand
/// (REQ-FN-056): Dapper's Reflection.Emit mappers threw <see cref="PlatformNotSupportedException"/>
/// in every ahead-of-time compiled iOS and Mac Catalyst Release build.</para>
/// </remarks>
public class SqliteVecStore : IVectorStore
{
    /// <inheritdoc/>
    public string Name => "SQLite-vec";

    /// <summary>Gets the vector width this store was created for (REQ-RAG-106 / BRD-158).</summary>
    public int Dimensions => dimensions;

    private readonly string connectionString;
    private readonly int dimensions;
    private bool initialized;

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
    /// <para><b>Note:</b> No sqlite-vec virtual table is created and no extension is loaded; the
    /// search is the managed scan described on the class.</para>
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Create Documents table
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS Documents (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                SourcePath TEXT NOT NULL,
                ChunkCount INTEGER DEFAULT 0,
                IngestedAt TEXT NOT NULL,
                Metadata TEXT
            )", cancellationToken);

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
            )", cancellationToken);

        // Create index for efficient document-based queries
        await connection.ExecuteAsync(@"
            CREATE INDEX IF NOT EXISTS IdxChunksDocument ON Chunks(DocumentId)", cancellationToken);

        // Enable foreign key constraints
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON", cancellationToken);

        initialized = true;
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
            cancellationToken,
            DocumentParameters(chunk.DocumentId, docName, sourcePath, chunk));

        var vectorBytes = chunk.Vector != null ? SerializeVector(chunk.Vector) : null;
        var metadataJson = JsonSerializer.Serialize(chunk.Metadata);

        await connection.ExecuteAsync(@"
            INSERT OR REPLACE INTO Chunks (Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt)
            VALUES (@Id, @DocumentId, @Text, @Vector, @PageNumber, @ChunkIndex, @Metadata, @CreatedAt)",
            cancellationToken,
            ChunkParameters(chunk, vectorBytes, metadataJson));

        // Update document chunk count
        await UpdateDocumentChunkCountAsync(connection, chunk.DocumentId, null, cancellationToken);

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
                    transaction,
                    cancellationToken,
                    DocumentParameters(docId, docName, sourcePath, firstChunk));
            }

            // Now insert the chunks
            foreach (var chunk in chunkList)
            {
                var vectorBytes = chunk.Vector != null ? SerializeVector(chunk.Vector) : null;
                var metadataJson = JsonSerializer.Serialize(chunk.Metadata);

                await connection.ExecuteAsync(@"
                    INSERT OR REPLACE INTO Chunks (Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt)
                    VALUES (@Id, @DocumentId, @Text, @Vector, @PageNumber, @ChunkIndex, @Metadata, @CreatedAt)",
                    transaction,
                    cancellationToken,
                    ChunkParameters(chunk, vectorBytes, metadataJson));

                ids.Add(chunk.Id);
            }

            // Update chunk counts for all affected documents
            foreach (var documentId in documentIds)
            {
                await UpdateDocumentChunkCountAsync(connection, documentId, transaction, cancellationToken);
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
    /// <returns>Ranked search results ordered by similarity score, highest first.</returns>
    /// <remarks>
    /// <para><b>Algorithm (REQ-RAG-056 / BRD-93):</b> an exact cosine scan in two passes.</para>
    /// <list type="number">
    /// <item><description>Only <c>rowid</c> and the <c>Vector</c> BLOB are read. Each BLOB is scored
    /// in place — a span over SQLite's own row buffer reinterpreted as float32, no copy — with SIMD
    /// <see cref="System.Numerics.Vector{T}"/> arithmetic, and a bounded heap keeps the best
    /// <paramref name="topK"/>. Nothing else is materialised per row.</description></item>
    /// <item><description>The full rows (text, metadata) are fetched for the winners only.</description></item>
    /// </list>
    /// <para>The previous implementation read and JSON-parsed every row, deserialised every vector
    /// into a new array and sorted the whole table. The ranking is identical — ties broken by scan
    /// order exactly as the old stable sort did, scores equal to within float rounding (SIMD sums in a
    /// different order) — which <c>SqliteVecSearchTests</c> and the benchmark assert.</para>
    /// <para>A stored vector whose width differs from the query scores 0, and a zero vector scores
    /// 0, as before.</para>
    /// </remarks>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        string? documentFilter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (topK <= 0)
        {
            return Array.Empty<SearchResult>();
        }

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var winners = ScoreAllChunks(connection, queryVector, topK, documentFilter, cancellationToken);
        if (winners.Count == 0)
        {
            return Array.Empty<SearchResult>();
        }

        // One named parameter per winner, the expansion Dapper used to do for "IN @RowIds".
        var rowIdParameters = new DbParam[winners.Count];
        for (var i = 0; i < winners.Count; i++)
        {
            rowIdParameters[i] = DbParam.Int64("RowId" + i, winners[i].RowId);
        }

        var rows = await connection.QueryAsync(
            "SELECT rowid AS RowId, Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt FROM Chunks WHERE rowid IN ("
                + string.Join(", ", rowIdParameters.Select(p => "@" + p.Name)) + ")",
            ChunkRow.Read,
            cancellationToken,
            rowIdParameters).ConfigureAwait(false);

        var byRowId = rows.ToDictionary(r => r.RowId);
        var results = new List<SearchResult>(winners.Count);
        foreach (var winner in winners)
        {
            // A row deleted between the two passes is simply dropped.
            if (byRowId.TryGetValue(winner.RowId, out var row))
            {
                results.Add(new SearchResult { Chunk = row.ToTextChunk(), Score = winner.Score });
            }
        }

        return results;
    }

    /// <summary>
    /// First search pass: scores every stored vector against the query and keeps the best.
    /// </summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="queryVector">The query vector.</param>
    /// <param name="topK">How many winners to keep.</param>
    /// <param name="documentFilter">Optional document ID filter.</param>
    /// <param name="cancellationToken">Checked every 1,024 rows.</param>
    /// <returns>The winners, best first.</returns>
    private static IReadOnlyList<ManagedVectorSearch.Candidate> ScoreAllChunks(
        SqliteConnection connection,
        float[] queryVector,
        int topK,
        string? documentFilter,
        CancellationToken cancellationToken)
    {
        // Straight to SQLite's C API: sqlite3_column_blob hands back a span over SQLite's own row
        // buffer, so a vector is scored where it lies — no byte[] per row, no copy. SqliteDataReader
        // offers only copying reads (GetBytes, GetFieldValue<byte[]>), which cost more than the maths.
        var sql = documentFilter is null
            ? "SELECT rowid, Vector FROM Chunks WHERE Vector IS NOT NULL"
            : "SELECT rowid, Vector FROM Chunks WHERE Vector IS NOT NULL AND DocumentId = ?1";

        var database = connection.Handle!;
        var rc = raw.sqlite3_prepare_v2(database, sql, out var statement);
        SqliteException.ThrowExceptionForRC(rc, database);

        var collector = new ManagedVectorSearch.TopKCollector(topK);
        var queryMagnitude = ManagedVectorSearch.Magnitude(queryVector);
        long sequence = 0;

        using (statement)
        {
            if (documentFilter is not null)
            {
                SqliteException.ThrowExceptionForRC(raw.sqlite3_bind_text(statement, 1, documentFilter), database);
            }

            while ((rc = raw.sqlite3_step(statement)) == raw.SQLITE_ROW)
            {
                if ((sequence & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var blob = raw.sqlite3_column_blob(statement, 1);
                if (blob.Length == 0)
                {
                    continue;
                }

                var stored = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(blob);
                var score = ManagedVectorSearch.CosineSimilarity(queryVector, queryMagnitude, stored);
                collector.Add(raw.sqlite3_column_int64(statement, 0), score, sequence++);
            }

            if (rc != raw.SQLITE_DONE)
            {
                SqliteException.ThrowExceptionForRC(rc, database);
            }
        }

        return collector.ToRankedList();
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
        var documentId = await connection.ScalarAsync(
            "SELECT DocumentId FROM Chunks WHERE Id = @ChunkId",
            null,
            cancellationToken,
            DbParam.Text("ChunkId", chunkId)) as string;

        await connection.ExecuteAsync(
            "DELETE FROM Chunks WHERE Id = @ChunkId", cancellationToken, DbParam.Text("ChunkId", chunkId));

        // Update chunk count if document was found
        if (!string.IsNullOrEmpty(documentId))
        {
            await UpdateDocumentChunkCountAsync(connection, documentId, null, cancellationToken);
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
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON", cancellationToken);

        // Delete document (cascades to chunks)
        await connection.ExecuteAsync(
            "DELETE FROM Documents WHERE Id = @DocumentId", cancellationToken, DbParam.Text("DocumentId", documentId));

        // Also explicitly delete chunks in case foreign keys didn't cascade
        await connection.ExecuteAsync(
            "DELETE FROM Chunks WHERE DocumentId = @DocumentId", cancellationToken, DbParam.Text("DocumentId", documentId));
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

        var rows = await connection.QueryAsync(
            "SELECT Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata FROM Documents ORDER BY IngestedAt DESC",
            DocumentRow.Read,
            cancellationToken);

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

        var documentCount = (int)await ScalarInt64Async(connection, "SELECT COUNT(*) FROM Documents", null, cancellationToken);
        var chunkCount = (int)await ScalarInt64Async(connection, "SELECT COUNT(*) FROM Chunks", null, cancellationToken);

        // Get database file size using SQLite pragmas
        var pageCount = await ScalarInt64Async(connection, "SELECT page_count FROM pragma_page_count()", null, cancellationToken);
        var pageSize = await ScalarInt64Async(connection, "SELECT page_size FROM pragma_page_size()", null, cancellationToken);
        var sizeBytes = pageCount * pageSize;

        // Get last ingestion time
        var lastIngestion = await connection.ScalarAsync(
            "SELECT MAX(IngestedAt) FROM Documents", null, cancellationToken) as string;
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

        await connection.ExecuteAsync("DELETE FROM Chunks", cancellationToken);
        await connection.ExecuteAsync("DELETE FROM Documents", cancellationToken);

        // Reclaim disk space
        await connection.ExecuteAsync("VACUUM", cancellationToken);
    }

    /// <summary>
    /// Updates the chunk count for a document based on actual chunks in the database.
    /// </summary>
    /// <param name="connection">The database connection.</param>
    /// <param name="documentId">The document ID to update.</param>
    /// <param name="transaction">Optional transaction to participate in.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private static async Task UpdateDocumentChunkCountAsync(
        SqliteConnection connection,
        string documentId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var count = (int)await ScalarInt64Async(
            connection,
            "SELECT COUNT(*) FROM Chunks WHERE DocumentId = @DocumentId",
            transaction,
            cancellationToken,
            DbParam.Text("DocumentId", documentId));

        await connection.ExecuteAsync(
            "UPDATE Documents SET ChunkCount = @Count WHERE Id = @DocumentId",
            transaction,
            cancellationToken,
            DbParam.Int32("Count", count),
            DbParam.Text("DocumentId", documentId));
    }

    /// <summary>
    /// Runs a query whose single row and column is an integer.
    /// </summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="sql">The query.</param>
    /// <param name="transaction">Optional transaction to participate in.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="parameters">The query's parameters.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">The query returned no row or SQL NULL.</exception>
    private static async Task<long> ScalarInt64Async(
        SqliteConnection connection,
        string sql,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken,
        params DbParam[] parameters)
    {
        var value = await connection.ScalarAsync(sql, transaction, cancellationToken, parameters)
            ?? throw new InvalidOperationException("Sequence contains no elements");
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds the parameters of a <c>Documents</c> row insert.
    /// </summary>
    /// <param name="documentId">The document ID.</param>
    /// <param name="name">The document name.</param>
    /// <param name="sourcePath">The document's source path.</param>
    /// <param name="firstChunk">The chunk the document row is being created from.</param>
    /// <returns>The <c>@Id</c>, <c>@Name</c>, <c>@SourcePath</c>, <c>@IngestedAt</c> and <c>@Metadata</c> parameters.</returns>
    private static DbParam[] DocumentParameters(string documentId, string name, string sourcePath, TextChunk firstChunk) =>
    [
        DbParam.Text("Id", documentId),
        DbParam.Text("Name", name),
        DbParam.Text("SourcePath", sourcePath),
        DbParam.Text("IngestedAt", DateTime.UtcNow.ToString("o")),
        DbParam.Text("Metadata", SerializeDocumentMetadata(firstChunk))
    ];

    /// <summary>
    /// Builds the parameters of a <c>Chunks</c> row insert.
    /// </summary>
    /// <param name="chunk">The chunk.</param>
    /// <param name="vectorBytes">The serialized vector, or <see langword="null"/>.</param>
    /// <param name="metadataJson">The chunk metadata as JSON.</param>
    /// <returns>One parameter per <c>Chunks</c> column.</returns>
    private static DbParam[] ChunkParameters(TextChunk chunk, byte[]? vectorBytes, string metadataJson) =>
    [
        DbParam.Text("Id", chunk.Id),
        DbParam.Text("DocumentId", chunk.DocumentId),
        DbParam.Text("Text", chunk.Text),
        DbParam.Blob("Vector", vectorBytes),
        DbParam.Int32("PageNumber", chunk.PageNumber),
        DbParam.Int32("ChunkIndex", chunk.ChunkIndex),
        DbParam.Text("Metadata", metadataJson),
        DbParam.Text("CreatedAt", chunk.CreatedAt.ToString("o"))
    ];

    /// <summary>
    /// Builds the JSON written to a document row's <c>Metadata</c> column.
    /// </summary>
    /// <param name="firstChunk">The chunk the document row is being created from.</param>
    /// <returns>A JSON object holding the chunk's document-scoped metadata; <c>{}</c> when it has none.</returns>
    /// <remarks>
    /// This column used to be a hardcoded <c>{}</c>, so everything an ingestion route recorded about
    /// a document — its byte size, the URL it came from — existed on the chunks and was invisible to
    /// <see cref="ListDocumentsAsync"/>, which is the API a catalogue screen reads. Only the keys in
    /// <see cref="DocumentMetadataKeys.DocumentScoped"/> are lifted: a page number or an audio offset
    /// belongs to one chunk and would be a lie at document level.
    /// </remarks>
    private static string SerializeDocumentMetadata(TextChunk firstChunk) =>
        JsonSerializer.Serialize(DocumentMetadataKeys.ExtractDocumentScoped(firstChunk.Metadata));

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
        public long RowId { get; set; }
        public string Id { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public byte[]? Vector { get; set; }
        public int? PageNumber { get; set; }
        public int? ChunkIndex { get; set; }
        public string? Metadata { get; set; }
        public string CreatedAt { get; set; } = string.Empty;

        /// <summary>
        /// Maps the current row of a query selecting the columns in declaration order.
        /// </summary>
        public static ChunkRow Read(DbDataReader reader) => new()
        {
            RowId = reader.GetNullableInt64(0) ?? 0,
            Id = reader.GetNullableString(1) ?? string.Empty,
            DocumentId = reader.GetNullableString(2) ?? string.Empty,
            Text = reader.GetNullableString(3) ?? string.Empty,
            Vector = reader.GetNullableBytes(4),
            PageNumber = (int?)reader.GetNullableInt64(5),
            ChunkIndex = (int?)reader.GetNullableInt64(6),
            Metadata = reader.GetNullableString(7),
            CreatedAt = reader.GetNullableString(8) ?? string.Empty
        };

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
        /// Maps the current row of a query selecting the columns in declaration order.
        /// </summary>
        public static DocumentRow Read(DbDataReader reader) => new()
        {
            Id = reader.GetNullableString(0) ?? string.Empty,
            Name = reader.GetNullableString(1) ?? string.Empty,
            SourcePath = reader.GetNullableString(2) ?? string.Empty,
            ChunkCount = (int)(reader.GetNullableInt64(3) ?? 0),
            IngestedAt = reader.GetNullableString(4) ?? string.Empty,
            Metadata = reader.GetNullableString(5)
        };

        /// <summary>
        /// Converts this row to a Document model.
        /// </summary>
        public Document ToDocument()
        {
            // Not a plain Deserialize: that hands back JsonElement values, which are not
            // IConvertible, so a caller reading a stored number gets an exception or a silent
            // fallback. FromJson unwraps them to the primitives that were stored.
            var metadata = DocumentMetadataKeys.FromJson(Metadata);

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
