using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// Persistence for saved connectors, their sync state, and the document each ingested item became
/// (REQ-RAG-019, REQ-RAG-020).
/// </summary>
/// <remarks>
/// <para><b>Three concerns, one repository, on purpose.</b> The three tables share a key and a
/// lifetime: deleting a connector must take its sync state and its item↔document map with it, and
/// nothing ever reads one without the connector it belongs to. Splitting them into three interfaces
/// would mean three constructor dependencies wherever one connector is handled, for no seam anybody
/// substitutes independently.</para>
/// <para>Dapper over the app database, per ADR-005 / BRD-102. EF Core is banned in this repository.</para>
/// </remarks>
public interface IConnectorRepository
{
    /// <summary>Lists every saved connector, newest change first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved connectors.</returns>
    Task<IReadOnlyList<ConnectorDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one saved connector.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connector, or <see langword="null"/> when it has been deleted.</returns>
    Task<ConnectorDefinition?> GetAsync(string connectorId, CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces one saved connector.</summary>
    /// <param name="definition">The connector to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the row is written.</returns>
    Task SaveAsync(ConnectorDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Deletes one saved connector, its sync state and its item↔document map.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the rows are gone.</returns>
    /// <remarks>
    /// The documents the connector ingested are deliberately left in the catalogue. Deleting a
    /// connector says "stop reading this source", not "throw away everything I have read from it",
    /// and a delete that silently removed months of indexed prose is not recoverable.
    /// </remarks>
    Task DeleteAsync(string connectorId, CancellationToken cancellationToken = default);

    /// <summary>Reads what this connector's previous run saw.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored state, or <see langword="null"/> when the connector has never run.</returns>
    Task<ConnectorSyncState?> GetSyncAsync(string connectorId, CancellationToken cancellationToken = default);

    /// <summary>Stores what a run saw, replacing whatever was there.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <param name="sync">The state to keep.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    Task SaveSyncAsync(
        string connectorId, ConnectorSyncState sync, CancellationToken cancellationToken = default);
}
