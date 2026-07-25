using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;

namespace TechieDesk.Services.Auth;

/// <summary>
/// Default <see cref="ITokenRefresher"/>: refreshes the per-circuit token pair via
/// <see cref="IAppManagerClient.RefreshAsync"/> when the access token is within the
/// configured lead window of expiry (BRD-15). On failure the session is cleared, which makes
/// the route guard redirect to <c>/login</c> preserving the requested route.
/// </summary>
public sealed class TokenRefresher : ITokenRefresher
{
    private readonly IAppManagerClient client;
    private readonly SessionTokenStore tokenStore;
    private readonly ITechieDeskAuthModeProvider modeProvider;
    private readonly AppManagerOptions options;
    private readonly ILogger<TokenRefresher> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenRefresher"/> class.
    /// </summary>
    /// <param name="client">The AppManager API client.</param>
    /// <param name="tokenStore">The per-circuit session token store.</param>
    /// <param name="modeProvider">The auth-mode switch.</param>
    /// <param name="options">The AppManager configuration (refresh lead window).</param>
    /// <param name="logger">Logger.</param>
    public TokenRefresher(
        IAppManagerClient client,
        SessionTokenStore tokenStore,
        ITechieDeskAuthModeProvider modeProvider,
        IOptions<AppManagerOptions> options,
        ILogger<TokenRefresher> logger)
    {
        this.client = client;
        this.tokenStore = tokenStore;
        this.modeProvider = modeProvider;
        this.options = options.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> EnsureValidTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!modeProvider.IsAppManagerEnabled)
        {
            return true;
        }

        if (!tokenStore.HasSession)
        {
            return false;
        }

        var expiresAt = tokenStore.ExpiresAt;
        var lead = TimeSpan.FromSeconds(options.TokenRefreshLeadSeconds);
        if (expiresAt.HasValue && expiresAt.Value - DateTimeOffset.UtcNow > lead)
        {
            return true;
        }

        try
        {
            var refreshed = await client
                .RefreshAsync(tokenStore.RefreshToken!, cancellationToken).ConfigureAwait(false);
            tokenStore.UpdateTokens(refreshed.AccessToken, refreshed.RefreshToken, refreshed.ExpiresAt);
            logger.LogDebug("Access token silently refreshed; new expiry {ExpiresAt}", refreshed.ExpiresAt);
            return true;
        }
        catch (AppManagerException ex)
        {
            logger.LogWarning("Silent token refresh failed ({ErrorCode}) — clearing session", ex.ErrorCode);
            tokenStore.Clear();
            return false;
        }
    }
}
