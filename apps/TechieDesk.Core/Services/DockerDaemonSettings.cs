using TechieDesk.Services.Data;

namespace TechieDesk.Services;

/// <summary>
/// The persisted Docker daemon configuration (REQ-FN-040 / BRD-134).
/// </summary>
/// <remarks>
/// <para><b>Security seam (REQ-FN-039):</b> <see cref="ClientCertificatePath"/> and
/// <see cref="ClientCertificatePassword"/> are resolved through this one abstraction on purpose.
/// When the OS credential store lands (Keychain / Windows Credential Manager) the *only* thing
/// that changes is where <see cref="IDockerDaemonSettingsStore"/> reads them from — no second
/// secret store is invented here, and the password is never written to the app database.</para>
/// </remarks>
/// <param name="Endpoint">The canonical daemon endpoint, for example <c>tcps://host.lan:2376</c>.</param>
/// <param name="VerifyTls">Whether the daemon's TLS certificate chain is verified. Defaults to true.</param>
/// <param name="ClientCertificatePath">Optional path to a PKCS#12 client certificate.</param>
/// <param name="ClientCertificatePassword">Optional password for the client certificate.</param>
public sealed record DockerDaemonSettings(
    string Endpoint,
    bool VerifyTls = true,
    string? ClientCertificatePath = null,
    string? ClientCertificatePassword = null);

/// <summary>
/// Reads and writes the Docker daemon configuration.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Makes the daemon endpoint a <i>setting</i> rather than a hard-coded local
/// socket, so TechieDesk can administer a daemon on this machine, on the LAN, or on a remote host
/// over TLS.</para>
/// <para><b>Code Flow:</b> <see cref="DockerContainerService"/> loads the settings on first use and
/// saves them whenever the operator applies a new endpoint on <c>/qdrant-admin</c>.</para>
/// </remarks>
public interface IDockerDaemonSettingsStore
{
    /// <summary>
    /// Loads the effective daemon settings — the persisted operator choice when there is one,
    /// otherwise the values supplied by application configuration.
    /// </summary>
    /// <returns>The effective settings.</returns>
    Task<DockerDaemonSettings> LoadAsync();

    /// <summary>
    /// Persists the operator's daemon choice so it survives a restart.
    /// </summary>
    /// <param name="settings">The settings to persist. Secrets are NOT written here.</param>
    Task SaveAsync(DockerDaemonSettings settings);
}

/// <summary>
/// <see cref="IInstanceSettingRepository"/>-backed daemon settings, defaulting to the
/// <c>Docker</c> configuration section (REQ-FN-040).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One place that answers "which daemon are we driving, and how safely".</para>
/// <para><b>Dependencies:</b> Dapper-backed <see cref="IInstanceSettingRepository"/> for the
/// persisted override; <see cref="IConfiguration"/> for the deployment default and for the
/// REQ-FN-039 credential seam.</para>
/// </remarks>
public sealed class DockerDaemonSettingsStore : IDockerDaemonSettingsStore
{
    /// <summary>Instance-setting key holding the operator's chosen daemon endpoint.</summary>
    public const string EndpointSettingKey = "docker.daemon.endpoint";

    /// <summary>Instance-setting key holding the TLS-verification flag.</summary>
    public const string VerifyTlsSettingKey = "docker.daemon.verifyTls";

    /// <summary>Configuration key for the deployment-default daemon endpoint.</summary>
    public const string EndpointConfigKey = "Docker:Endpoint";

    /// <summary>Configuration key for the deployment-default TLS-verification flag.</summary>
    public const string VerifyTlsConfigKey = "Docker:VerifyTls";

    /// <summary>
    /// Configuration key for the client-certificate file. REQ-FN-039 seam: this moves to the OS
    /// credential store, it does not move to a new bespoke secret file.
    /// </summary>
    public const string ClientCertificatePathConfigKey = "Docker:ClientCertificatePath";

    /// <summary>
    /// Configuration key for the client-certificate password. REQ-FN-039 seam — never persisted
    /// to the app database by this store.
    /// </summary>
    public const string ClientCertificatePasswordConfigKey = "Docker:ClientCertificatePassword";

    private readonly IInstanceSettingRepository settings;
    private readonly IConfiguration configuration;
    private readonly ILogger<DockerDaemonSettingsStore> logger;

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="settings">Instance-setting repository holding the persisted override.</param>
    /// <param name="configuration">Application configuration supplying deployment defaults.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public DockerDaemonSettingsStore(
        IInstanceSettingRepository settings,
        IConfiguration configuration,
        ILogger<DockerDaemonSettingsStore> logger)
    {
        this.settings = settings;
        this.configuration = configuration;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task<DockerDaemonSettings> LoadAsync()
    {
        var endpoint = configuration[EndpointConfigKey];
        var verifyTls = !string.Equals(configuration[VerifyTlsConfigKey], "false", StringComparison.OrdinalIgnoreCase);

        try
        {
            var persistedEndpoint = await settings.GetAsync(EndpointSettingKey).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(persistedEndpoint))
            {
                endpoint = persistedEndpoint;
                var persistedVerify = await settings.GetAsync(VerifyTlsSettingKey).ConfigureAwait(false);
                verifyTls = !string.Equals(persistedVerify, "false", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            // A settings-store outage must not silently degrade to "local daemon, no TLS" without
            // saying so — that is exactly the class of misleading fallback this project has been
            // bitten by before.
            logger.LogWarning(ex,
                "Could not read the persisted Docker daemon endpoint; falling back to configuration " +
                "({ConfigEndpoint})", endpoint ?? "local socket");
        }

        return new DockerDaemonSettings(
            Endpoint: string.IsNullOrWhiteSpace(endpoint) ? DockerDaemonEndpoint.Local().Display : endpoint,
            VerifyTls: verifyTls,
            ClientCertificatePath: configuration[ClientCertificatePathConfigKey],
            ClientCertificatePassword: configuration[ClientCertificatePasswordConfigKey]);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(DockerDaemonSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await this.settings.SetAsync(EndpointSettingKey, settings.Endpoint).ConfigureAwait(false);
        await this.settings.SetAsync(VerifyTlsSettingKey, settings.VerifyTls ? "true" : "false")
            .ConfigureAwait(false);

        logger.LogInformation(
            "Docker daemon endpoint saved: {Endpoint} (TLS verification {Verify})",
            settings.Endpoint, settings.VerifyTls ? "on" : "OFF");
    }
}
