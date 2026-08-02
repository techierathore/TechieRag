namespace TechieDesk.Services.Connectors;

/// <summary>
/// One saved connector as the connector hub lists it — no credential, no JSON
/// (REQ-RAG-019, REQ-RAG-020).
/// </summary>
/// <param name="ConnectorId">The connector key.</param>
/// <param name="ConnectorType">The kind of source — see <see cref="ConnectorTypes"/>.</param>
/// <param name="TypeNameKey">
/// Resource key naming that kind for a grid badge, or <see langword="null"/> when this build does not
/// know the type — in which case the honest badge is <paramref name="ConnectorType"/> itself.
/// </param>
/// <param name="DisplayName">The operator-facing name of this connector. User data, never translated.</param>
/// <param name="Settings">
/// The connector's parsed, non-secret configuration. The hub renders its one-line summary through
/// <see cref="ConnectorSettings.Describe(string, Localization.LocalizeText)"/>.
/// </param>
/// <param name="WorkspaceId">The workspace its documents are linked into, or <see langword="null"/>.</param>
/// <param name="Pinned">Whether its documents are pinned into workspace context.</param>
/// <param name="HasCredential">Whether a token is expected for this connector.</param>
/// <param name="CredentialResolves">
/// Whether that token can actually be read on this machine right now. False with
/// <paramref name="HasCredential"/> true is the case the hub must surface: the connector will fail on
/// its next run, and it will fail at 07:00 rather than while somebody is watching.
/// </param>
/// <param name="LastSyncUtc">When the connector last recorded sync state, or <see langword="null"/>.</param>
/// <param name="KnownItemCount">How many items the previous run tracked versions for.</param>
/// <param name="UpdatedUtc">When the connector's configuration last changed.</param>
/// <remarks>
/// REQ-UI-051 / BRD-91: this row carries no English. The type badge is a resource KEY and the
/// one-line summary is deferred to <see cref="ConnectorSettings.Describe(string, Localization.LocalizeText)"/>,
/// which the hub calls with its own localizer — a service that builds this list has no business
/// deciding what language the reader wants.
/// </remarks>
public sealed record ConnectorSummary(
    string ConnectorId,
    string ConnectorType,
    string? TypeNameKey,
    string DisplayName,
    ConnectorSettings Settings,
    string? WorkspaceId,
    bool Pinned,
    bool HasCredential,
    bool CredentialResolves,
    DateTimeOffset? LastSyncUtc,
    int KnownItemCount,
    DateTime UpdatedUtc);

/// <summary>
/// What the connector hub submits to add or change a connector (REQ-RAG-019, REQ-RAG-020).
/// </summary>
/// <remarks>
/// <para><b><see cref="AccessToken"/> is the only credential-bearing field in this cluster, and it is
/// on the REQUEST, not on anything stored.</b> It travels from the form into
/// <see cref="IConnectorSecretStore"/> and is never written to the row, the settings JSON, a log line
/// or an exception. Null means "leave whatever is stored alone", so re-saving a connector's branch
/// does not require the operator to re-type a personal access token; an empty string means "remove
/// the stored token and read anonymously from now on".</para>
/// </remarks>
public sealed record ConnectorRegistration
{
    /// <summary>Gets the connector to change, or <see langword="null"/> to add a new one.</summary>
    public string? ConnectorId { get; init; }

    /// <summary>Gets the kind of source — see <see cref="ConnectorTypes"/>.</summary>
    public string ConnectorType { get; init; } = string.Empty;

    /// <summary>Gets the operator-facing name.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the workspace ingested documents are linked into, or <see langword="null"/>.</summary>
    public string? WorkspaceId { get; init; }

    /// <summary>Gets a value indicating whether ingested documents are pinned into workspace context.</summary>
    public bool Pinned { get; init; }

    /// <summary>Gets the connector-specific configuration.</summary>
    public ConnectorSettings Settings { get; init; } = new();

    /// <summary>
    /// Gets the access token to store, <see langword="null"/> to leave the stored one alone, or an
    /// empty string to remove it.
    /// </summary>
    public string? AccessToken { get; init; }
}

/// <summary>
/// Saving, listing and testing connectors — everything the connector hub needs that is not "run one"
/// (REQ-RAG-019 / BRD-63, REQ-RAG-020 / BRD-64).
/// </summary>
/// <remarks>
/// <para>Sits above <see cref="IConnectorRepository"/> and <see cref="IConnectorSecretStore"/> because
/// the rules it enforces span both: a connector's credential and its row are written together, and
/// deleting a connector must not leave its token in the OS store.</para>
/// <para><b>It is deliberately separate from <see cref="IConnectorJobService"/>.</b> That one starts
/// and watches runs; this one owns what a connector IS. Merging them would give the connector screen
/// one service that both writes credentials and reports live progress, and it would make the job
/// cluster depend on the storage decisions it was designed not to know about.</para>
/// </remarks>
public interface IConnectorRegistry
{
    /// <summary>Gets the connector types this build can save.</summary>
    IReadOnlyList<ConnectorTypeDescriptor> AvailableTypes { get; }

    /// <summary>Gets a value indicating whether a saved token survives a restart on this machine.</summary>
    /// <remarks>
    /// False must be shown, not hidden. It is the difference between "your connector is configured"
    /// and "your connector is configured until you close the app".
    /// </remarks>
    bool CredentialsAreDurable { get; }

    /// <summary>
    /// Gets the RESOURCE KEY of the operator-facing description of where connector tokens are kept.
    /// </summary>
    /// <remarks>
    /// REQ-UI-051 / BRD-91: the connector hub and the connector editor interpolate this into four
    /// otherwise localized alerts. It is a key, so those alerts cannot be half-Hindi.
    /// </remarks>
    string CredentialStorageDescriptionKey { get; }

    /// <summary>Lists every saved connector.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved connectors, newest change first.</returns>
    Task<IReadOnlyList<ConnectorSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one saved connector's stored configuration, for an edit form.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connector, or <see langword="null"/> when it has been deleted.</returns>
    Task<ConnectorDefinition?> GetAsync(
        string connectorId, CancellationToken cancellationToken = default);

    /// <summary>Checks a registration before anything is written.</summary>
    /// <param name="registration">What the operator submitted.</param>
    /// <returns><see langword="null"/> when it is usable, otherwise the reason it is not.</returns>
    string? Validate(ConnectorRegistration registration);

    /// <summary>Adds or changes a connector, storing its token in the OS credential store.</summary>
    /// <param name="registration">What the operator submitted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The connector key.</returns>
    /// <exception cref="TechieRag.Connectors.ConnectorException">The registration is not usable.</exception>
    Task<string> SaveAsync(
        ConnectorRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>Deletes a connector, its sync state and its stored token.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when it is gone.</returns>
    /// <remarks>The documents it already ingested stay in the catalogue; see <see cref="IConnectorRepository.DeleteAsync"/>.</remarks>
    Task DeleteAsync(string connectorId, CancellationToken cancellationToken = default);

    /// <summary>Builds the payload a hand-started run of this connector uses.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The payload, or <see langword="null"/> when the connector has been deleted.</returns>
    Task<ConnectorJobPayload?> CreatePayloadAsync(
        string connectorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Proves the connector can actually reach its source, by listing its first page.
    /// </summary>
    /// <param name="connectorId">The connector key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="null"/> when it worked, otherwise the reason it did not.</returns>
    /// <remarks>
    /// Lists, and does not fetch or ingest. A "Test" button that ingested would make trying a
    /// configuration a destructive act; listing is the cheapest call that still proves the base URL,
    /// the project or space, and the credential are all right.
    /// </remarks>
    Task<string?> TestAsync(string connectorId, CancellationToken cancellationToken = default);
}
