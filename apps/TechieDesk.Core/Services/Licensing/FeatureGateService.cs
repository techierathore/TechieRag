using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.Auth;

namespace TechieDesk.Services.Licensing;

/// <summary>
/// Default <see cref="IFeatureGate"/>. In AppManager mode it asks FeatureSvc
/// (<c>GET /FeatureSvc/{aFeatureCode}</c>) for a binary/level decision; in offline mode it
/// resolves against the local Free tier (REQ-FN-014). When the license grace window has expired
/// (REQ-FN-015) premium features are denied. Decisions are memoized per circuit.
/// </summary>
public sealed class FeatureGateService : IFeatureGate
{
    private readonly IAppManagerClient appManager;
    private readonly ITechieDeskAuthModeProvider modeProvider;
    private readonly SessionTokenStore tokenStore;
    private readonly ITokenRefresher tokenRefresher;
    private readonly ILicenseService licenseService;
    private readonly LicensingOptions options;
    private readonly ILogger<FeatureGateService> logger;

    private readonly ConcurrentDictionary<string, FeatureDecision> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="FeatureGateService"/> class.</summary>
    public FeatureGateService(
        IAppManagerClient appManager,
        ITechieDeskAuthModeProvider modeProvider,
        SessionTokenStore tokenStore,
        ITokenRefresher tokenRefresher,
        ILicenseService licenseService,
        IOptions<LicensingOptions> options,
        ILogger<FeatureGateService> logger)
    {
        this.appManager = appManager;
        this.modeProvider = modeProvider;
        this.tokenStore = tokenStore;
        this.tokenRefresher = tokenRefresher;
        this.licenseService = licenseService;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string featureCode, CancellationToken cancellationToken = default)
        => (await EvaluateAsync(featureCode, cancellationToken).ConfigureAwait(false)).IsEnabled;

    /// <inheritdoc />
    public async Task<int?> GetLevelAsync(string featureCode, CancellationToken cancellationToken = default)
        => (await EvaluateAsync(featureCode, cancellationToken).ConfigureAwait(false)).Level;

    /// <inheritdoc />
    public async Task<FeatureDecision> EvaluateAsync(string featureCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(featureCode))
        {
            return FeatureDecision.Denied(
                featureCode ?? string.Empty, reasonKey: LicenseMessageKeys.FeatureDeniedNoFeature);
        }

        if (cache.TryGetValue(featureCode, out var cached))
        {
            return cached;
        }

        var decision = await ResolveAsync(featureCode, cancellationToken).ConfigureAwait(false);
        cache[featureCode] = decision;
        return decision;
    }

    private async Task<FeatureDecision> ResolveAsync(string featureCode, CancellationToken cancellationToken)
    {
        // Offline single-user mode: local Free tier. Premium codes are gated; everything else works.
        if (!modeProvider.IsAppManagerEnabled)
        {
            return options.OfflinePremiumFeatures.Contains(featureCode)
                ? FeatureDecision.Denied(featureCode, options.DefaultUpgradeTier,
                    LicenseMessageKeys.FeatureDeniedOfflineTier,
                    [Humanize(featureCode), options.DefaultUpgradeTier])
                : FeatureDecision.Allowed(featureCode);
        }

        // Grace expired: degrade — deny premium features until AppManager is reachable again.
        var licenseStatus = licenseService.Current;
        if (licenseStatus.Availability == LicenseAvailability.GraceExpired)
        {
            return FeatureDecision.Denied(featureCode, options.DefaultUpgradeTier,
                LicenseMessageKeys.FeatureDeniedGraceExpired);
        }

        if (!tokenStore.HasSession)
        {
            return FeatureDecision.Denied(featureCode, reasonKey: LicenseMessageKeys.FeatureDeniedSignIn);
        }

        try
        {
            await tokenRefresher.EnsureValidTokenAsync(cancellationToken).ConfigureAwait(false);
            var access = await appManager
                .CheckFeatureAsync(tokenStore.AccessToken!, featureCode, cancellationToken)
                .ConfigureAwait(false);

            if (access.HasAccess)
            {
                return FeatureDecision.Allowed(featureCode, access.Level);
            }

            var requiredLicense = access.RequiredLicense ?? options.DefaultUpgradeTier;

            // REQ-UI-055: FeatureSvc's own reason is carried through UNTRANSLATED and flagged as
            // the server's, because it is composed at run time by whoever configured the feature
            // and no key can exist for it. FeatureDecision.DescribeReason frames it.
            return string.IsNullOrWhiteSpace(access.Reason)
                ? FeatureDecision.Denied(featureCode, requiredLicense,
                    LicenseMessageKeys.FeatureDeniedUpgradeRequired, [Humanize(featureCode)])
                : FeatureDecision.DeniedByServer(featureCode, requiredLicense, access.Reason);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
            || (ex is AppManagerException am && (am.StatusCode == 0 || am.StatusCode >= 500)))
        {
            // AppManager unreachable: honor a cached/live license within grace, otherwise deny.
            logger.LogWarning(ex, "FeatureSvc unreachable for {FeatureCode} — resolving from license grace state", featureCode);
            return licenseStatus.FeaturesPermitted
                ? FeatureDecision.Allowed(featureCode)
                : FeatureDecision.Denied(featureCode, options.DefaultUpgradeTier,
                    LicenseMessageKeys.FeatureDeniedUnverifiable);
        }
        catch (AppManagerException ex)
        {
            logger.LogWarning("FeatureSvc denied {FeatureCode} ({ErrorCode})", featureCode, ex.ErrorCode);

            // Keyed off the typed mapping of the WIRE CODE, never off ex.Message (REQ-UI-055).
            return FeatureDecision.Denied(featureCode, options.DefaultUpgradeTier,
                LicenseMessageKeys.ForFeatureFailure(ex.Error), [Humanize(featureCode)]);
        }
    }

    /// <summary>Turns a SCREAMING_SNAKE feature code into a Title Case label.</summary>
    /// <param name="featureCode">The wire feature code, e.g. <c>WHITE_LABEL</c>.</param>
    /// <returns>A readable rendering of the code, e.g. <c>White Label</c>.</returns>
    /// <remarks>
    /// <b>Machine-facing text, deliberately not translated (REQ-UI-055).</b> This is a mechanical
    /// re-casing of AppManager's own feature code — the codes are configured server-side and this
    /// build has no list of them, so there is nothing to key a translation off. It renders in Latin
    /// script inside a Devanagari sentence for the same reason a provider brand name does. The
    /// upgrade prompt has applied exactly this rule to the same codes since REQ-UI-050.
    /// </remarks>
    private static string Humanize(string featureCode)
    {
        var words = featureCode.Replace('_', ' ').ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
