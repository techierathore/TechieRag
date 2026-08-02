using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// The <see cref="IConnectorResolver"/> used when no connector types are installed in this build.
/// </summary>
/// <remarks>
/// <para>Registered with <c>TryAddScoped</c>, so a build that ships connectors replaces it simply by
/// registering its own resolver first. Its purpose is that the connector hub, the schedule authoring
/// dialog and the job handler all have something to talk to on a build where the connector cluster is
/// not wired yet — and that what they get back is an honest, named "there are none" rather than a
/// missing-service exception three layers down.</para>
/// <para>It deliberately does not pretend to succeed. <see cref="ResolveAsync"/> throws the same
/// <see cref="ConnectorException"/> a broken connector would, so the run is recorded as failed with a
/// readable reason instead of quietly reporting zero items.</para>
/// </remarks>
public sealed class NoConnectorsResolver : IConnectorResolver
{
    private const string Explanation = "No connector types are installed in this build of TechieDesk.";

    /// <inheritdoc />
    public IReadOnlyList<ConnectorTypeDescriptor> AvailableTypes { get; } = [];

    /// <inheritdoc />
    public string? Validate(ConnectorJobPayload payload) => Explanation;

    /// <inheritdoc />
    public Task<ResolvedConnector> ResolveAsync(
        ConnectorJobPayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        throw new ConnectorException(payload.ConnectorType, Explanation);
    }

    /// <inheritdoc />
    public Task SaveSyncAsync(
        ConnectorJobPayload payload, ConnectorSyncState sync, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
