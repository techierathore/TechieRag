using TechieDesk.Services.Scheduling;
using TechieRag.Connectors;
using TechieRag.Connectors.Confluence;
using TechieRag.Connectors.Email;
using TechieRag.Connectors.Http;
using TechieRag.Connectors.Repository;

namespace TechieDesk.Services.Connectors;

/// <summary>
/// The real <see cref="IConnectorResolver"/>: turns a saved connector row plus a credential from the
/// OS store into a live connector, and keeps the sync state that goes with it
/// (REQ-RAG-019 / BRD-63, REQ-RAG-020 / BRD-64).
/// </summary>
/// <remarks>
/// <para><b>This is the piece that made connectors real.</b> REQ-RAG-032 built the connectors and
/// REQ-FN-020 built the job that runs one, but nothing joined them: the shipped resolver honestly
/// reported "no connector types are installed". Everything a connector needs and the library
/// deliberately refuses to own — where the configuration is stored, where the token is stored, what
/// the last run saw — is owned here.</para>
/// <para><b>The credential is read at resolve time and never leaves this method's stack.</b> It is
/// put on the library's options object, which lives for the run, and it is not logged, not put in the
/// payload, not put on <see cref="ConnectorDefinition"/> and not returned to the caller. A connector
/// whose row says it has a credential but whose store no longer holds one FAILS the run with a named
/// reason, rather than quietly reading the source anonymously and reporting an empty private
/// repository as an empty repository.</para>
/// <para><b>The private-network opt-in is passed to both call sites the library requires.</b>
/// <see cref="HttpConnectorTransport.CreateDefaultClient"/> decides at connect time on the resolved
/// address, and the transport's own flag catches an obviously private literal early; the library's
/// default for both is "refuse". A self-hosted GitLab or Confluence therefore works only when the
/// operator turned <see cref="ConnectorSettings.AllowPrivateNetwork"/> on, and it is on by nobody's
/// default.</para>
/// <para><b>Scoped, and it disposes what it created.</b> <see cref="ConnectorJobHandler"/> opens a
/// scope per run; the clients this resolver builds die with it. A connector run holding a process-wide
/// client would have kept a self-hosted host's connection pool alive long after the run.</para>
/// </remarks>
public sealed class DatabaseConnectorResolver : IConnectorResolver, IDisposable
{
    private readonly IConnectorRepository repository;
    private readonly IConnectorSecretStore secretStore;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<DatabaseConnectorResolver> logger;
    private readonly TimeProvider timeProvider;
    private readonly List<HttpClient> clients = [];
    private readonly object gate = new();
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="DatabaseConnectorResolver"/> class.</summary>
    /// <param name="repository">Saved connectors and their sync state.</param>
    /// <param name="secretStore">Where connector access tokens live (REQ-FN-039).</param>
    /// <param name="loggerFactory">Creates the library components' loggers.</param>
    /// <param name="logger">Diagnostics. Never receives a token.</param>
    /// <param name="timeProvider">Clock, so the rate-limit waiter is testable without real waiting.</param>
    public DatabaseConnectorResolver(
        IConnectorRepository repository,
        IConnectorSecretStore secretStore,
        ILoggerFactory loggerFactory,
        ILogger<DatabaseConnectorResolver> logger,
        TimeProvider timeProvider)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public IReadOnlyList<ConnectorTypeDescriptor> AvailableTypes => ConnectorTypes.All;

    /// <inheritdoc />
    /// <remarks>
    /// Checked at save time as well as at run time (BRD-136): a schedule naming a connector that has
    /// been deleted must be refused in the confirm dialog, not at 07:00 three days later. The database
    /// read is dispatched to the thread pool rather than blocked on inline, so a call from the Blazor
    /// dispatcher cannot deadlock on its own synchronization context.
    /// </remarks>
    public JobMessage? Validate(ConnectorJobPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!ConnectorTypes.IsKnown(payload.ConnectorType))
        {
            return JobMessage.Of("ConnectorSettingsUnknownType", payload.ConnectorType);
        }

        var definition = Task
            .Run(() => repository.GetAsync(payload.ConnectorId, CancellationToken.None))
            .GetAwaiter()
            .GetResult();

        if (definition is null)
        {
            return JobMessage.Of("ConnectorNoLongerSavedAddAgain", payload.DisplayName);
        }

        if (!definition.ConnectorType.Equals(payload.ConnectorType, StringComparison.Ordinal))
        {
            return JobMessage.Of(
                "ConnectorTypeMismatch",
                payload.ConnectorType,
                definition.DisplayName,
                definition.ConnectorType);
        }

        return definition.ReadSettings().Validate(definition.ConnectorType);
    }

    /// <inheritdoc />
    public async Task<ResolvedConnector> ResolveAsync(
        ConnectorJobPayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ObjectDisposedException.ThrowIf(disposed, this);

        var definition = await repository
            .GetAsync(payload.ConnectorId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ConnectorSetupException(
                payload.ConnectorType,
                JobMessage.Of("ConnectorNoLongerSaved", payload.DisplayName));

        var settings = definition.ReadSettings();
        var invalid = settings.Validate(definition.ConnectorType);
        if (invalid is not null)
        {
            throw new ConnectorSetupException(definition.ConnectorType, invalid);
        }

        var token = ResolveCredential(definition);
        var connector = Build(definition, settings, token);
        var previous = await repository
            .GetSyncAsync(definition.ConnectorId, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Opened connector {ConnectorId} ({SourceName}); {SyncState}",
            definition.ConnectorId,
            connector.SourceName,
            previous is null
                ? "no previous run, so this is a full read"
                : $"the previous run recorded {previous.ItemVersions.Count} item version(s)");

        return new ResolvedConnector(connector, previous);
    }

    /// <inheritdoc />
    public Task SaveSyncAsync(
        ConnectorJobPayload payload, ConnectorSyncState sync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(sync);

        return repository.SaveSyncAsync(payload.ConnectorId, sync, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (gate)
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }

            clients.Clear();
        }
    }

    /// <summary>Reads this connector's token, failing loudly when it should be there and is not.</summary>
    /// <param name="definition">The saved connector.</param>
    /// <returns>The token, or <see langword="null"/> when the connector reads anonymously.</returns>
    /// <exception cref="ConnectorSetupException">The row says a credential exists and the store has none.</exception>
    private string? ResolveCredential(ConnectorDefinition definition)
    {
        if (!definition.HasCredential)
        {
            return null;
        }

        var token = secretStore.Read(definition.ConnectorId);
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        // Falling back to anonymous here would be the worst possible failure: a private repository
        // would list as empty and the run would report a clean "0 ingested of 0 listed", which reads
        // exactly like a source that has nothing in it.
        //
        // REQ-UI-051: the store's own description used to be interpolated into this sentence.
        // IConnectorSecretStore now returns a resource KEY rather than English, and this message is a
        // ConnectorException whose text travels into the run row's FailureReason — a scheduling-owned
        // string that no connector code resolves — so interpolating the key here would have put
        // "ConnectorCredentialsEncryptedAtRest" in front of an operator. Dropping it loses nothing
        // the operator cannot see: the connector editor states where tokens are kept, localized, in
        // the alert directly above the token field, which is where they are about to re-enter it.
        throw new ConnectorSetupException(
            definition.ConnectorType,
            JobMessage.Of("ConnectorCredentialUnreadable", definition.DisplayName));
    }

    /// <summary>Builds the library connector this definition names.</summary>
    /// <param name="definition">The saved connector.</param>
    /// <param name="settings">Its parsed, already-validated configuration.</param>
    /// <param name="token">The resolved credential, or <see langword="null"/> for anonymous access.</param>
    /// <returns>The live connector.</returns>
    private IDataConnector Build(
        ConnectorDefinition definition, ConnectorSettings settings, string? token)
    {
        // A mailbox is not reached over HTTP, so it is built before the HTTP transport is created —
        // building one would open a client this connector never uses.
        if (definition.ConnectorType == ConnectorTypes.Email)
        {
            return BuildEmail(settings, token);
        }

        var transport = CreateTransport(settings);
        return definition.ConnectorType switch
        {
            ConnectorTypes.Repository => new RepositoryConnector(
                transport,
                settings.ToRepositoryOptions(token),
                loggerFactory.CreateLogger<RepositoryConnector>()),
            ConnectorTypes.Confluence => new ConfluenceConnector(
                transport,
                settings.ToConfluenceOptions(token),
                loggerFactory.CreateLogger<ConfluenceConnector>()),
            _ => throw new ConnectorSetupException(
                definition.ConnectorType,
                JobMessage.Of("ConnectorSettingsUnknownType", definition.ConnectorType)),
        };
    }

    /// <summary>Builds a mail connector over IMAP-with-TLS or a local <c>.mbox</c> archive.</summary>
    /// <param name="settings">The connector's configuration, already validated.</param>
    /// <param name="secret">The mailbox password or OAuth bearer token, or <see langword="null"/> for an archive.</param>
    /// <returns>The live connector (REQ-RAG-049 / BRD-135).</returns>
    /// <remarks>
    /// <para><b>The connection is created per run and always TLS.</b> <c>SocketImapConnection</c>
    /// speaks implicit TLS only; there is no cleartext path to select, and
    /// <see cref="ConnectorSettings.Validate"/> has already refused a cleartext port by the time this
    /// runs. The factory is a delegate because <c>ImapMailTransport</c> reconnects per folder.</para>
    /// <para><b>No attachment processors are passed.</b> The library only consults them when
    /// <c>IncludeAttachments</c> is set, and wiring the app's processor set through here is the
    /// remaining half of attachment ingestion — the body path is complete without it, and passing an
    /// empty set makes an attachment-enabled connector ingest bodies rather than fail.</para>
    /// </remarks>
    private IDataConnector BuildEmail(ConnectorSettings settings, string? secret)
    {
        var options = settings.ToEmailOptions();

        if (settings.IsMboxMailbox)
        {
            return new EmailConnector(
                new MboxMailTransport(settings.MboxPath!),
                options,
                attachmentProcessors: null,
                loggerFactory.CreateLogger<EmailConnector>());
        }

        var mailbox = settings.ToImapOptions(secret);
        var transport = new ImapMailTransport(
            () => new SocketImapConnection(mailbox.Host, mailbox.Port, mailbox.Timeout),
            mailbox,
            loggerFactory.CreateLogger<ImapMailTransport>());

        return new EmailConnector(
            transport, options, attachmentProcessors: null, loggerFactory.CreateLogger<EmailConnector>());
    }

    /// <summary>Builds the network seam for one run, with the private-network decision applied twice.</summary>
    /// <param name="settings">The connector's configuration.</param>
    /// <returns>A rate-limit-aware transport.</returns>
    /// <remarks>
    /// Both <see cref="HttpConnectorTransport.CreateDefaultClient"/> and the transport constructor
    /// take the flag, and the library is explicit that passing it to only one of them leaves a hole:
    /// the client's guarded handler is the check that actually holds (it decides on the RESOLVED
    /// address, on every redirect hop), while the transport's own check is the cheap literal fast path
    /// that produces a message an operator can act on.
    /// </remarks>
    private IConnectorTransport CreateTransport(ConnectorSettings settings)
    {
        var blockPrivateTargets = !settings.AllowPrivateNetwork;
        var client = HttpConnectorTransport.CreateDefaultClient(blockPrivateTargets);
        lock (gate)
        {
            clients.Add(client);
        }

        if (!blockPrivateTargets)
        {
            logger.LogWarning(
                "This connector is allowed to reach private-network addresses, which the operator "
                + "turned on deliberately. Its credential will be sent to whatever host its base URL "
                + "resolves to");
        }

        var http = new HttpConnectorTransport(
            client, loggerFactory.CreateLogger<HttpConnectorTransport>(), blockPrivateTargets);

        return new RateLimitedTransport(
            http, loggerFactory.CreateLogger<RateLimitedTransport>(), timeProvider);
    }
}
