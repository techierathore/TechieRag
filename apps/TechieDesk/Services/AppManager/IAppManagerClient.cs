using TechieDesk.Services.AppManager.Models;

namespace TechieDesk.Services.AppManager;

/// <summary>
/// Typed client for the AppManager API (wire contract v1.4). Every AppManager call made by
/// TechieDesk goes through this interface (BRD-21); implementations send the
/// <c>X-Api-Key</c>/<c>X-Api-Secret</c> headers on every request and RSA-encrypt all password
/// fields before transmission (BRD-14).
/// </summary>
public interface IAppManagerClient
{
    /// <summary>
    /// Fetches (and caches) the server's PEM-encoded RSA public key from
    /// <c>GET /AuthSvc/public-key</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The PEM-encoded RSA public key.</returns>
    /// <exception cref="AppManagerException">When the key cannot be fetched.</exception>
    Task<string> GetPublicKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new user via <c>POST /AuthSvc/register</c>. The password is RSA-encrypted
    /// before sending; on <c>DECRYPTION_FAILED</c> the public key is refetched and the call
    /// retried exactly once.
    /// </summary>
    /// <param name="request">The registration details (no password).</param>
    /// <param name="password">The plaintext password, encrypted in-memory just before transmission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authenticated user with tokens.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<AuthResponseData> RegisterAsync(RegisterRequest request, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a user in via <c>POST /AuthSvc/login</c> with an RSA-encrypted password.
    /// </summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="password">The plaintext password, encrypted in-memory just before transmission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authenticated user with tokens and active license.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<AuthResponseData> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a refresh token for a fresh token pair via <c>POST /AuthSvc/refresh</c>.
    /// </summary>
    /// <param name="refreshToken">The current refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new token pair with expiry.</returns>
    /// <exception cref="AppManagerException">When the refresh token is invalid, expired, or revoked.</exception>
    Task<TokenRefreshData> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs the user out via <c>POST /AuthSvc/logout</c>, optionally across all devices.
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="refreshToken">The refresh token to revoke, when known.</param>
    /// <param name="logoutAllDevices">True to revoke every refresh token for the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task LogoutAsync(string accessToken, string? refreshToken, bool logoutAllDevices = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a password reset via <c>POST /AuthSvc/forgot-password</c>.
    /// </summary>
    /// <param name="email">The account email address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a password reset via <c>POST /AuthSvc/reset-password</c> with an RSA-encrypted
    /// new password.
    /// </summary>
    /// <param name="token">The reset token from the password-reset email.</param>
    /// <param name="newPassword">The plaintext new password, encrypted just before transmission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the current user's password via <c>POST /UserSvc/change-password</c>; both
    /// passwords are RSA-encrypted before transmission.
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="currentPassword">The plaintext current password.</param>
    /// <param name="newPassword">The plaintext new password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user's app-scoped profile via <c>GET /UserSvc/profile</c>.
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's profile with the app-scoped <c>applicationRole</c>.</returns>
    /// <exception cref="AppManagerException">Including <c>NO_APP_ACCESS</c> when the user has no role for this app.</exception>
    Task<UserProfileData> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the current user's profile via <c>PUT /UserSvc/profile</c>.
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="request">The profile fields to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task UpdateProfileAsync(string accessToken, UpdateProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the current user's license for this application via
    /// <c>POST /LicenseSvc/validate</c> (with the v1.4 <c>aApplicationId</c> query parameter
    /// when an explicit ApplicationId is configured).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The license validation result.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<LicenseValidationData> ValidateLicenseAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the current user's access to a feature via <c>GET /FeatureSvc/{aFeatureCode}</c>.
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="featureCode">The feature code (e.g. <c>CONNECTORS</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The feature access decision.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<FeatureAccessData> CheckFeatureAsync(string accessToken, string featureCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a GDPR data-export request via <c>POST /UserSvc/data-export</c>.
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The accepted request details.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<GdprRequestData> RequestDataExportAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a GDPR account-deletion request via <c>POST /UserSvc/delete-request</c>.
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="confirmEmail">Must match the authenticated user's email.</param>
    /// <param name="reason">Optional free-text reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The accepted request details.</returns>
    /// <exception cref="AppManagerException">Including <c>EMAIL_MISMATCH</c> when the confirmation email differs.</exception>
    Task<GdprRequestData> RequestAccountDeletionAsync(string accessToken, string confirmEmail, string? reason = null, CancellationToken cancellationToken = default);
}
