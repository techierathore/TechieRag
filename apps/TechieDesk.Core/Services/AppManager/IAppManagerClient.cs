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
    /// <param name="installId">
    /// This installation's identity, sent as the <c>X-Install-Id</c> header so AppManager can bind a
    /// seat to one install (REQ-FN-051 clause 2). Null — the default — sends nothing at all, which
    /// is byte-for-byte the pre-REQ-FN-051 request. See
    /// <c>LicensingOptions.SendInstallIdentity</c> for why it is off by default: the server-side
    /// registration contract this feeds does not exist in the documented API yet.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The license validation result.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<LicenseValidationData> ValidateLicenseAsync(
        string accessToken, string? installId = null, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Lists the authenticated user's support issues via <c>GET /IssueSvc</c> (REQ-UI-033).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="status">Optional status filter (<c>Open</c>, <c>InProgress</c>, <c>Resolved</c>, <c>Closed</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's issues, newest-first as the server ordered them.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<IReadOnlyList<SupportIssueData>> ListIssuesAsync(string accessToken, string? status = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one support issue with its public comment thread via
    /// <c>GET /IssueSvc/{aIssueId}</c> (REQ-UI-033).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="issueId">The numeric issue identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The issue and its comments.</returns>
    /// <exception cref="AppManagerException">Including <c>ISSUE_NOT_FOUND</c> and <c>APP_ID_MISMATCH</c>.</exception>
    Task<SupportIssueData> GetIssueAsync(string accessToken, int issueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a support issue via <c>POST /IssueSvc</c> (REQ-UI-032).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="request">The issue to create; <c>ApplicationId</c> is required by the endpoint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The assigned issue identifier and issue number.</returns>
    /// <exception cref="AppManagerException">Including <c>VALIDATION_ERROR</c> and <c>APPLICATION_ID_REQUIRED</c>.</exception>
    Task<CreatedIssueData> CreateIssueAsync(string accessToken, CreateIssueRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a public comment to an issue via <c>POST /IssueSvc/{aIssueId}/comments</c>
    /// (REQ-UI-033).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="issueId">The numeric issue identifier.</param>
    /// <param name="comment">The comment body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AppManagerException">Including <c>VALIDATION_ERROR</c> and <c>ISSUE_NOT_FOUND</c>.</exception>
    Task AddIssueCommentAsync(string accessToken, int issueId, string comment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes an issue the caller raised via <c>POST /IssueSvc/{aIssueId}/close</c> (REQ-FN-027).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="issueId">The numeric issue identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AppManagerException">
    /// Including <c>ALREADY_CLOSED</c> when the issue is already closed and <c>STATUS_NOT_FOUND</c>
    /// when the server has no closed status configured.
    /// </exception>
    Task CloseIssueAsync(string accessToken, int issueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the purchasable license types via <c>GET /LicenseSvc/types</c>. Anonymous — the
    /// ApplicationId is resolved from the API key headers, or sent explicitly as
    /// <c>aApplicationId</c> when one is configured (REQ-UI-029).
    /// </summary>
    /// <param name="currency">Optional ISO currency filter sent as <c>aCurrency</c> (e.g. <c>USD</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalogue of license types with their per-currency pricing.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<IReadOnlyList<LicenseTypeData>> GetLicenseTypesAsync(string? currency = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the current user's licenses via <c>GET /LicenseSvc</c>, scoped to this application
    /// when an ApplicationId is configured (REQ-UI-030).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's licenses, in the order the server returned them.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<IReadOnlyList<UserLicenseData>> GetLicensesAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates one device from a license via
    /// <c>DELETE /LicenseSvc/{aLicenseId}/devices/{aDeviceId}</c> (REQ-UI-030).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="licenseId">The license the device is registered against.</param>
    /// <param name="deviceId">The device to release.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AppManagerException">Including <c>DEVICE_NOT_FOUND</c> and <c>CROSS_APP_LICENSE</c>.</exception>
    Task DeactivateDeviceAsync(string accessToken, int licenseId, int deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the current user's subscriptions via <c>GET /PaymentSvc/subscriptions</c>
    /// (REQ-UI-030 / BRD-77).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's subscriptions for this application.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<IReadOnlyList<SubscriptionData>> GetSubscriptionsAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a subscription via <c>POST /PaymentSvc/subscriptions/{aSubscriptionId}/cancel</c>,
    /// either at the end of the current billing period or immediately (REQ-UI-030 / BRD-77).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="subscriptionId">The subscription to cancel.</param>
    /// <param name="cancelImmediately">
    /// False (the default) ends the subscription when the current period closes; true ends it now.
    /// </param>
    /// <param name="reason">Optional free-text cancellation reason stored on the subscription.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AppManagerException">Including <c>ALREADY_CANCELLED</c> and <c>SUBSCRIPTION_NOT_FOUND</c>.</exception>
    Task CancelSubscriptionAsync(string accessToken, int subscriptionId, bool cancelImmediately = false, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a page of the user's payment history via <c>GET /PaymentSvc/transactions</c>
    /// (REQ-UI-031 / BRD-78).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The number of rows per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The requested page of transactions with its paging metadata.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<PagedResultData<TransactionData>> GetTransactionsAsync(string accessToken, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a page of the user's invoices via <c>GET /PaymentSvc/invoices</c>
    /// (REQ-UI-031 / BRD-78).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The number of rows per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The requested page of invoices with its paging metadata.</returns>
    /// <exception cref="AppManagerException">On any documented API error.</exception>
    Task<PagedResultData<InvoiceData>> GetInvoicesAsync(string accessToken, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an invoice PDF via <c>GET /PaymentSvc/invoices/{aInvoiceId}/download</c>.
    /// AppManager renders the document; TechieDesk never generates invoice PDFs itself
    /// (REQ-UI-031 / BRD-78).
    /// </summary>
    /// <param name="accessToken">The current access token (bearer auth).</param>
    /// <param name="invoiceId">The invoice to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rendered PDF bytes with the server's suggested file name.</returns>
    /// <exception cref="AppManagerException">
    /// Including <c>INVOICE_NOT_FOUND</c> and <c>PDF_GENERATION_FAILED</c>; a success response that
    /// is not a PDF is rejected rather than saved as a broken file.
    /// </exception>
    Task<InvoiceDownloadData> DownloadInvoiceAsync(string accessToken, int invoiceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a promotional code via <c>POST /PaymentSvc/promo-codes/validate</c>. Anonymous,
    /// but application-scoped: the ApplicationId comes from the API key headers (REQ-FN-026 /
    /// BRD-79).
    /// </summary>
    /// <param name="code">The promo code, already normalized by <see cref="PromoCodeValidator"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The discount the code grants.</returns>
    /// <exception cref="AppManagerException">
    /// Including <c>PROMO_CODE_NOT_FOUND</c>, <c>PROMO_CODE_INACTIVE</c>,
    /// <c>PROMO_CODE_EXPIRED</c>, <c>PROMO_CODE_EXHAUSTED</c> and
    /// <c>PROMO_CODE_NOT_VALID_FOR_APPLICATION</c>.
    /// </exception>
    Task<PromoCodeData> ValidatePromoCodeAsync(string code, CancellationToken cancellationToken = default);
}
