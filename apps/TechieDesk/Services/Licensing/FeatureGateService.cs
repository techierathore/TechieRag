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
            return FeatureDecision.Denied(featureCode ?? string.Empty, reason: "No feature specified.");
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
                    $"{Humanize(featureCode)} is available on {options.DefaultUpgradeTier} and Enterprise plans.")
                : FeatureDecision.Allowed(featureCode);
        }

        // Grace expired: degrade — deny premium features until AppManager is reachable again.
        var licenseStatus = licenseService.Current;
        if (licenseStatus.Availability == LicenseAvailability.GraceExpired)
        {
            return FeatureDecision.Denied(featureCode, options.DefaultUpgradeTier,
                "License verification is unavailable (grace period expired).");
        }

        if (!tokenStore.HasSession)
        {
            return FeatureDecision.Denied(featureCode, reason: "Sign in to access this feature.");
        }

        try
        {
            await tokenRefresher.EnsureValidTokenAsync(cancellationToken).ConfigureAwait(false);
            var access = await appManager
                .CheckFeatureAsync(tokenStore.AccessToken!, featureCode, cancellationToken)
                .ConfigureAwait(false);

            return access.HasAccess
                ? FeatureDecision.Allowed(featureCode, access.Level)
                : FeatureDecision.Denied(featureCode, access.RequiredLicense ?? options.DefaultUpgradeTier,
                    access.Reason ?? $"{Humanize(featureCode)} requires a plan upgrade.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException
            || (ex is AppManagerException am && (am.StatusCode == 0 || am.StatusCode >= 500)))
        {
            // AppManager unreachable: honor a cached/live license within grace, otherwise deny.
            logger.LogWarning(ex, "FeatureSvc unreachable for {FeatureCode} — resolving from license grace state", featureCode);
            return licenseStatus.FeaturesPermitted
                ? FeatureDecision.Allowed(featureCode)
                : FeatureDecision.Denied(featureCode, options.DefaultUpgradeTier,
                    "Feature access could not be verified — the license server is unreachable.");
        }
        catch (AppManagerException ex)
        {
            logger.LogWarning("FeatureSvc denied {FeatureCode} ({ErrorCode})", featureCode, ex.ErrorCode);
            return FeatureDecision.Denied(featureCode, options.DefaultUpgradeTier,
                $"{Humanize(featureCode)} requires a plan upgrade.");
        }
    }

    /// <summary>Turns a SCREAMING_SNAKE feature code into a Title Case label.</summary>
    private static string Humanize(string featureCode)
    {
        var words = featureCode.Replace('_', ' ').ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
