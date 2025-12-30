using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace TechieRagWeb.Services;

#region Models

/// <summary>
/// Information about the Qdrant cluster.
/// </summary>
public record QdrantClusterInfo(string Version, long TotalCollections, string Status);

/// <summary>
/// Summary information about a collection.
/// </summary>
public record CollectionInfo(string Name, long VectorCount, long PointCount);

/// <summary>
/// Detailed information about a collection.
/// </summary>
public record CollectionDetailInfo(
    string Name,
    long PointCount,
    int VectorSize,
    string DistanceMetric,
    string Status,
    long SegmentsCount,
    long IndexedVectorsCount,
    Dictionary<string, string> Config);

/// <summary>
/// A page of vectors for browsing.
/// </summary>
public record VectorPage(IReadOnlyList<VectorSummary> Vectors, long TotalCount, int Offset, int Limit);

/// <summary>
/// Summary of a vector for list display.
/// </summary>
public record VectorSummary(string Id, string? ChunkText, string? DocumentName, float? Score);

/// <summary>
/// Full details of a vector.
/// </summary>
public record VectorDetail(
    string Id,
    float[] Vector,
    Dictionary<string, object> Payload,
    string? ChunkText,
    string? DocumentName,
    int? ChunkIndex);

/// <summary>
/// Vector search result with score.
/// </summary>
public record VectorSearchResult(string Id, float Score, string? ChunkText, string? DocumentName);

#endregion

/// <summary>
/// Administrative service for Qdrant vector database.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides full CRUD operations for collections and vectors
/// in Qdrant database, enabling admin UI functionality.</para>
/// <para><b>Code Flow:</b> Injected into QdrantAdmin page. Uses Qdrant.Client for gRPC API.</para>
/// </remarks>
public interface IQdrantAdminService
{
    /// <summary>
    /// Gets the current endpoint host.
    /// </summary>
    string Host { get; }

    /// <summary>
    /// Gets the current endpoint port.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Gets the current API key (masked for display).
    /// </summary>
    string? ApiKeyMasked { get; }

    /// <summary>
    /// Gets the connection string for use in settings.
    /// </summary>
    string ConnectionString { get; }

    /// <summary>
    /// Configures the endpoint dynamically.
    /// </summary>
    /// <param name="host">The host address.</param>
    /// <param name="port">The gRPC port (default 6334).</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    void ConfigureEndpoint(string host, int port, string? apiKey = null);

    /// <summary>
    /// Gets the last connection error message for debugging.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Tests connection to Qdrant server.
    /// </summary>
    Task<bool> TestConnectionAsync();

    /// <summary>
    /// Tests connection to a specific endpoint without changing current config.
    /// </summary>
    Task<bool> TestConnectionAsync(string host, int port);

    /// <summary>
    /// Gets cluster information.
    /// </summary>
    Task<QdrantClusterInfo> GetClusterInfoAsync();

    /// <summary>
    /// Lists all collections with summary info.
    /// </summary>
    Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync();

    /// <summary>
    /// Gets detailed information about a collection.
    /// </summary>
    Task<CollectionDetailInfo> GetCollectionInfoAsync(string collectionName);

    /// <summary>
    /// Creates a new collection.
    /// </summary>
    Task CreateCollectionAsync(string name, int vectorSize, string distance = "Cosine");

    /// <summary>
    /// Deletes a collection.
    /// </summary>
    Task DeleteCollectionAsync(string collectionName);

    /// <summary>
    /// Checks if a collection exists.
    /// </summary>
    Task<bool> CollectionExistsAsync(string collectionName);

    /// <summary>
    /// Browses vectors with pagination.
    /// </summary>
    Task<VectorPage> BrowseVectorsAsync(string collectionName, int offset = 0, int limit = 20);

    /// <summary>
    /// Gets a vector by its ID.
    /// </summary>
    Task<VectorDetail?> GetVectorByIdAsync(string collectionName, string pointId);

    /// <summary>
    /// Searches vectors using a query vector.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchVectorsAsync(string collectionName, float[] queryVector, int topK = 10);

    /// <summary>
    /// Deletes a single vector.
    /// </summary>
    Task DeleteVectorAsync(string collectionName, string pointId);

    /// <summary>
    /// Deletes multiple vectors.
    /// </summary>
    Task DeleteVectorsAsync(string collectionName, IEnumerable<string> pointIds);

    /// <summary>
    /// Updates a vector's payload.
    /// </summary>
    Task UpdateVectorPayloadAsync(string collectionName, string pointId, Dictionary<string, object> payload);
}

/// <summary>
/// Implementation of Qdrant admin service using Qdrant.Client.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides administrative access to Qdrant database.</para>
/// <para><b>Code Flow:</b> Creates QdrantClient on construction, methods call gRPC API.</para>
/// </remarks>
public class QdrantAdminService : IQdrantAdminService
{
    private readonly ILogger<QdrantAdminService> logger;
    private string currentHost;
    private int currentPort;
    private string? currentApiKey;
    private string? lastError;

    /// <inheritdoc/>
    public string Host => currentHost;

    /// <inheritdoc/>
    public int Port => currentPort;

    /// <inheritdoc/>
    public string? ApiKeyMasked => string.IsNullOrEmpty(currentApiKey)
        ? null
        : currentApiKey.Length <= 8
            ? "****"
            : $"{currentApiKey[..4]}...{currentApiKey[^4..]}";

    /// <inheritdoc/>
    public string ConnectionString => string.IsNullOrEmpty(currentApiKey)
        ? $"grpc://{currentHost}:{currentPort}"
        : $"grpc://{currentHost}:{currentPort}?apiKey={ApiKeyMasked}";

    /// <inheritdoc/>
    public string? LastError => lastError;

    /// <summary>
    /// Creates a new Qdrant admin service.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="configuration">Configuration for endpoint settings.</param>
    public QdrantAdminService(ILogger<QdrantAdminService> logger, IConfiguration configuration)
    {
        this.logger = logger;

        // Read endpoint from configuration, default to localhost
        var endpoint = configuration["Qdrant:Endpoint"] ?? "http://localhost:6334";
        var uri = new Uri(endpoint);
        currentHost = uri.Host;
        currentPort = uri.Port > 0 ? uri.Port : 6334;
        currentApiKey = configuration["Qdrant:ApiKey"];

        logger.LogInformation("QdrantAdminService initialized with endpoint: {Host}:{Port}, ApiKey: {HasKey}",
            currentHost, currentPort, !string.IsNullOrEmpty(currentApiKey));
    }

    /// <inheritdoc/>
    public void ConfigureEndpoint(string host, int port, string? apiKey = null)
    {
        currentHost = host;
        currentPort = port;
        currentApiKey = apiKey;
        logger.LogInformation("QdrantAdminService endpoint changed to: {Host}:{Port}, ApiKey: {HasKey}",
            currentHost, currentPort, !string.IsNullOrEmpty(currentApiKey));
    }

    private QdrantClient CreateClient()
    {
        logger.LogDebug("Creating QdrantClient for {Host}:{Port} (https=false, apiKey={HasKey})",
            currentHost, currentPort, !string.IsNullOrEmpty(currentApiKey));
        return new QdrantClient(currentHost, currentPort, https: false, apiKey: currentApiKey);
    }

    private QdrantClient CreateClient(string host, int port, string? apiKey = null)
    {
        logger.LogDebug("Creating QdrantClient for {Host}:{Port} (https=false, apiKey={HasKey})",
            host, port, !string.IsNullOrEmpty(apiKey));
        return new QdrantClient(host, port, https: false, apiKey: apiKey);
    }

    /// <inheritdoc/>
    public async Task<bool> TestConnectionAsync()
    {
        logger.LogInformation("Testing Qdrant connection to {Host}:{Port}", currentHost, currentPort);
        lastError = null;
        try
        {
            using var client = CreateClient();
            var collections = await client.ListCollectionsAsync();
            logger.LogInformation("Qdrant connection successful. Found {Count} collections", collections.Count);
            return true;
        }
        catch (Grpc.Core.RpcException rpcEx)
        {
            lastError = $"gRPC Error: {rpcEx.StatusCode} - {rpcEx.Status.Detail}";
            logger.LogError(rpcEx, "Qdrant gRPC connection failed to {Host}:{Port}. Status: {Status}, Detail: {Detail}",
                currentHost, currentPort, rpcEx.StatusCode, rpcEx.Status.Detail);
            return false;
        }
        catch (Exception ex)
        {
            lastError = $"{ex.GetType().Name}: {ex.Message}";
            logger.LogError(ex, "Qdrant connection test failed to {Host}:{Port}. Exception type: {ExType}, Message: {Message}",
                currentHost, currentPort, ex.GetType().Name, ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TestConnectionAsync(string host, int port)
    {
        logger.LogInformation("Testing Qdrant connection to {Host}:{Port}", host, port);
        lastError = null;
        try
        {
            using var client = CreateClient(host, port);
            var collections = await client.ListCollectionsAsync();
            logger.LogInformation("Qdrant connection successful to {Host}:{Port}. Found {Count} collections", host, port, collections.Count);
            return true;
        }
        catch (Grpc.Core.RpcException rpcEx)
        {
            lastError = $"gRPC Error: {rpcEx.StatusCode} - {rpcEx.Status.Detail}";
            logger.LogError(rpcEx, "Qdrant gRPC connection failed to {Host}:{Port}. Status: {Status}, Detail: {Detail}",
                host, port, rpcEx.StatusCode, rpcEx.Status.Detail);
            return false;
        }
        catch (Exception ex)
        {
            lastError = $"{ex.GetType().Name}: {ex.Message}";
            logger.LogError(ex, "Qdrant connection test failed for {Host}:{Port}. Exception type: {ExType}, Message: {Message}",
                host, port, ex.GetType().Name, ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<QdrantClusterInfo> GetClusterInfoAsync()
    {
        try
        {
            using var client = CreateClient();
            var collections = await client.ListCollectionsAsync();

            return new QdrantClusterInfo(
                Version: "1.12.x",
                TotalCollections: collections.Count,
                Status: "Connected"
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get cluster info");
            return new QdrantClusterInfo("Unknown", 0, "Error");
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CollectionInfo>> ListCollectionsAsync()
    {
        using var client = CreateClient();
        var collections = await client.ListCollectionsAsync();
        var result = new List<CollectionInfo>();

        foreach (var name in collections)
        {
            try
            {
                var info = await client.GetCollectionInfoAsync(name);
                result.Add(new CollectionInfo(
                    Name: name,
                    VectorCount: (long)info.VectorsCount,
                    PointCount: (long)info.PointsCount
                ));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get info for collection: {Collection}", name);
                result.Add(new CollectionInfo(name, 0, 0));
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<CollectionDetailInfo> GetCollectionInfoAsync(string collectionName)
    {
        using var client = CreateClient();
        var info = await client.GetCollectionInfoAsync(collectionName);

        var vectorSize = 0;
        var distance = "Unknown";

        if (info.Config?.Params?.VectorsConfig?.Params != null)
        {
            vectorSize = (int)info.Config.Params.VectorsConfig.Params.Size;
            distance = info.Config.Params.VectorsConfig.Params.Distance.ToString();
        }

        return new CollectionDetailInfo(
            Name: collectionName,
            PointCount: (long)info.PointsCount,
            VectorSize: vectorSize,
            DistanceMetric: distance,
            Status: info.Status.ToString(),
            SegmentsCount: (long)info.SegmentsCount,
            IndexedVectorsCount: (long)info.IndexedVectorsCount,
            Config: new Dictionary<string, string>
            {
                ["Status"] = info.Status.ToString(),
                ["OptimizerStatus"] = info.OptimizerStatus?.Ok.ToString() ?? "Unknown"
            }
        );
    }

    /// <inheritdoc/>
    public async Task CreateCollectionAsync(string name, int vectorSize, string distance = "Cosine")
    {
        using var client = CreateClient();

        var distanceType = distance.ToLowerInvariant() switch
        {
            "cosine" => Distance.Cosine,
            "euclid" or "euclidean" => Distance.Euclid,
            "dot" => Distance.Dot,
            _ => Distance.Cosine
        };

        await client.CreateCollectionAsync(name, new VectorParams
        {
            Size = (ulong)vectorSize,
            Distance = distanceType
        });

        logger.LogInformation("Created collection: {Collection} with size {Size} and distance {Distance}",
            name, vectorSize, distance);
    }

    /// <inheritdoc/>
    public async Task DeleteCollectionAsync(string collectionName)
    {
        using var client = CreateClient();
        await client.DeleteCollectionAsync(collectionName);
        logger.LogInformation("Deleted collection: {Collection}", collectionName);
    }

    /// <inheritdoc/>
    public async Task<bool> CollectionExistsAsync(string collectionName)
    {
        using var client = CreateClient();
        return await client.CollectionExistsAsync(collectionName);
    }

    /// <inheritdoc/>
    public async Task<VectorPage> BrowseVectorsAsync(string collectionName, int offset = 0, int limit = 20)
    {
        using var client = CreateClient();

        // Get collection info for total count
        var info = await client.GetCollectionInfoAsync(collectionName);
        var totalCount = (long)info.PointsCount;

        // Scroll through points
        PointId? offsetId = offset > 0 ? new PointId { Num = (ulong)offset } : null;
        var scrollResult = await client.ScrollAsync(
            collectionName,
            limit: (uint)limit,
            offset: offsetId,
            payloadSelector: true
        );

        var vectors = scrollResult.Result.Select(p => new VectorSummary(
            Id: p.Id.HasNum ? p.Id.Num.ToString() : p.Id.Uuid,
            ChunkText: GetPayloadString(p.Payload, "Text", "ChunkText", "text"),
            DocumentName: GetPayloadString(p.Payload, "DocumentName", "DocumentId", "SourceFile"),
            Score: null
        )).ToList();

        return new VectorPage(vectors, totalCount, offset, limit);
    }

    /// <inheritdoc/>
    public async Task<VectorDetail?> GetVectorByIdAsync(string collectionName, string pointId)
    {
        using var client = CreateClient();

        var pointIdValue = ulong.TryParse(pointId, out var numId)
            ? new PointId { Num = numId }
            : new PointId { Uuid = pointId };

        var ids = new List<PointId> { pointIdValue };
        var points = await client.RetrieveAsync(collectionName, ids, withPayload: true, withVectors: true);
        var point = points.FirstOrDefault();

        if (point == null) return null;

        var payload = ConvertPayload(point.Payload);
        var vector = point.Vectors?.Vector?.Data?.ToArray() ?? Array.Empty<float>();

        return new VectorDetail(
            Id: pointId,
            Vector: vector,
            Payload: payload,
            ChunkText: GetPayloadString(point.Payload, "Text", "ChunkText", "text"),
            DocumentName: GetPayloadString(point.Payload, "DocumentName", "DocumentId", "SourceFile"),
            ChunkIndex: GetPayloadInt(point.Payload, "ChunkIndex", "chunkIndex")
        );
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchVectorsAsync(string collectionName, float[] queryVector, int topK = 10)
    {
        using var client = CreateClient();

        var results = await client.SearchAsync(collectionName, queryVector, limit: (ulong)topK, payloadSelector: true);

        return results.Select(r => new VectorSearchResult(
            Id: r.Id.HasNum ? r.Id.Num.ToString() : r.Id.Uuid,
            Score: r.Score,
            ChunkText: GetPayloadString(r.Payload, "Text", "ChunkText", "text"),
            DocumentName: GetPayloadString(r.Payload, "DocumentName", "DocumentId", "SourceFile")
        )).ToList();
    }

    /// <inheritdoc/>
    public async Task DeleteVectorAsync(string collectionName, string pointId)
    {
        using var client = CreateClient();

        var id = ulong.TryParse(pointId, out var numId) ? numId : 0UL;
        if (id == 0)
        {
            // UUID-based ID - use filter
            await client.DeleteAsync(collectionName, new Filter
            {
                Must = { new Condition { HasId = new HasIdCondition { HasId = { new PointId { Uuid = pointId } } } } }
            });
        }
        else
        {
            await client.DeleteAsync(collectionName, id);
        }
        logger.LogInformation("Deleted vector {PointId} from {Collection}", pointId, collectionName);
    }

    /// <inheritdoc/>
    public async Task DeleteVectorsAsync(string collectionName, IEnumerable<string> pointIds)
    {
        using var client = CreateClient();

        var numericIds = new List<ulong>();
        var uuidIds = new List<PointId>();

        foreach (var id in pointIds)
        {
            if (ulong.TryParse(id, out var numId))
            {
                numericIds.Add(numId);
            }
            else
            {
                uuidIds.Add(new PointId { Uuid = id });
            }
        }

        if (numericIds.Count > 0)
        {
            await client.DeleteAsync(collectionName, numericIds);
        }

        if (uuidIds.Count > 0)
        {
            await client.DeleteAsync(collectionName, new Filter
            {
                Must = { new Condition { HasId = new HasIdCondition { HasId = { uuidIds } } } }
            });
        }

        logger.LogInformation("Deleted {Count} vectors from {Collection}", numericIds.Count + uuidIds.Count, collectionName);
    }

    /// <inheritdoc/>
    public async Task UpdateVectorPayloadAsync(string collectionName, string pointId, Dictionary<string, object> payload)
    {
        using var client = CreateClient();

        var qdrantPayload = payload.ToDictionary(
            kvp => kvp.Key,
            kvp => ConvertToValue(kvp.Value)
        );

        // Use SetPayloadAsync with Guid or ulong ID
        if (ulong.TryParse(pointId, out var numId))
        {
            await client.SetPayloadAsync(collectionName, qdrantPayload, numId);
        }
        else if (Guid.TryParse(pointId, out var guidId))
        {
            await client.SetPayloadAsync(collectionName, qdrantPayload, guidId);
        }
        else
        {
            // Generate deterministic GUID from string
            using var md5 = System.Security.Cryptography.MD5.Create();
            var inputBytes = System.Text.Encoding.UTF8.GetBytes(pointId);
            var hashBytes = md5.ComputeHash(inputBytes);
            var deterministicGuid = new Guid(hashBytes);
            await client.SetPayloadAsync(collectionName, qdrantPayload, deterministicGuid);
        }

        logger.LogInformation("Updated payload for vector {PointId} in {Collection}", pointId, collectionName);
    }

    #region Helper Methods

    private static string? GetPayloadString(IDictionary<string, Value>? payload, params string[] keys)
    {
        if (payload == null) return null;

        foreach (var key in keys)
        {
            if (payload.TryGetValue(key, out var value) && value.HasStringValue)
            {
                return value.StringValue;
            }
        }
        return null;
    }

    private static int? GetPayloadInt(IDictionary<string, Value>? payload, params string[] keys)
    {
        if (payload == null) return null;

        foreach (var key in keys)
        {
            if (payload.TryGetValue(key, out var value) && value.HasIntegerValue)
            {
                return (int)value.IntegerValue;
            }
        }
        return null;
    }

    private static Dictionary<string, object> ConvertPayload(IDictionary<string, Value>? payload)
    {
        if (payload == null) return new Dictionary<string, object>();

        return payload.ToDictionary(
            kvp => kvp.Key,
            kvp => ConvertFromValue(kvp.Value)
        );
    }

    private static object ConvertFromValue(Value value)
    {
        if (value.HasStringValue) return value.StringValue;
        if (value.HasIntegerValue) return value.IntegerValue;
        if (value.HasDoubleValue) return value.DoubleValue;
        if (value.HasBoolValue) return value.BoolValue;
        return value.ToString() ?? "";
    }

    private static Value ConvertToValue(object obj)
    {
        return obj switch
        {
            string s => new Value { StringValue = s },
            int i => new Value { IntegerValue = i },
            long l => new Value { IntegerValue = l },
            double d => new Value { DoubleValue = d },
            float f => new Value { DoubleValue = f },
            bool b => new Value { BoolValue = b },
            _ => new Value { StringValue = obj.ToString() ?? "" }
        };
    }

    #endregion
}
