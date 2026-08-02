using Microsoft.Extensions.Options;

namespace TechieDesk.Services.Licensing;

/// <summary>
/// Default <see cref="IInstanceModeService"/> (REQ-FN-044/045). Thin by design: it asks
/// <see cref="ILicenseService"/> for the licence status — which already handles validation,
/// persistence through <c>ILicenseCacheRepository</c> and the BRD-51 grace window — and hands it
/// to the pure <see cref="InstanceModeResolver"/>.
/// <para>
/// <b>Fail-open, always.</b> Every failure mode — an exception from the licence service, a
/// resolver fault, a malformed tier — is caught and answered with
/// <see cref="InstanceModeStatus.Individual"/>. There is no code path through this class that
/// can produce a locked, read-only or unusable state, because BRD-129 forbids one.
/// </para>
/// </summary>
public sealed class InstanceModeService : IInstanceModeService
{
    private readonly ILicenseService licenseService;
    private readonly LicensingOptions options;
    private readonly ILogger<InstanceModeService> logger;

    /// <summary>Initializes a new instance of the <see cref="InstanceModeService"/> class.</summary>
    /// <param name="licenseService">The licence service supplying the validated/cached status.</param>
    /// <param name="options">Licensing options carrying the tier-name maps.</param>
    /// <param name="logger">Logger.</param>
    public InstanceModeService(
        ILicenseService licenseService,
        IOptions<LicensingOptions> options,
        ILogger<InstanceModeService> logger)
    {
        this.licenseService = licenseService;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public InstanceModeStatus Current { get; private set; } = InstanceModeStatus.Individual;

    /// <inheritdoc />
    public Task<InstanceModeStatus> EnsureFreshAsync(CancellationToken cancellationToken = default)
        => ResolveAsync(licenseService.EnsureFreshAsync, cancellationToken);

    /// <inheritdoc />
    public Task<InstanceModeStatus> RefreshAsync(CancellationToken cancellationToken = default)
        => ResolveAsync(licenseService.ValidateAsync, cancellationToken);

    private async Task<InstanceModeStatus> ResolveAsync(
        Func<CancellationToken, Task<LicenseStatus>> read, CancellationToken cancellationToken)
    {
        try
        {
            var license = await read(cancellationToken).ConfigureAwait(false);
            Current = InstanceModeResolver.Resolve(license, options);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Deliberately broad. Resolving the mode is an entitlement question, and BRD-129 makes
            // the answer to "can this person use their own machine?" unconditional. Anything that
            // goes wrong here degrades to the Individual floor rather than surfacing to the user.
            logger.LogWarning(ex,
                "Instance mode could not be resolved — falling back to full Individual capability");
            Current = InstanceModeStatus.Individual;
        }

        return Current;
    }
}
