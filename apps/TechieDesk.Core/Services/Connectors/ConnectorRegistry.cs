using System.Linq;
using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// Default <see cref="IConnectorRegistry"/>: the row and the credential are written together, or
/// neither is (REQ-RAG-019 / BRD-63, REQ-RAG-020 / BRD-64, REQ-FN-039).
/// </summary>
/// <remarks>
/// <para><b>Order matters and is deliberate.</b> On save the token goes into the credential store
/// FIRST and the row second, so a failure between them leaves an orphaned secret rather than a
/// connector that claims a credential it cannot resolve. On delete the row goes first and the token
/// second, for the mirror-image reason: the worse outcome is a live connector whose secret is gone.
/// </para>
/// <para><b>It never returns a token and has no method that could.</b> The hub reads a connector to
/// populate an edit form; the token field on that form starts empty and stays empty unless the
/// operator types a new one. There is no "show me the saved token" path, because there is no product
/// reason for one and every reason against.</para>
/// </remarks>
public sealed class ConnectorRegistry : IConnectorRegistry
{
    private readonly IConnectorRepository repository;
    private readonly IConnectorSecretStore secretStore;
    private readonly IConnectorResolver resolver;
    private readonly ILogger<ConnectorRegistry> logger;

    /// <summary>Initializes a new instance of the <see cref="ConnectorRegistry"/> class.</summary>
    /// <param name="repository">Saved connectors and their sync state.</param>
    /// <param name="secretStore">Where connector access tokens live.</param>
    /// <param name="resolver">Used by <see cref="TestAsync"/> to build the connector it tests.</param>
    /// <param name="logger">Diagnostics. Never receives a token.</param>
    public ConnectorRegistry(
        IConnectorRepository repository,
        IConnectorSecretStore secretStore,
        IConnectorResolver resolver,
        ILogger<ConnectorRegistry> logger)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyList<ConnectorTypeDescriptor> AvailableTypes => resolver.AvailableTypes;

    /// <inheritdoc />
    public bool CredentialsAreDurable => secretStore.IsDurable;

    /// <inheritdoc />
    public string CredentialStorageDescriptionKey => secretStore.StorageDescriptionKey;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConnectorSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var definitions = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var summaries = new List<ConnectorSummary>(definitions.Count);

        foreach (var definition in definitions)
        {
            var sync = await repository
                .GetSyncAsync(definition.ConnectorId, cancellationToken)
                .ConfigureAwait(false);

            summaries.Add(new ConnectorSummary(
                definition.ConnectorId,
                definition.ConnectorType,
                ConnectorTypes.DisplayNameKey(definition.ConnectorType),
                definition.DisplayName,
                definition.ReadSettings(),
                definition.WorkspaceId,
                definition.Pinned,
                definition.HasCredential,
                !definition.HasCredential
                    || !string.IsNullOrWhiteSpace(secretStore.Read(definition.ConnectorId)),
                sync?.LastRunUtc,
                sync?.ItemVersions.Count ?? 0,
                definition.UpdatedUtc));
        }

        return summaries;
    }

    /// <inheritdoc />
    public Task<ConnectorDefinition?> GetAsync(
        string connectorId, CancellationToken cancellationToken = default) =>
        repository.GetAsync(connectorId, cancellationToken);

    /// <inheritdoc />
    public JobMessage? Validate(ConnectorRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        if (!ConnectorTypes.IsKnown(registration.ConnectorType))
        {
            return JobMessage.Of("ConnectorSettingsUnknownType", registration.ConnectorType);
        }

        return string.IsNullOrWhiteSpace(registration.DisplayName)
            ? JobMessage.Of("ConnectorRegistrationNeedsName")
            : registration.Settings.Validate(registration.ConnectorType);
    }

    /// <inheritdoc />
    public async Task<string> SaveAsync(
        ConnectorRegistration registration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registration);

        var invalid = Validate(registration);
        if (invalid is not null)
        {
            throw new ConnectorSetupException(registration.ConnectorType, invalid);
        }

        var connectorId = string.IsNullOrWhiteSpace(registration.ConnectorId)
            ? Guid.NewGuid().ToString("N")
            : registration.ConnectorId;

        var existing = await repository.GetAsync(connectorId, cancellationToken).ConfigureAwait(false);

        // The name carries a UNIQUE constraint ("UcConnectorDisplayName"). Without this check the
        // collision surfaced to the operator as the raw text
        // "SQLite Error 19: 'UNIQUE constraint failed: Connector.DisplayName'" — a database error shown
        // to someone who typed a name that was already taken. Checked here rather than in Validate
        // because Validate is synchronous by contract and reading the table from it would mean
        // blocking on the Blazor dispatcher.
        var wantedName = registration.DisplayName.Trim();
        var all = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        if (all.Any(other =>
                !string.Equals(other.ConnectorId, connectorId, StringComparison.Ordinal)
                && string.Equals(other.DisplayName, wantedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConnectorSetupException(
                registration.ConnectorType,
                JobMessage.Of("ConnectorNameAlreadyTaken", wantedName));
        }

        var now = DateTime.UtcNow;

        // The secret first: an orphaned entry in the credential store is inert, while a row that
        // claims a credential the store never received fails every run with "re-enter the token".
        var credentialRef = ApplyCredential(connectorId, registration, existing);

        await repository.SaveAsync(
            new ConnectorDefinition
            {
                ConnectorId = connectorId,
                ConnectorType = registration.ConnectorType,
                DisplayName = registration.DisplayName.Trim(),
                WorkspaceId = string.IsNullOrWhiteSpace(registration.WorkspaceId)
                    ? null
                    : registration.WorkspaceId,
                Pinned = registration.Pinned,
                Settings = registration.Settings.ToJson(),
                CredentialRef = credentialRef,
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now,
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Saved connector {ConnectorId} ({ConnectorType}) '{DisplayName}'; credential {Credential}",
            connectorId,
            registration.ConnectorType,
            registration.DisplayName,
            credentialRef is null ? "none (anonymous access)" : "held in the OS credential store");

        return connectorId;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string connectorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);

        await repository.DeleteAsync(connectorId, cancellationToken).ConfigureAwait(false);
        secretStore.Delete(connectorId);
        logger.LogInformation(
            "Deleted connector {ConnectorId} and its stored access token; the documents it ingested "
            + "were left in the library",
            connectorId);
    }

    /// <inheritdoc />
    public async Task<ConnectorJobPayload?> CreatePayloadAsync(
        string connectorId, CancellationToken cancellationToken = default)
    {
        var definition = await repository.GetAsync(connectorId, cancellationToken).ConfigureAwait(false);
        return definition?.ToPayload();
    }

    /// <inheritdoc />
    public async Task<JobMessage?> TestAsync(
        string connectorId, CancellationToken cancellationToken = default)
    {
        var payload = await CreatePayloadAsync(connectorId, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return JobMessage.Of("ConnectorNoLongerSavedShort");
        }

        var invalid = resolver.Validate(payload);
        if (invalid is not null)
        {
            return invalid;
        }

        try
        {
            var resolved = await resolver.ResolveAsync(payload, cancellationToken).ConfigureAwait(false);
            var page = await resolved.Connector
                .ListAsync(new ConnectorListRequest(), cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Connector {ConnectorId} reached {SourceName} and listed {Count} item(s)",
                connectorId, resolved.Connector.SourceName, page.Items.Count);
            return null;
        }
        catch (Exception exception) when (exception is ConnectorException or ConnectorSetupException)
        {
            // The library contract is that this message never contains a credential, so it is safe
            // to put in front of the operator — and it is the only thing that tells them WHICH of
            // the URL, the project and the token is wrong. A library message has no code and is
            // carried verbatim; an app-authored refusal keeps its codes (REQ-UI-056).
            logger.LogWarning(
                "Connector {ConnectorId} could not be reached: {Reason}", connectorId, exception.Message);
            return ConnectorSetupException.ReasonFor(exception);
        }
    }

    /// <summary>Applies the registration's credential decision and returns the row's reference.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <param name="registration">What the operator submitted.</param>
    /// <param name="existing">The saved connector, when this is a change rather than an addition.</param>
    /// <returns>The value for <see cref="ConnectorDefinition.CredentialRef"/>, or null for anonymous.</returns>
    /// <remarks>
    /// Three cases, all of them meaningful: null token leaves whatever is stored alone (so editing a
    /// branch does not require re-typing a PAT), an empty token removes it (so a connector can be
    /// moved to anonymous access), and a non-empty token replaces it.
    /// </remarks>
    private string? ApplyCredential(
        string connectorId, ConnectorRegistration registration, ConnectorDefinition? existing)
    {
        if (registration.AccessToken is null)
        {
            return existing?.CredentialRef;
        }

        if (string.IsNullOrWhiteSpace(registration.AccessToken))
        {
            secretStore.Delete(connectorId);
            return null;
        }

        secretStore.Write(connectorId, registration.AccessToken);
        return ConnectorCredentialReference(connectorId);
    }

    /// <summary>Builds the opaque reference stored on the row in place of the credential.</summary>
    /// <param name="connectorId">The connector key.</param>
    /// <returns>A name, never a value.</returns>
    private static string ConnectorCredentialReference(string connectorId) =>
        $"secret:connector:{connectorId}";

}
