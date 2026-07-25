using System.Net;
using TechieDesk.Services.AppManager;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.AppManager;

/// <summary>
/// REQ-FN-004: every documented AppManager error code maps to its typed
/// <see cref="AppManagerError"/> member, and failed HTTP calls surface as
/// <see cref="AppManagerException"/> with code, enum, and status.
/// </summary>
public sealed class ErrorMappingTests
{
    /// <summary>
    /// Each documented wire error code maps to the matching typed enum member.
    /// </summary>
    [Theory]
    [InlineData("VALIDATION_ERROR", AppManagerError.ValidationError)]
    [InlineData("DECRYPTION_FAILED", AppManagerError.DecryptionFailed)]
    [InlineData("UNAUTHORIZED", AppManagerError.Unauthorized)]
    [InlineData("INVALID_CREDENTIALS", AppManagerError.InvalidCredentials)]
    [InlineData("INVALID_TOKEN", AppManagerError.InvalidToken)]
    [InlineData("INVALID_REFRESH_TOKEN", AppManagerError.InvalidRefreshToken)]
    [InlineData("EXPIRED_REFRESH_TOKEN", AppManagerError.ExpiredRefreshToken)]
    [InlineData("REVOKED_REFRESH_TOKEN", AppManagerError.RevokedRefreshToken)]
    [InlineData("INVALID_RESET_TOKEN", AppManagerError.InvalidResetToken)]
    [InlineData("INVALID_PASSWORD", AppManagerError.InvalidPassword)]
    [InlineData("INVALID_CURRENT_PASSWORD", AppManagerError.InvalidCurrentPassword)]
    [InlineData("ACCOUNT_LOCKED", AppManagerError.AccountLocked)]
    [InlineData("ACCOUNT_DISABLED", AppManagerError.AccountDisabled)]
    [InlineData("NOT_FOUND", AppManagerError.NotFound)]
    [InlineData("ISSUE_NOT_FOUND", AppManagerError.IssueNotFound)]
    [InlineData("TRANSACTION_NOT_FOUND", AppManagerError.TransactionNotFound)]
    [InlineData("INVOICE_NOT_FOUND", AppManagerError.InvoiceNotFound)]
    [InlineData("LICENSE_NOT_FOUND", AppManagerError.LicenseNotFound)]
    [InlineData("SUBSCRIPTION_NOT_FOUND", AppManagerError.SubscriptionNotFound)]
    [InlineData("PROMO_CODE_NOT_FOUND", AppManagerError.PromoCodeNotFound)]
    [InlineData("FEATURE_NOT_FOUND", AppManagerError.FeatureNotFound)]
    [InlineData("FLAG_NOT_FOUND", AppManagerError.FlagNotFound)]
    [InlineData("USER_NOT_FOUND", AppManagerError.UserNotFound)]
    [InlineData("DEVICE_NOT_FOUND", AppManagerError.DeviceNotFound)]
    [InlineData("STATUS_NOT_FOUND", AppManagerError.StatusNotFound)]
    [InlineData("EMAIL_EXISTS", AppManagerError.EmailExists)]
    [InlineData("EMAIL_MISMATCH", AppManagerError.EmailMismatch)]
    [InlineData("INTERNAL_ERROR", AppManagerError.InternalError)]
    [InlineData("APPLICATION_ID_REQUIRED", AppManagerError.ApplicationIdRequired)]
    [InlineData("APP_ID_MISMATCH", AppManagerError.AppIdMismatch)]
    [InlineData("CROSS_APP_LICENSE", AppManagerError.CrossAppLicense)]
    [InlineData("CROSS_APP_RESOURCE", AppManagerError.CrossAppResource)]
    [InlineData("NO_APP_ACCESS", AppManagerError.NoAppAccess)]
    [InlineData("LICENSE_INACTIVE", AppManagerError.LicenseInactive)]
    [InlineData("INVALID_LICENSE_MODEL", AppManagerError.InvalidLicenseModel)]
    [InlineData("INSUFFICIENT_QUANTITY", AppManagerError.InsufficientQuantity)]
    [InlineData("ALREADY_CLOSED", AppManagerError.AlreadyClosed)]
    [InlineData("ALREADY_CANCELLED", AppManagerError.AlreadyCancelled)]
    [InlineData("PDF_GENERATION_FAILED", AppManagerError.PdfGenerationFailed)]
    [InlineData("PROMO_CODE_NOT_VALID_FOR_APPLICATION", AppManagerError.PromoCodeNotValidForApplication)]
    [InlineData("PROMO_CODE_INACTIVE", AppManagerError.PromoCodeInactive)]
    [InlineData("PROMO_CODE_EXPIRED", AppManagerError.PromoCodeExpired)]
    [InlineData("PROMO_CODE_EXHAUSTED", AppManagerError.PromoCodeExhausted)]
    [InlineData("RATE_LIMITED", AppManagerError.RateLimited)]
    [InlineData("SESSION_EXPIRED", AppManagerError.SessionExpired)]
    [InlineData("LICENSE_EXPIRED", AppManagerError.LicenseExpired)]
    [InlineData("FEATURE_NOT_AVAILABLE", AppManagerError.FeatureNotAvailable)]
    public void KnownCodesMapToEnum(string wireCode, AppManagerError expected)
    {
        Assert.Equal(expected, AppManagerErrorMapper.Map(wireCode));
    }

    /// <summary>
    /// Undocumented, empty, and null codes map to Unknown rather than throwing.
    /// </summary>
    [Theory]
    [InlineData("TOTALLY_NEW_CODE")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownCodeMapsToUnknown(string? wireCode)
    {
        Assert.Equal(AppManagerError.Unknown, AppManagerErrorMapper.Map(wireCode));
    }

    /// <summary>
    /// A failing HTTP call surfaces as AppManagerException carrying the raw code, the typed
    /// enum, the message, and the HTTP status code.
    /// </summary>
    [Fact]
    public async Task HttpErrorProducesTypedException()
    {
        var handler = new StubHttpMessageHandler((request, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized,
                TestFactory.ErrorResponse("INVALID_CREDENTIALS", "Invalid email or password", 401)));
        var client = TestFactory.Client(handler);

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.GetProfileAsync("bad-token"));

        Assert.Equal("INVALID_CREDENTIALS", exception.ErrorCode);
        Assert.Equal(AppManagerError.InvalidCredentials, exception.Error);
        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("Invalid email or password", exception.Message);
    }

    /// <summary>
    /// A non-JSON error body (e.g. proxy page) still produces a typed exception keyed off the
    /// HTTP status code.
    /// </summary>
    [Fact]
    public async Task NonJsonErrorFallsBackToStatus()
    {
        var handler = new StubHttpMessageHandler((request, body) =>
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("<html>bad gateway</html>")
            });
        var client = TestFactory.Client(handler);

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.GetProfileAsync("access-token-1"));

        Assert.Equal("HTTP_502", exception.ErrorCode);
        Assert.Equal(AppManagerError.Unknown, exception.Error);
        Assert.Equal(502, exception.StatusCode);
    }
}
