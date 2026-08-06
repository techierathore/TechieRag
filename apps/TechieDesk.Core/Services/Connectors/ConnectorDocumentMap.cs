using Dapper;
using TechieDesk.Services.Data;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// Dapper implementation of <see cref="IConnectorDocumentMap"/> over the <c>ConnectorItemDocument</c>
/// table (REQ-RAG-019, REQ-RAG-020, ADR-005).
/// </summary>
/// <remarks>
/// <para><b>It must survive a restart, which is why it is a table and not a dictionary.</b> The whole
/// point of the mapping is the SECOND run — and the second run is usually the next morning, in a
/// process that was not alive for the first one. An in-memory map would have been correct in every
/// test and wrong in every real installation.</para>
/// </remarks>
public sealed class ConnectorDocumentMap : IConnectorDocumentMap
{
    private readonly IAppDbConnectionFactory connectionFactory;

    /// <summary>Initializes a new instance of the <see cref="ConnectorDocumentMap"/> class.</summary>
    /// <param name="connectionFactory">Provider-agnostic connection factory.</param>
    public ConnectorDocumentMap(IAppDbConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc />
    public async Task<string?> FindDocumentAsync(
        string connectorId, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        const string sql = """
            SELECT "DocumentId" FROM "ConnectorItemDocument"
            WHERE "ConnectorId" = @connectorId AND "ItemId" = @itemId;
            """;
        using var connection = connectionFactory.CreateConnection();
        return await connection
            .QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                sql, new { connectorId, itemId }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        string connectorId,
        string itemId,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        const string sql = """
            INSERT INTO "ConnectorItemDocument"
                ("ConnectorId", "ItemId", "DocumentId", "IngestedUtc")
            VALUES (@connectorId, @itemId, @documentId, @ingestedUtc)
            ON CONFLICT ("ConnectorId", "ItemId") DO UPDATE SET
                "DocumentId"  = excluded."DocumentId",
                "IngestedUtc" = excluded."IngestedUtc";
            """;
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { connectorId, itemId, documentId, ingestedUtc = DateTime.UtcNow },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }
}
