using Qdrant.Client;
using Qdrant.Client.Grpc;
using TechieRag.Abstractions;
using TechieRag.Models;
using System.Text.Json;

using Document = TechieRag.Models.Document;

namespace TechieRag.VectorStores;

/// <summary>
/// Qdrant-based implementation of the vector store interface.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides high-performance vector storage and retrieval using Qdrant,
/// a dedicated vector database optimized for similarity search operations.</para>
/// <para><b>Code Flow:</b> Instantiated by TechieRagBuilder when Qdrant is configured as the
/// vector store provider. Uses gRPC for efficient communication with the Qdrant server.</para>
/// <para><b>Configuration:</b> Requires a Qdrant server endpoint. Supports both local and
/// cloud-hosted Qdrant instances.</para>
/// </remarks>
public class QdrantStore : IVectorStore
{
    /// <summary>
    /// The name of this vector store implementation.
    /// </summary>
    private const string StoreName = "Qdrant";

    /// <summary>
    /// The name of the collection used to store document chunks.
    /// </summary>
    private const string ChunksCollectionName = "techierag_chunks";

    /// <summary>
    /// The name of the collection used to store document metadata.
    /// </summary>
    private const string DocumentsCollectionName = "techierag_documents";

    /// <summary>
    /// The Qdrant client for communicating with the Qdrant server.
    /// </summary>
    private readonly QdrantClient client;

    /// <summary>
    /// The name of the chunks collection.
    /// </summary>
    private readonly string collectionName;

    /// <summary>
    /// The dimensionality of the vector embeddings.
    /// </summary>
    private readonly int dimensions;

    /// <summary>
    /// Indicates whether the store has been initialized.
    /// </summary>
    private bool initialized;

    /// <inheritdoc/>
    public string Name => StoreName;

    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantStore"/> class.
    /// </summary>
    /// <param name="endpoint">The Qdrant server endpoint URL (e.g., "http://localhost:6334").</param>
    /// <param name="dimensions">The dimensionality of vector embeddings. Defaults to 1024 for BGE-M3.</param>
    /// <param name="collectionName">The name of the collection to use. Defaults to "techierag_chunks".</param>
    /// <param name="apiKey">Optional API key for Qdrant authentication.</param>
    /// <exception cref="ArgumentNullException">Thrown when endpoint is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when dimensions is less than or equal to zero.</exception>
    public QdrantStore(string endpoint, int dimensions = 1024, string collectionName = ChunksCollectionName, string? apiKey = null)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentNullException(nameof(endpoint), "Qdrant endpoint cannot be null or empty.");
        }

        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be greater than zero.");
        }

        this.dimensions = dimensions;
        this.collectionName = collectionName;
        this.initialized = false;

        var uri = new Uri(endpoint);

        // REQ-NFR-004: honour the endpoint's scheme instead of forcing cleartext. An
        // https:// endpoint now negotiates TLS, so a remote Qdrant's API key is no longer
        // transmitted in the clear. http:// endpoints (the local-container default) are
        // unchanged.
        this.client = new QdrantClient(
            uri.Host, uri.Port, https: uri.Scheme == Uri.UriSchemeHttps, apiKey: apiKey);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantStore"/> class with a pre-configured client.
    /// </summary>
    /// <param name="client">The Qdrant client to use.</param>
    /// <param name="dimensions">The dimensionality of vector embeddings. Defaults to 1024 for BGE-M3.</param>
    /// <param name="collectionName">The name of the collection to use. Defaults to "techierag_chunks".</param>
    /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when dimensions is less than or equal to zero.</exception>
    public QdrantStore(QdrantClient client, int dimensions = 1024, string collectionName = ChunksCollectionName)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));

        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be greater than zero.");
        }

        this.dimensions = dimensions;
        this.collectionName = collectionName;
        this.initialized = false;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Creates the chunks and documents collections if they do not exist.
    /// Uses cosine distance metric for vector similarity calculations.
    /// </remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        try
        {
            // Check if chunks collection exists
            var collections = await client.ListCollectionsAsync(cancellationToken);
            var collectionNames = collections.ToList();

            // Create chunks collection if it doesn't exist
            if (!collectionNames.Contains(collectionName))
            {
                await client.CreateCollectionAsync(
                    collectionName,
                    new VectorParams
                    {
                        Size = (ulong)dimensions,
                        Distance = Distance.Cosine
                    },
                    cancellationToken: cancellationToken);
            }

            // Create documents collection if it doesn't exist
            if (!collectionNames.Contains(DocumentsCollectionName))
            {
                await client.CreateCollectionAsync(
                    DocumentsCollectionName,
                    new VectorParams
                    {
                        Size = 1,
                        Distance = Distance.Cosine
                    },
                    cancellationToken: cancellationToken);
            }

            initialized = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize Qdrant store: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Stores the chunk with its vector embedding and metadata in the Qdrant collection.
    /// Uses the chunk ID as the point ID for efficient retrieval.
    /// </remarks>
    public async Task<string> UpsertAsync(TextChunk chunk, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (chunk.Vector == null || chunk.Vector.Length == 0)
        {
            throw new ArgumentException("Chunk must have a vector embedding.", nameof(chunk));
        }

        var pointId = CreatePointId(chunk.Id);
        var payload = CreatePayload(chunk);

        var point = new PointStruct
        {
            Id = pointId,
            Vectors = chunk.Vector,
            Payload = { payload }
        };

        await client.UpsertAsync(
            collectionName,
            new[] { point },
            cancellationToken: cancellationToken);

        return chunk.Id;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Performs batch upsert for improved performance when storing multiple chunks.
    /// All chunks in the batch are stored in a single API call.
    /// </remarks>
    public async Task<IReadOnlyList<string>> UpsertBatchAsync(IEnumerable<TextChunk> chunks, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var chunkList = chunks.ToList();
        if (chunkList.Count == 0)
        {
            return Array.Empty<string>();
        }

        var points = new List<PointStruct>();
        var ids = new List<string>();

        foreach (var chunk in chunkList)
        {
            if (chunk.Vector == null || chunk.Vector.Length == 0)
            {
                throw new ArgumentException($"Chunk {chunk.Id} must have a vector embedding.");
            }

            var pointId = CreatePointId(chunk.Id);
            var payload = CreatePayload(chunk);

            points.Add(new PointStruct
            {
                Id = pointId,
                Vectors = chunk.Vector,
                Payload = { payload }
            });

            ids.Add(chunk.Id);
        }

        await client.UpsertAsync(
            collectionName,
            points,
            cancellationToken: cancellationToken);

        // Update document metadata after upserting chunks
        await UpdateDocumentMetadataAsync(chunkList, cancellationToken);

        return ids;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Performs vector similarity search using cosine distance.
    /// Returns results sorted by similarity score in descending order.
    /// </remarks>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 5,
        string? documentFilter = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (queryVector == null || queryVector.Length == 0)
        {
            throw new ArgumentException("Query vector cannot be null or empty.", nameof(queryVector));
        }

        Filter? filter = null;
        if (!string.IsNullOrEmpty(documentFilter))
        {
            filter = new Filter
            {
                Must =
                {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "DocumentId",
                            Match = new Match { Keyword = documentFilter }
                        }
                    }
                }
            };
        }

        var searchResult = await client.SearchAsync(
            collectionName,
            queryVector,
            filter: filter,
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: cancellationToken);

        var results = new List<SearchResult>();

        foreach (var scored in searchResult)
        {
            var chunk = CreateChunkFromPayload(scored.Id, scored.Payload);
            results.Add(new SearchResult
            {
                Chunk = chunk,
                Score = scored.Score
            });
        }

        return results;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Deletes a single point from the collection by its ID.
    /// </remarks>
    public async Task DeleteAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (string.IsNullOrEmpty(chunkId))
        {
            throw new ArgumentNullException(nameof(chunkId), "Chunk ID cannot be null or empty.");
        }

        var chunkGuid = GetGuidFromId(chunkId);

        await client.DeleteAsync(
            collectionName,
            ids: new List<Guid> { chunkGuid },
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Deletes all chunks associated with a specific document using a payload filter.
    /// Also removes the document metadata from the documents collection.
    /// </remarks>
    public async Task DeleteByDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (string.IsNullOrEmpty(documentId))
        {
            throw new ArgumentNullException(nameof(documentId), "Document ID cannot be null or empty.");
        }

        var filter = new Filter
        {
            Must =
            {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "DocumentId",
                        Match = new Match { Keyword = documentId }
                    }
                }
            }
        };

        await client.DeleteAsync(
            collectionName,
            filter: filter,
            cancellationToken: cancellationToken);

        // Also delete from documents collection
        var docGuid = GetGuidFromId(documentId);
        await client.DeleteAsync(
            DocumentsCollectionName,
            ids: new List<Guid> { docGuid },
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Retrieves all documents from the documents metadata collection.
    /// </remarks>
    public async Task<IReadOnlyList<Document>> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var documents = new List<Document>();

        try
        {
            // Scroll through all points in the documents collection
            var scrollResult = await client.ScrollAsync(
                DocumentsCollectionName,
                limit: 1000,
                payloadSelector: true,
                cancellationToken: cancellationToken);

            foreach (var point in scrollResult.Result)
            {
                var doc = CreateDocumentFromPayload(point.Id, point.Payload);
                documents.Add(doc);
            }
        }
        catch
        {
            // Collection might not exist or be empty
            return documents;
        }

        return documents;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Retrieves statistics about the vector store including document and chunk counts.
    /// </remarks>
    public async Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        int totalChunks = 0;
        int totalDocuments = 0;
        DateTime? lastIngestionTime = null;

        try
        {
            var chunksInfo = await client.GetCollectionInfoAsync(collectionName);
            totalChunks = (int)chunksInfo.PointsCount;

            var docsInfo = await client.GetCollectionInfoAsync(DocumentsCollectionName);
            totalDocuments = (int)docsInfo.PointsCount;

            // Get the most recent document to find last ingestion time
            var scrollResult = await client.ScrollAsync(
                DocumentsCollectionName,
                limit: 1,
                payloadSelector: true,
                cancellationToken: cancellationToken);

            foreach (var point in scrollResult.Result)
            {
                if (point.Payload.TryGetValue("IngestedAt", out var ingestedAtValue))
                {
                    if (DateTime.TryParse(ingestedAtValue.StringValue, out var ingestedAt))
                    {
                        lastIngestionTime = ingestedAt;
                    }
                }
            }
        }
        catch
        {
            // Collections might not exist
        }

        // Estimate storage size (approximate: 4 bytes per dimension per vector + metadata overhead)
        long estimatedSize = totalChunks * (dimensions * 4 + 500);

        return new IngestionStats
        {
            TotalDocuments = totalDocuments,
            TotalChunks = totalChunks,
            VectorStoreSizeBytes = estimatedSize,
            LastIngestionTime = lastIngestionTime,
            VectorStoreName = StoreName
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Deletes and recreates both collections to clear all data.
    /// </remarks>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Delete chunks collection
            await client.DeleteCollectionAsync(collectionName, cancellationToken: cancellationToken);
        }
        catch
        {
            // Collection might not exist
        }

        try
        {
            // Delete documents collection
            await client.DeleteCollectionAsync(DocumentsCollectionName, cancellationToken: cancellationToken);
        }
        catch
        {
            // Collection might not exist
        }

        initialized = false;

        // Reinitialize to recreate collections
        await InitializeAsync(cancellationToken);
    }

    /// <summary>
    /// Ensures the store has been initialized before performing operations.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!initialized)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Creates a Qdrant point ID from a string chunk ID.
    /// </summary>
    /// <param name="id">The string ID to convert.</param>
    /// <returns>A Qdrant PointId.</returns>
    private static PointId CreatePointId(string id)
    {
        // Try to parse as GUID first, otherwise generate a deterministic GUID from the string
        if (Guid.TryParse(id, out var guid))
        {
            return new PointId { Uuid = guid.ToString() };
        }

        // Generate a deterministic GUID from the string using MD5 hash
        var deterministicGuid = GenerateDeterministicGuid(id);
        return new PointId { Uuid = deterministicGuid.ToString() };
    }

    /// <summary>
    /// Gets a GUID from a string ID for use with delete operations.
    /// </summary>
    /// <param name="id">The string ID to convert.</param>
    /// <returns>A GUID suitable for Qdrant operations.</returns>
    private static Guid GetGuidFromId(string id)
    {
        // Try to parse as GUID first, otherwise generate a deterministic GUID from the string
        if (Guid.TryParse(id, out var guid))
        {
            return guid;
        }

        // Generate a deterministic GUID from the string using MD5 hash
        return GenerateDeterministicGuid(id);
    }

    /// <summary>
    /// Generates a deterministic GUID from a string using MD5 hash.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>A deterministic GUID.</returns>
    private static Guid GenerateDeterministicGuid(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hashBytes = md5.ComputeHash(inputBytes);
        return new Guid(hashBytes);
    }

    /// <summary>
    /// Creates the payload dictionary for a text chunk.
    /// </summary>
    /// <param name="chunk">The text chunk to create payload for.</param>
    /// <returns>A dictionary of payload values.</returns>
    private static Dictionary<string, Value> CreatePayload(TextChunk chunk)
    {
        var payload = new Dictionary<string, Value>
        {
            ["DocumentId"] = new Value { StringValue = chunk.DocumentId },
            ["Text"] = new Value { StringValue = chunk.Text },
            ["CreatedAt"] = new Value { StringValue = chunk.CreatedAt.ToString("O") }
        };

        if (chunk.PageNumber.HasValue)
        {
            payload["PageNumber"] = new Value { IntegerValue = chunk.PageNumber.Value };
        }

        if (chunk.ChunkIndex.HasValue)
        {
            payload["ChunkIndex"] = new Value { IntegerValue = chunk.ChunkIndex.Value };
        }

        if (chunk.Metadata.Count > 0)
        {
            payload["Metadata"] = new Value { StringValue = JsonSerializer.Serialize(chunk.Metadata) };
        }

        return payload;
    }

    /// <summary>
    /// Creates a TextChunk from Qdrant payload data.
    /// </summary>
    /// <param name="pointId">The point ID.</param>
    /// <param name="payload">The payload dictionary.</param>
    /// <returns>A TextChunk populated with the payload data.</returns>
    private static TextChunk CreateChunkFromPayload(PointId pointId, IDictionary<string, Value> payload)
    {
        var chunk = new TextChunk
        {
            Id = pointId.Uuid ?? pointId.Num.ToString(),
            DocumentId = payload.TryGetValue("DocumentId", out var docId) ? docId.StringValue : string.Empty,
            Text = payload.TryGetValue("Text", out var text) ? text.StringValue : string.Empty
        };

        if (payload.TryGetValue("PageNumber", out var pageNumber))
        {
            chunk.PageNumber = (int)pageNumber.IntegerValue;
        }

        if (payload.TryGetValue("ChunkIndex", out var chunkIndex))
        {
            chunk.ChunkIndex = (int)chunkIndex.IntegerValue;
        }

        if (payload.TryGetValue("CreatedAt", out var createdAt) &&
            DateTime.TryParse(createdAt.StringValue, out var createdAtDate))
        {
            chunk.CreatedAt = createdAtDate;
        }

        if (payload.TryGetValue("Metadata", out var metadata) &&
            !string.IsNullOrEmpty(metadata.StringValue))
        {
            try
            {
                var metadataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(metadata.StringValue);
                if (metadataDict != null)
                {
                    chunk.Metadata = metadataDict;
                }
            }
            catch
            {
                // Ignore deserialization errors
            }
        }

        return chunk;
    }

    /// <summary>
    /// Updates document metadata after upserting chunks.
    /// </summary>
    /// <param name="chunks">The chunks that were upserted.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task UpdateDocumentMetadataAsync(IReadOnlyList<TextChunk> chunks, CancellationToken cancellationToken)
    {
        // Group chunks by document ID
        var documentGroups = chunks.GroupBy(c => c.DocumentId);

        var points = new List<PointStruct>();

        foreach (var group in documentGroups)
        {
            var documentId = group.Key;
            var chunkCount = group.Count();
            var firstChunk = group.First();

            var payload = new Dictionary<string, Value>
            {
                ["Id"] = new Value { StringValue = documentId },
                ["Name"] = new Value { StringValue = firstChunk.Metadata.TryGetValue("FileName", out var fileName) ? fileName?.ToString() ?? documentId : documentId },
                ["SourcePath"] = new Value { StringValue = firstChunk.Metadata.TryGetValue("SourcePath", out var sourcePath) ? sourcePath?.ToString() ?? string.Empty : string.Empty },
                ["ChunkCount"] = new Value { IntegerValue = chunkCount },
                ["IngestedAt"] = new Value { StringValue = DateTime.UtcNow.ToString("O") },

                // Document-scoped metadata (byte size, source URL, …) as one JSON string rather
                // than one payload field per key: the set is open, and a payload schema that has to
                // grow every time an ingestion route records something new is how this ended up
                // dropped entirely on the round trip.
                ["Metadata"] = new Value
                {
                    StringValue = JsonSerializer.Serialize(
                        DocumentMetadataKeys.ExtractDocumentScoped(firstChunk.Metadata))
                }
            };

            var pointId = CreatePointId(documentId);

            points.Add(new PointStruct
            {
                Id = pointId,
                Vectors = new float[] { 0.0f }, // Dummy vector for documents collection
                Payload = { payload }
            });
        }

        if (points.Count > 0)
        {
            await client.UpsertAsync(
                DocumentsCollectionName,
                points,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Creates a Document from Qdrant payload data.
    /// </summary>
    /// <param name="pointId">The point ID.</param>
    /// <param name="payload">The payload dictionary.</param>
    /// <returns>A Document populated with the payload data.</returns>
    private static Document CreateDocumentFromPayload(PointId pointId, IDictionary<string, Value> payload)
    {
        var id = payload.TryGetValue("Id", out var idValue) ? idValue.StringValue : (pointId.Uuid ?? pointId.Num.ToString());
        var name = payload.TryGetValue("Name", out var nameValue) ? nameValue.StringValue : id;
        var sourcePath = payload.TryGetValue("SourcePath", out var sourcePathValue) ? sourcePathValue.StringValue : string.Empty;
        var chunkCount = payload.TryGetValue("ChunkCount", out var chunkCountValue) ? (int)chunkCountValue.IntegerValue : 0;

        DateTime ingestedAt = DateTime.UtcNow;
        if (payload.TryGetValue("IngestedAt", out var ingestedAtValue) &&
            DateTime.TryParse(ingestedAtValue.StringValue, out var parsedDate))
        {
            ingestedAt = parsedDate;
        }

        var metadata = payload.TryGetValue("Metadata", out var metadataValue)
            ? DocumentMetadataKeys.FromJson(metadataValue.StringValue)
            : new Dictionary<string, object>();

        return new Document
        {
            Id = id,
            Name = name,
            SourcePath = sourcePath,
            ChunkCount = chunkCount,
            IngestedAt = ingestedAt,
            Metadata = metadata
        };
    }
}
