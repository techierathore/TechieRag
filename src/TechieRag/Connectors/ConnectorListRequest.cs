namespace TechieRag.Connectors;

/// <summary>
/// What the runner asks a connector for when it wants the next page of a listing
/// (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// Page size and every source-specific filter live on the connector's own options object rather than
/// here, because the limits differ per host (25, 100, 1000) and a shared knob would either lie about
/// the maximum or force every connector to clamp it.
/// </remarks>
/// <param name="Cursor">Opaque continuation from the previous <see cref="ConnectorPage.NextCursor"/>, or null to start.</param>
/// <param name="PreviousSync">What the last run saw, so the connector can ask the source for changes only. May be ignored.</param>
public sealed record ConnectorListRequest(
    string? Cursor = null,
    ConnectorSyncState? PreviousSync = null);
