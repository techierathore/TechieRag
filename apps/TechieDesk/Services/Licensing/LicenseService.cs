using System.Text.Json;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Data;

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

    private DateTimeOffset? lastAttempt;

    /// <summary>Initializes a new instance of the <see cref="LicenseService"/> class.</summary>
    public LicenseService(
        IAppManagerClient appManager,
        ILicenseCacheRepository cacheRepository,
        ITechieDeskAuthModeProvider modeProvider,
        ITechieDeskUserContext userContext,
        SessionTokenStore tokenStore,
        ITokenRefresher tokenRefresher,
        IOptions<LicensingOptions> options,
        TimeProvider timeProvider,
        ILogger<LicenseService> logger)
    {
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

            var result = await appManager.ValidateLicenseAsync(token, cancellationToken).ConfigureAwait(false);

            if (result.IsValid && result.License is not null)
            {
                var validatedAt = timeProvider.GetUtcNow().UtcDateTime;
                var payloadJson = JsonSerializer.Serialize(result, JsonOptions);
                await cacheRepository.UpsertAsync(userId, payloadJson, validatedAt).ConfigureAwait(false);
                Current = FromLicense(LicenseAvailability.Live, result.License, validatedAt,
                    $"License validated with {result.License.LicenseName}.");
            }
            else
            {
                Current = new LicenseStatus
                {
                    Availability = LicenseAvailability.Invalid,
                    LicenseName = result.License?.LicenseName,
                    Status = result.License?.Status ?? "Invalid",
                    ExpiryDate = result.License?.ExpiryDate,
                    Message = "AppManager reports no valid license for this application."
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
            Current = new LicenseStatus
            {
                Availability = LicenseAvailability.Invalid,
                Status = "Invalid",
                Message = "Your license could not be validated. Please contact your administrator."
            };
        }

        return Current;
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
                Message = "AppManager is unreachable and no cached license is available. "
                    + "Features are locked until the license server can be reached."
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
                "AppManager is unreachable — running on your cached license.");
            return status;
        }

        return new LicenseStatus
        {
            Availability = LicenseAvailability.GraceExpired,
            LicenseName = license?.LicenseName,
            Status = license?.Status,
            ExpiryDate = license?.ExpiryDate,
            ValidatedAt = cache.ValidatedAt,
            Message = $"Cached license expired after the {options.LicenseGraceHours}h grace period. "
                + "Reconnect to AppManager — premium features are locked until then."
        };
    }

    private static LicenseStatus FromLicense(
        LicenseAvailability availability, ActiveLicenseData? license, DateTime validatedAt, string message)
    {
        return new LicenseStatus
        {
            Availability = availability,
            LicenseName = license?.LicenseName,
            Status = license?.Status,
            ExpiryDate = license?.ExpiryDate,
            DaysRemaining = license?.DaysRemaining,
            ValidatedAt = validatedAt,
            Message = message
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
