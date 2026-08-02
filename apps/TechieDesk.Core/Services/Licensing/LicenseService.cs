using System.Text.Json;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Data;
using TechieDesk.Services.Install;

namespace TechieDesk.Services.Licensing;

/// <summary>
/// Default <see cref="ILicenseService"/>. Validates via <see cref="IAppManagerClient"/>, persists
/// the last-known-good payload through <see cref="ILicenseCacheRepository"/>, and applies the
/// configured grace window when AppManager is unreachable (REQ-FN-013/015).
/// </summary>
public sealed class LicenseService : ILicenseService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAppManagerClient appManager;
    private readonly ILicenseCacheRepository cacheRepository;
    private readonly ITechieDeskAuthModeProvider modeProvider;
    private readonly ITechieDeskUserContext userContext;
    private readonly SessionTokenStore tokenStore;
    private readonly ITokenRefresher tokenRefresher;
    private readonly LicensingOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<LicenseService> logger;
    private readonly IInstallIdentityProvider? installIdentity;

    private DateTimeOffset? lastAttempt;

    /// <summary>Initializes a new instance of the <see cref="LicenseService"/> class.</summary>
    /// <remarks>
    /// <paramref name="installIdentity"/> is the REQ-FN-051 clause 2 seam and is optional on
    /// purpose: a host that has no install identity still validates licences exactly as before, and
    /// nothing about identity is on the path of an offline, account-free install (BRD-129).
    /// </remarks>
    public LicenseService(
        IAppManagerClient appManager,
        ILicenseCacheRepository cacheRepository,
        ITechieDeskAuthModeProvider modeProvider,
        ITechieDeskUserContext userContext,
        SessionTokenStore tokenStore,
        ITokenRefresher tokenRefresher,
        IOptions<LicensingOptions> options,
        TimeProvider timeProvider,
        ILogger<LicenseService> logger,
        IInstallIdentityProvider? installIdentity = null)
    {
        this.installIdentity = installIdentity;
        this.appManager = appManager;
        this.cacheRepository = cacheRepository;
        this.modeProvider = modeProvider;
        this.userContext = userContext;
        this.tokenStore = tokenStore;
        this.tokenRefresher = tokenRefresher;
        this.options = options.Value;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    public LicenseStatus Current { get; private set; } = LicenseStatus.Unknown;

    /// <inheritdoc />
    public async Task<LicenseStatus> EnsureFreshAsync(CancellationToken cancellationToken = default)
    {
        if (!modeProvider.IsAppManagerEnabled)
        {
            Current = LicenseStatus.Offline;
            return Current;
        }

        var now = timeProvider.GetUtcNow();
        var isStale = lastAttempt is null
            || now - lastAttempt.Value >= TimeSpan.FromMinutes(Math.Max(1, options.LicenseRevalidationMinutes));

        if (Current.Availability == LicenseAvailability.Unknown || isStale)
        {
            return await ValidateAsync(cancellationToken).ConfigureAwait(false);
        }

        return Current;
    }

    /// <inheritdoc />
    public async Task<LicenseStatus> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (!modeProvider.IsAppManagerEnabled)
        {
            Current = LicenseStatus.Offline;
            return Current;
        }

        lastAttempt = timeProvider.GetUtcNow();

        if (!tokenStore.HasSession)
        {
            Current = LicenseStatus.Unknown;
            return Current;
        }

        var userId = userContext.CurrentUser.UserId.ToString();

        try
        {
            await tokenRefresher.EnsureValidTokenAsync(cancellationToken).ConfigureAwait(false);
            var token = tokenStore.AccessToken
                ?? throw new AppManagerException("UNAUTHORIZED", "No access token after refresh", 401);

            var result = await appManager
                .ValidateLicenseAsync(token, InstallIdentityForValidation(), cancellationToken)
                .ConfigureAwait(false);

            if (result.IsValid && result.License is not null)
            {
                var validatedAt = timeProvider.GetUtcNow().UtcDateTime;
                var payloadJson = JsonSerializer.Serialize(result, JsonOptions);
                await cacheRepository.UpsertAsync(userId, payloadJson, validatedAt).ConfigureAwait(false);

                // The plan name travels as a formatting ARGUMENT, invariant, exactly as AppManager
                // sent it (REQ-UI-055): it is the value entitlements are matched on, so the
                // sentence around it is translated and the name itself never is.
                Current = FromLicense(LicenseAvailability.Live, result.License, validatedAt,
                    LicenseMessageKeys.StateValidated, [result.License.LicenseName]);
            }
            else
            {
                Current = new LicenseStatus
                {
                    Availability = LicenseAvailability.Invalid,
                    LicenseName = result.License?.LicenseName,
                    Status = result.License?.Status ?? "Invalid",
                    ExpiryDate = result.License?.ExpiryDate,
                    MessageKey = LicenseMessageKeys.StateNoValidLicense
                };
            }
        }
        catch (Exception ex) when (IsUnreachable(ex))
        {
            logger.LogWarning(ex,
                "AppManager unreachable during license validation — falling back to cached license");
            Current = await BuildFromCacheAsync(userId).ConfigureAwait(false);
        }
        catch (AppManagerException ex)
        {
            logger.LogWarning("License validation rejected by AppManager ({ErrorCode})", ex.ErrorCode);

            // REQ-UI-055: keyed off ex.Error — the typed mapping of the WIRE CODE — and never off
            // ex.Message, which is prose written by a server this app does not own and is not part
            // of any documented contract. The raw code stays in the log for the operator.
            Current = new LicenseStatus
            {
                Availability = LicenseAvailability.Invalid,
                Status = "Invalid",
                MessageKey = LicenseMessageKeys.ForValidationFailure(ex.Error)
            };
        }

        return Current;
    }

    /// <summary>
    /// Resolves the install identity to present at licence validation (REQ-FN-051 clause 2).
    /// </summary>
    /// <returns>
    /// The composite install id, or null — which is the default and sends nothing.
    /// </returns>
    /// <remarks>
    /// Gated by <see cref="LicensingOptions.SendInstallIdentity"/>, which ships OFF because
    /// AppManager has no documented endpoint that consumes this yet. Failure to compute an identity
    /// returns null rather than propagating: a machine that cannot be fingerprinted must still be
    /// able to validate its licence.
    /// </remarks>
    private string? InstallIdentityForValidation()
    {
        if (!options.SendInstallIdentity || installIdentity is null)
        {
            return null;
        }

        try
        {
            return installIdentity.Current.CompositeId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "The install identity could not be resolved; validating without it (REQ-FN-051)");
            return null;
        }
    }

    /// <summary>
    /// Builds a status from the cached last-known-good payload, honoring the grace window
    /// (REQ-FN-015). Within the window the state is <see cref="LicenseAvailability.Cached"/>;
    /// past it the state degrades to <see cref="LicenseAvailability.GraceExpired"/>.
    /// </summary>
    private async Task<LicenseStatus> BuildFromCacheAsync(string userId)
    {
        var cache = await cacheRepository.GetAsync(userId).ConfigureAwait(false);
        if (cache is null)
        {
            return new LicenseStatus
            {
                Availability = LicenseAvailability.GraceExpired,
                MessageKey = LicenseMessageKeys.StateNoCachedLicense
            };
        }

        ActiveLicenseData? license = null;
        try
        {
            license = JsonSerializer
                .Deserialize<LicenseValidationData>(cache.PayloadJson, JsonOptions)?.License;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Cached license payload could not be deserialized");
        }

        var age = timeProvider.GetUtcNow().UtcDateTime - cache.ValidatedAt;
        var graceWindow = TimeSpan.FromHours(Math.Max(0, options.LicenseGraceHours));

        if (age <= graceWindow)
        {
            var status = FromLicense(LicenseAvailability.Cached, license, cache.ValidatedAt,
                LicenseMessageKeys.StateCached, []);
            return status;
        }

        return new LicenseStatus
        {
            Availability = LicenseAvailability.GraceExpired,
            LicenseName = license?.LicenseName,
            Status = license?.Status,
            ExpiryDate = license?.ExpiryDate,
            ValidatedAt = cache.ValidatedAt,

            // The grace window is a NUMBER in a translated sentence, not a number glued to an "h"
            // suffix: a language that writes the unit differently needs the whole phrase, so the
            // resource carries the wording and this carries only the hours (REQ-UI-055).
            MessageKey = LicenseMessageKeys.StateGraceExpired,
            MessageArguments = [options.LicenseGraceHours]
        };
    }

    private static LicenseStatus FromLicense(
        LicenseAvailability availability,
        ActiveLicenseData? license,
        DateTime validatedAt,
        string messageKey,
        IReadOnlyList<object?> messageArguments)
    {
        return new LicenseStatus
        {
            Availability = availability,
            LicenseName = license?.LicenseName,
            Status = license?.Status,
            ExpiryDate = license?.ExpiryDate,
            DaysRemaining = license?.DaysRemaining,
            ValidatedAt = validatedAt,
            MessageKey = messageKey,
            MessageArguments = messageArguments
        };
    }

    /// <summary>
    /// Classifies an exception as an AppManager reachability failure (network/timeout/5xx/local),
    /// as opposed to a definitive negative answer such as an invalid or unauthorized license.
    /// </summary>
    private static bool IsUnreachable(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => true,
            TaskCanceledException => true,
            OperationCanceledException => true,
            AppManagerException am => am.StatusCode == 0 || am.StatusCode >= 500,
            _ => false
        };
    }
}
