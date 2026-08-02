namespace TechieDesk.Services.Install;

/// <summary>
/// Default <see cref="IInstallIdentityProvider"/>: <see cref="InstallIdentityStore"/> bound to one
/// data directory and one machine-fingerprint source, resolved once per process.
/// </summary>
/// <remarks>
/// The data directory is passed in rather than resolved here. REQ-FN-034/037 made
/// <c>DataDirectory</c> the single authority for where state lives and the defect class it closed
/// was exactly "a second component held its own opinion"; the composition root already knows the
/// answer, so this type takes it.
/// </remarks>
public sealed class InstallIdentityProvider : IInstallIdentityProvider
{
    private readonly string dataDirectory;
    private readonly IMachineFingerprintProvider fingerprintProvider;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<InstallIdentityProvider> logger;
    private readonly object gate = new();

    private InstallIdentity? cached;

    /// <summary>Initializes a new instance of the <see cref="InstallIdentityProvider"/> class.</summary>
    /// <param name="dataDirectory">The absolute data directory this install is scoped to.</param>
    /// <param name="fingerprintProvider">Source of the machine-derived half of the identity.</param>
    /// <param name="timeProvider">Clock used to stamp a newly minted identity.</param>
    /// <param name="logger">Logger.</param>
    public InstallIdentityProvider(
        string dataDirectory,
        IMachineFingerprintProvider fingerprintProvider,
        TimeProvider timeProvider,
        ILogger<InstallIdentityProvider> logger)
    {
        this.dataDirectory = dataDirectory;
        this.fingerprintProvider = fingerprintProvider;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    public InstallIdentity Current
    {
        get
        {
            lock (gate)
            {
                cached ??= InstallIdentityStore.Load(
                    dataDirectory, fingerprintProvider.Get(), timeProvider, logger);
                return cached;
            }
        }
    }
}
