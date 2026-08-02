namespace TechieDesk.Services.AppManager;

/// <summary>
/// Typed representation of every documented AppManager API error code (usage guide v1.4 §6.2
/// plus the per-endpoint error tables). String codes map to members via
/// <see cref="AppManagerErrorMapper.Map(string?)"/>.
/// </summary>
public enum AppManagerError
{
    /// <summary>Error code not present in the documented contract.</summary>
    Unknown = 0,

    /// <summary>Local-only: AppManager:BaseUrl is empty — the app is in offline mode.</summary>
    NotConfigured,

    /// <summary>Local-only: <c>GET /AuthSvc/public-key</c> returned no usable key.</summary>
    KeyFetchFailed,

    /// <summary>Local-only: the server reported success but returned no data payload.</summary>
    EmptyResponse,

    /// <summary><c>VALIDATION_ERROR</c> (400) — request validation failed.</summary>
    ValidationError,

    /// <summary><c>DECRYPTION_FAILED</c> (400) — server could not RSA-decrypt an encrypted password field.</summary>
    DecryptionFailed,

    /// <summary><c>UNAUTHORIZED</c> (401) — missing or invalid access token.</summary>
    Unauthorized,

    /// <summary><c>INVALID_CREDENTIALS</c> (401) — wrong email or password.</summary>
    InvalidCredentials,

    /// <summary><c>INVALID_TOKEN</c> (401) — access token malformed, unknown, expired, or revoked.</summary>
    InvalidToken,

    /// <summary><c>INVALID_REFRESH_TOKEN</c> (401) — refresh token malformed or unknown.</summary>
    InvalidRefreshToken,

    /// <summary><c>EXPIRED_REFRESH_TOKEN</c> (401) — refresh token has expired.</summary>
    ExpiredRefreshToken,

    /// <summary><c>REVOKED_REFRESH_TOKEN</c> (401) — refresh token has been revoked.</summary>
    RevokedRefreshToken,

    /// <summary><c>INVALID_RESET_TOKEN</c> (400) — password-reset token invalid or expired.</summary>
    InvalidResetToken,

    /// <summary><c>INVALID_PASSWORD</c> (400) — new password does not meet complexity rules.</summary>
    InvalidPassword,

    /// <summary><c>INVALID_CURRENT_PASSWORD</c> (400) — current password does not match the stored hash.</summary>
    InvalidCurrentPassword,

    /// <summary><c>ACCOUNT_LOCKED</c> (423) — too many failed login attempts.</summary>
    AccountLocked,

    /// <summary><c>ACCOUNT_DISABLED</c> (403) — account has been deactivated.</summary>
    AccountDisabled,

    /// <summary><c>NOT_FOUND</c> (404) — generic resource not found.</summary>
    NotFound,

    /// <summary><c>ISSUE_NOT_FOUND</c> (404).</summary>
    IssueNotFound,

    /// <summary><c>TRANSACTION_NOT_FOUND</c> (404).</summary>
    TransactionNotFound,

    /// <summary><c>INVOICE_NOT_FOUND</c> (404).</summary>
    InvoiceNotFound,

    /// <summary><c>LICENSE_NOT_FOUND</c> (404).</summary>
    LicenseNotFound,

    /// <summary><c>SUBSCRIPTION_NOT_FOUND</c> (404).</summary>
    SubscriptionNotFound,

    /// <summary><c>PROMO_CODE_NOT_FOUND</c> (404).</summary>
    PromoCodeNotFound,

    /// <summary><c>FEATURE_NOT_FOUND</c> (404).</summary>
    FeatureNotFound,

    /// <summary><c>FLAG_NOT_FOUND</c> (404).</summary>
    FlagNotFound,

    /// <summary><c>USER_NOT_FOUND</c> (404).</summary>
    UserNotFound,

    /// <summary><c>DEVICE_NOT_FOUND</c> (404) — device not registered against the license.</summary>
    DeviceNotFound,

    /// <summary><c>STATUS_NOT_FOUND</c> (400) — no Closed/IsFinal issue status configured.</summary>
    StatusNotFound,

    /// <summary><c>EMAIL_EXISTS</c> (409) — email already registered.</summary>
    EmailExists,

    /// <summary><c>EMAIL_MISMATCH</c> (400) — GDPR delete confirmation email does not match the authenticated user.</summary>
    EmailMismatch,

    /// <summary><c>INTERNAL_ERROR</c> (500) — server error.</summary>
    InternalError,

    /// <summary><c>APPLICATION_ID_REQUIRED</c> (400) — no ApplicationId resolvable for an endpoint that needs one.</summary>
    ApplicationIdRequired,

    /// <summary><c>APP_ID_MISMATCH</c> (400/401/403) — caller's resolved ApplicationId does not match the resource's or token's.</summary>
    AppIdMismatch,

    /// <summary><c>CROSS_APP_LICENSE</c> (403) — license belongs to a different application.</summary>
    CrossAppLicense,

    /// <summary><c>CROSS_APP_RESOURCE</c> (403) — payment resource belongs to a different application.</summary>
    CrossAppResource,

    /// <summary><c>NO_APP_ACCESS</c> (403) — user has no role row for the calling application.</summary>
    NoAppAccess,

    /// <summary><c>LICENSE_INACTIVE</c> (400) — license status is not Active.</summary>
    LicenseInactive,

    /// <summary><c>INVALID_LICENSE_MODEL</c> (400) — license is not a quantity-model license.</summary>
    InvalidLicenseModel,

    /// <summary><c>INSUFFICIENT_QUANTITY</c> (400) — remaining quantity below the requested amount.</summary>
    InsufficientQuantity,

    /// <summary><c>ALREADY_CLOSED</c> (400) — issue is already in the closed status.</summary>
    AlreadyClosed,

    /// <summary><c>ALREADY_CANCELLED</c> (400) — subscription is already cancelled.</summary>
    AlreadyCancelled,

    /// <summary><c>PDF_GENERATION_FAILED</c> — invoice PDF could not be produced.</summary>
    PdfGenerationFailed,

    /// <summary><c>PROMO_CODE_NOT_VALID_FOR_APPLICATION</c> (400) — promo code scoped to a different application.</summary>
    PromoCodeNotValidForApplication,

    /// <summary><c>PROMO_CODE_INACTIVE</c> (400) — promo code disabled.</summary>
    PromoCodeInactive,

    /// <summary><c>PROMO_CODE_EXPIRED</c> (400) — promo code validity window has passed.</summary>
    PromoCodeExpired,

    /// <summary><c>PROMO_CODE_EXHAUSTED</c> (400) — promo code maximum uses reached.</summary>
    PromoCodeExhausted,

    /// <summary><c>RATE_LIMITED</c> — too many requests (referenced by the usage-guide error-handling examples).</summary>
    RateLimited,

    /// <summary><c>SESSION_EXPIRED</c> — session no longer valid (referenced by the usage-guide error-handling examples).</summary>
    SessionExpired,

    /// <summary><c>LICENSE_EXPIRED</c> — license has expired (referenced by the usage-guide error-handling examples).</summary>
    LicenseExpired,

    /// <summary><c>FEATURE_NOT_AVAILABLE</c> — feature not included in the current license (usage-guide examples).</summary>
    FeatureNotAvailable
}
