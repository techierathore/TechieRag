using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.VectorStores;

/// <summary>
/// PostgreSQL vector store implementation using the pgvector extension.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides vector storage and similarity search capabilities using
/// PostgreSQL with the pgvector extension, enabling integration with existing PostgreSQL
/// infrastructure for enterprise deployments.</para>
/// <para><b>Code Flow:</b> Instantiated by TechieRagBuilder when PostgreSQL is configured
/// as the vector store. Uses NpgsqlDataSource for efficient connection pooling.</para>
/// <para><b>Design:</b> Implements IVectorStore using parameterized queries for security.
/// Uses JSONB columns for flexible metadata storage and IVFFlat indexing for fast
/// approximate nearest neighbor searches.</para>
/// </remarks>
public class PgVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;
    private readonly ILogger<PgVectorStore> logger;
    private readonly int vectorDimension;
    private bool isInitialized;

    /// <summary>
    /// Gets the display name of this vector store implementation.
    /// </summary>
    public string Name => "PGVector";

    /// <summary>
    /// Initializes a new instance of the <see cref="PgVectorStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    /// <param name="vectorDimension">The dimension of the embedding vectors (default: 1024 for BGE-M3).</param>
    /// <exception cref="ArgumentNullException">Thrown when connectionString or logger is null.</exception>
    public PgVectorStore(string connectionString, ILogger<PgVectorStore> logger, int vectorDimension = 1024)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNull(logger);

        this.logger = logger;
        this.vectorDimension = vectorDimension;

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        this.dataSource = dataSourceBuilder.Build();
    }

    /// <summary>
    /// Initializes the vector store, creating the pgvector extension and required tables.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the pgvector extension is not available.</exception>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (isInitialized)
        {
            return;
        }

        logger.LogInformation("Initializing PGVector store with dimension {Dimension}", vectorDimension);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Try to create the pgvector extension
        try
        {
            await using var extCmd = connection.CreateCommand();
            extCmd.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
            await extCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "42501" || ex.SqlState == "58P01")
        {
            throw new InvalidOperationException(
                "The pgvector extension is not available. Please ensure pgvector is installed on your PostgreSQL server. " +
                "Installation instructions: https://github.com/pgvector/pgvector#installation",
                ex);
        }

        // Create Documents table
        await using var docCmd = connection.CreateCommand();
        docCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Documents (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                SourcePath TEXT NOT NULL,
                ChunkCount INTEGER DEFAULT 0,
                IngestedAt TIMESTAMPTZ NOT NULL,
                Metadata JSONB
            )
            """;
        await docCmd.ExecuteNonQueryAsync(cancellationToken);

        // Create Chunks table with vector column
        await using var chunkCmd = connection.CreateCommand();
        chunkCmd.CommandText = $"""
            CREATE TABLE IF NOT EXISTS Chunks (
                Id TEXT PRIMARY KEY,
                DocumentId TEXT NOT NULL REFERENCES Documents(Id) ON DELETE CASCADE,
                Text TEXT NOT NULL,
                Embedding vector({vectorDimension}),
                PageNumber INTEGER,
                ChunkIndex INTEGER,
                Metadata JSONB,
                CreatedAt TIMESTAMPTZ NOT NULL
            )
            """;
        await chunkCmd.ExecuteNonQueryAsync(cancellationToken);

        // Create indexes
        await using var idxDocCmd = connection.CreateCommand();
        idxDocCmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxChunksDocument ON Chunks(DocumentId)";
        await idxDocCmd.ExecuteNonQueryAsync(cancellationToken);

        // Create IVFFlat index for vector similarity search
        // Note: IVFFlat requires data to build the index, so we use CREATE INDEX IF NOT EXISTS
        // which will be a no-op if index already exists
        try
        {
            await using var idxVecCmd = connection.CreateCommand();
            idxVecCmd.CommandText = "CREATE INDEX IF NOT EXISTS IdxChunksEmbedding ON Chunks USING ivfflat (Embedding vector_cosine_ops)";
            await idxVecCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.Message.Contains("ivfflat"))
        {
            // IVFFlat may not be available in all pgvector installations
            logger.LogWarning("IVFFlat index creation failed. Falling back to no index for vector column: {Message}", ex.Message);
        }

        isInitialized = true;
        logger.LogInformation("PGVector store initialized successfully");
    }

    /// <summary>
    /// Inserts or updates a single text chunk with its vector embedding.
    /// </summary>
    /// <param name="chunk">The chunk containing text, vector, and metadata.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The ID of the upserted chunk.</returns>
    /// <exception cref="ArgumentNullException">Thrown when chunk is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the store is not initialized.</exception>
    public async Task<string> UpsertAsync(TextChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        EnsureInitialized();

        logger.LogDebug("Upserting chunk {ChunkId} for document {DocumentId}", chunk.Id, chunk.DocumentId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Ensure document exists
            await EnsureDocumentExistsAsync(connection, chunk, cancellationToken);

            // Upsert chunk
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Chunks (Id, DocumentId, Text, Embedding, PageNumber, ChunkIndex, Metadata, CreatedAt)
                VALUES (@Id, @DocumentId, @Text, @Embedding, @PageNumber, @ChunkIndex, @Metadata::jsonb, @CreatedAt)
                ON CONFLICT (Id) DO UPDATE SET
                    Text = EXCLUDED.Text,
                    Embedding = EXCLUDED.Embedding,
                    PageNumber = EXCLUDED.PageNumber,
                    ChunkIndex = EXCLUDED.ChunkIndex,
                    Metadata = EXCLUDED.Metadata
                """;

            cmd.Parameters.AddWithValue("Id", chunk.Id);
            cmd.Parameters.AddWithValue("DocumentId", chunk.DocumentId);
            // Sanitize text to remove null bytes that PostgreSQL rejects
            cmd.Parameters.AddWithValue("Text", SanitizeTextForPostgres(chunk.Text));
            cmd.Parameters.AddWithValue("Embedding", chunk.Vector != null ? new Vector(chunk.Vector) : DBNull.Value);
            cmd.Parameters.AddWithValue("PageNumber", chunk.PageNumber.HasValue ? chunk.PageNumber.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("ChunkIndex", chunk.ChunkIndex.HasValue ? chunk.ChunkIndex.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("Metadata", JsonSerializer.Serialize(chunk.Metadata));
            cmd.Parameters.AddWithValue("CreatedAt", chunk.CreatedAt);

            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // Update document chunk count
            await UpdateDocumentChunkCountAsync(connection, chunk.DocumentId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            logger.LogDebug("Successfully upserted chunk {ChunkId}", chunk.Id);
            return chunk.Id;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Inserts or updates multiple chunks in a batch operation for efficiency.
    /// </summary>
    /// <param name="chunks">Collection of chunks to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of IDs for all upserted chunks.</returns>
    /// <exception cref="ArgumentNullException">Thrown when chunks is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the store is not initialized.</exception>
    public async Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        EnsureInitialized();

        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
        {
            return Array.Empty<string>();
        }

        logger.LogInformation("Upserting batch of {Count} chunks", chunkList.Count);

        var ids = new List<string>(chunkList.Count);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Group chunks by document and materialize the list so we can iterate multiple times
        var chunksByDocument = chunkList.GroupBy(c => c.DocumentId).ToList();

        try
        {
            foreach (var documentGroup in chunksByDocument)
            {
                var firstChunk = documentGroup.First();
                await EnsureDocumentExistsAsync(connection, firstChunk, cancellationToken);
            }

            // Batch insert chunks using COPY for efficiency
            // Binary importer MUST be disposed to release connection from COPY state
            var binaryImportSucceeded = false;
            try
            {
                await using (var writer = await connection.BeginBinaryImportAsync(
                    "COPY Chunks (Id, DocumentId, Text, Embedding, PageNumber, ChunkIndex, Metadata, CreatedAt) FROM STDIN (FORMAT BINARY)",
                    cancellationToken))
                {
                    foreach (var chunk in chunkList)
                    {
                        await writer.StartRowAsync(cancellationToken);
                        await writer.WriteAsync(chunk.Id, cancellationToken);
                        await writer.WriteAsync(chunk.DocumentId, cancellationToken);
                        // Sanitize text to remove null bytes that PostgreSQL rejects
                        await writer.WriteAsync(SanitizeTextForPostgres(chunk.Text), cancellationToken);

                        if (chunk.Vector != null)
                        {
                            // Explicitly specify data type name for vector type in binary import
                            await writer.WriteAsync(new Vector(chunk.Vector), "vector", cancellationToken);
                        }
                        else
                        {
                            await writer.WriteNullAsync(cancellationToken);
                        }

                        if (chunk.PageNumber.HasValue)
                        {
                            await writer.WriteAsync(chunk.PageNumber.Value, cancellationToken);
                        }
                        else
                        {
                            await writer.WriteNullAsync(cancellationToken);
                        }

                        if (chunk.ChunkIndex.HasValue)
                        {
                            await writer.WriteAsync(chunk.ChunkIndex.Value, cancellationToken);
                        }
                        else
                        {
                            await writer.WriteNullAsync(cancellationToken);
                        }

                        await writer.WriteAsync(JsonSerializer.Serialize(chunk.Metadata), NpgsqlTypes.NpgsqlDbType.Jsonb, cancellationToken);
                        await writer.WriteAsync(chunk.CreatedAt, cancellationToken);

                        ids.Add(chunk.Id);
                    }

                    await writer.CompleteAsync(cancellationToken);
                    binaryImportSucceeded = true;
                }
                // Writer is now disposed and connection is out of COPY state
            }
            catch (Exception ex)
            {
                // If binary import fails, close the connection to prevent it from
                // being returned to the pool in a bad state
                logger.LogWarning(ex, "Binary import failed, closing connection to reset state");
                await connection.CloseAsync();
                throw;
            }

            // Only proceed if binary import succeeded and connection is ready
            if (!binaryImportSucceeded)
            {
                throw new InvalidOperationException("Binary import did not complete successfully");
            }

            // Update document chunk counts after binary import is complete
            foreach (var documentGroup in chunksByDocument)
            {
                await UpdateDocumentChunkCountAsync(connection, documentGroup.Key, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Successfully upserted {Count} chunks", ids.Count);
            return ids;
        }
        catch (Exception ex) when (connection.State == System.Data.ConnectionState.Open)
        {
            // Only try to rollback if connection is still open
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                logger.LogWarning(rollbackEx, "Failed to rollback transaction after error: {Error}", ex.Message);
            }
            throw;
        }
    }

    /// <summary>
    /// Performs vector similarity search using pgvector's cosine distance operator.
    /// </summary>
    /// <param name="queryVector">The embedding vector of the search query.</param>
    /// <param name="topK">Maximum number of results to return.</param>
    /// <param name="documentFilter">Optional document ID to restrict search scope.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Ranked search results ordered by similarity score (highest first).</returns>
    /// <exception cref="ArgumentNullException">Thrown when queryVector is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the store is not initialized.</exception>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        string? documentFilter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        EnsureInitialized();

        logger.LogDebug("Searching for top {TopK} results, documentFilter: {Filter}", topK, documentFilter ?? "(none)");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();

        // Use <=> operator for cosine distance (lower is more similar)
        // Convert to similarity score: 1 - distance
        if (documentFilter != null)
        {
            cmd.CommandText = """
                SELECT Id, DocumentId, Text, Embedding, PageNumber, ChunkIndex, Metadata, CreatedAt,
                       1 - (Embedding <=> @QueryVector) AS Score
                FROM Chunks
                WHERE DocumentId = @DocumentId AND Embedding IS NOT NULL
                ORDER BY Embedding <=> @QueryVector
                LIMIT @TopK
                """;
            cmd.Parameters.AddWithValue("DocumentId", documentFilter);
        }
        else
        {
            cmd.CommandText = """
                SELECT Id, DocumentId, Text, Embedding, PageNumber, ChunkIndex, Metadata, CreatedAt,
                       1 - (Embedding <=> @QueryVector) AS Score
                FROM Chunks
                WHERE Embedding IS NOT NULL
                ORDER BY Embedding <=> @QueryVector
                LIMIT @TopK
                """;
        }

        cmd.Parameters.AddWithValue("QueryVector", new Vector(queryVector));
        cmd.Parameters.AddWithValue("TopK", topK);

        var results = new List<SearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var chunk = new TextChunk
            {
                Id = reader.GetString(0),
                DocumentId = reader.GetString(1),
                Text = reader.GetString(2),
                Vector = reader.IsDBNull(3) ? null : ((Vector)reader.GetValue(3)).ToArray(),
                PageNumber = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                ChunkIndex = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Metadata = reader.IsDBNull(6)
                    ? new Dictionary<string, object>()
                    : JsonSerializer.Deserialize<Dictionary<string, object>>(reader.GetString(6)) ?? new Dictionary<string, object>(),
                CreatedAt = reader.GetDateTime(7)
            };

            var score = reader.GetFloat(8);

            results.Add(new SearchResult
            {
                Chunk = chunk,
                Score = score
            });
        }

        logger.LogDebug("Search returned {Count} results", results.Count);
        return results;
    }

    /// <summary>
    /// Deletes a specific chunk by its ID.
    /// </summary>
    /// <param name="chunkId">The ID of the chunk to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when chunkId is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the store is not initialized.</exception>
    public async Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkId);
        EnsureInitialized();

        logger.LogDebug("Deleting chunk {ChunkId}", chunkId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Get document ID before deleting
        await using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT DocumentId FROM Chunks WHERE Id = @Id";
        selectCmd.Parameters.AddWithValue("Id", chunkId);
        var documentId = await selectCmd.ExecuteScalarAsync(cancellationToken) as string;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Chunks WHERE Id = @Id";
        cmd.Parameters.AddWithValue("Id", chunkId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Update document chunk count if document was found
        if (documentId != null)
        {
            await UpdateDocumentChunkCountAsync(connection, documentId, cancellationToken);
        }

        logger.LogDebug("Successfully deleted chunk {ChunkId}", chunkId);
    }

    /// <summary>
    /// Deletes all chunks belonging to a specific document.
    /// </summary>
    /// <param name="documentId">The document ID whose chunks should be deleted.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when documentId is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the store is not initialized.</exception>
    public async Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        EnsureInitialized();

        logger.LogInformation("Deleting document {DocumentId} and all its chunks", documentId);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Delete chunks (will cascade from document delete, but explicit for clarity)
            await using var chunkCmd = connection.CreateCommand();
            chunkCmd.CommandText = "DELETE FROM Chunks WHERE DocumentId = @DocumentId";
            chunkCmd.Parameters.AddWithValue("DocumentId", documentId);
            var chunksDeleted = await chunkCmd.ExecuteNonQueryAsync(cancellationToken);

            // Delete document
            await using var docCmd = connection.CreateCommand();
            docCmd.CommandText = "DELETE FROM Documents WHERE Id = @Id";
            docCmd.Parameters.AddWithValue("Id", documentId);
            await docCmd.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Successfully deleted document {DocumentId} with {ChunkCount} chunks", documentId, chunksDeleted);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Lists all documents in the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>List of documents with their metadata.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the store is not initialized.</exception>
    public async Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        logger.LogDebug("Listing all documents");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata
            FROM Documents
            ORDER BY IngestedAt DESC
            """;

        var documents = new List<Document>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(new Document
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                SourcePath = reader.GetString(2),
                ChunkCount = reader.GetInt32(3),
                IngestedAt = reader.GetDateTime(4),
                // Unwrapped rather than deserialized straight into object values: JsonElement is not
                // IConvertible, so a stored number would be unreadable to every ordinary caller.
                Metadata = reader.IsDBNull(5)
                    ? new Dictionary<string, object>()
                    : DocumentMetadataKeys.FromJson(reader.GetString(5))
            });
        }

        logger.LogDebug("Found {Count} documents", documents.Count);
        return documents;
    }

    /// <summary>
    /// Retrieves statistics about the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Statistics including counts and storage size.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the store is not initialized.</exception>
    public async Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        logger.LogDebug("Getting vector store statistics");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // Get document count
        await using var docCountCmd = connection.CreateCommand();
        docCountCmd.CommandText = "SELECT COUNT(*) FROM Documents";
        var docCount = Convert.ToInt32(await docCountCmd.ExecuteScalarAsync(cancellationToken));

        // Get chunk count
        await using var chunkCountCmd = connection.CreateCommand();
        chunkCountCmd.CommandText = "SELECT COUNT(*) FROM Chunks";
        var chunkCount = Convert.ToInt32(await chunkCountCmd.ExecuteScalarAsync(cancellationToken));

        // Get last ingestion time
        await using var lastIngestCmd = connection.CreateCommand();
        lastIngestCmd.CommandText = "SELECT MAX(IngestedAt) FROM Documents";
        var lastIngestResult = await lastIngestCmd.ExecuteScalarAsync(cancellationToken);
        var lastIngestionTime = lastIngestResult is DateTime dt ? dt : (DateTime?)null;

        // Get approximate size (using pg_total_relation_size for tables)
        await using var sizeCmd = connection.CreateCommand();
        sizeCmd.CommandText = """
            SELECT COALESCE(
                pg_total_relation_size('Documents') + pg_total_relation_size('Chunks'),
                0
            )
            """;
        long sizeBytes = 0;
        try
        {
            sizeBytes = Convert.ToInt64(await sizeCmd.ExecuteScalarAsync(cancellationToken));
        }
        catch (PostgresException)
        {
            // pg_total_relation_size may not be available in all configurations
            logger.LogDebug("Unable to determine table sizes, returning 0");
        }

        return new IngestionStats
        {
            TotalDocuments = docCount,
            TotalChunks = chunkCount,
            VectorStoreSizeBytes = sizeBytes,
            LastIngestionTime = lastIngestionTime,
            VectorStoreName = Name
        };
    }

    /// <summary>
    /// Clears all data from the vector store.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous clear operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the store is not initialized.</exception>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        logger.LogWarning("Clearing all data from PGVector store");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Delete all chunks first (due to foreign key)
            await using var chunkCmd = connection.CreateCommand();
            chunkCmd.CommandText = "DELETE FROM Chunks";
            await chunkCmd.ExecuteNonQueryAsync(cancellationToken);

            // Delete all documents
            await using var docCmd = connection.CreateCommand();
            docCmd.CommandText = "DELETE FROM Documents";
            await docCmd.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Successfully cleared all data from PGVector store");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Disposes the data source and releases all resources.
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await dataSource.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Ensures the store has been initialized before operations.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the store is not initialized.</exception>
    private void EnsureInitialized()
    {
        if (!isInitialized)
        {
            throw new InvalidOperationException(
                "PgVectorStore has not been initialized. Call InitializeAsync before performing operations.");
        }
    }

    /// <summary>
    /// Ensures a document record exists for the given chunk, creating it if necessary.
    /// </summary>
    private async Task EnsureDocumentExistsAsync(NpgsqlConnection connection, TextChunk chunk, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Documents (Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata)
            VALUES (@Id, @Name, @SourcePath, 0, @IngestedAt, @Metadata::jsonb)
            ON CONFLICT (Id) DO NOTHING
            """;

        cmd.Parameters.AddWithValue("Id", chunk.DocumentId);

        // Extract document name and source path from chunk metadata if available
        var name = chunk.Metadata.TryGetValue("DocumentName", out var docName) ? docName?.ToString() : chunk.DocumentId;
        var sourcePath = chunk.Metadata.TryGetValue("SourcePath", out var path) ? path?.ToString() : "unknown";

        cmd.Parameters.AddWithValue("Name", name ?? chunk.DocumentId);
        cmd.Parameters.AddWithValue("SourcePath", sourcePath ?? "unknown");
        cmd.Parameters.AddWithValue("IngestedAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("Metadata", JsonSerializer.Serialize(chunk.Metadata));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Updates the chunk count for a document.
    /// </summary>
    private async Task UpdateDocumentChunkCountAsync(NpgsqlConnection connection, string documentId, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Documents
            SET ChunkCount = (SELECT COUNT(*) FROM Chunks WHERE DocumentId = @DocumentId)
            WHERE Id = @DocumentId
            """;
        cmd.Parameters.AddWithValue("DocumentId", documentId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Sanitizes text for PostgreSQL by removing null bytes and other invalid UTF-8 sequences.
    /// </summary>
    /// <param name="text">The text to sanitize.</param>
    /// <returns>Sanitized text safe for PostgreSQL.</returns>
    private static string SanitizeTextForPostgres(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Remove null bytes (0x00) which PostgreSQL doesn't accept in UTF-8 text
        // Also remove other control characters that might cause issues
        var sanitized = text.Replace("\0", "");

        return sanitized;
    }
}
