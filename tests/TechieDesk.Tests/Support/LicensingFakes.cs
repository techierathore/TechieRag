using TechieDesk.Services.AppManager;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Data;
using TechieDesk.Services.Licensing;

namespace TechieDesk.Tests.Support;

/// <summary>A <see cref="TimeProvider"/> whose UTC clock is fixed and manually advanced.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset now;

    public FixedTimeProvider(DateTimeOffset start) => now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now = now.Add(by);
}

/// <summary>
/// An <see cref="IAppManagerClient"/> whose LicenseSvc/FeatureSvc behavior is scripted per test;
/// every other member throws (it must never be called by the licensing units under test).
/// </summary>
public sealed class FakeAppManagerClient : IAppManagerClient
{
    public Func<string, CancellationToken, Task<LicenseValidationData>>? OnValidateLicense { get; set; }

    public Func<string, string, CancellationToken, Task<FeatureAccessData>>? OnCheckFeature { get; set; }

    /// <summary>Scripts <c>POST /AuthSvc/login</c> for the REQ-FN-039 sign-in tests.</summary>
    public Func<string, string, Task<AuthResponseData>>? OnLogin { get; set; }

    /// <summary>Scripts <c>POST /AuthSvc/register</c> for the REQ-FN-039 sign-in tests.</summary>
    public Func<RegisterRequest, string, Task<AuthResponseData>>? OnRegister { get; set; }

    public int ValidateLicenseCalls { get; private set; }

    /// <summary>
    /// The install identity presented on the LAST validation call (REQ-FN-051 clause 2), or null
    /// when none was sent.
    /// </summary>
    public string? LastInstallId { get; private set; }

    public Task<LicenseValidationData> ValidateLicenseAsync(
        string accessToken, string? installId = null, CancellationToken cancellationToken = default)
    {
        ValidateLicenseCalls++;
        LastInstallId = installId;
        return OnValidateLicense?.Invoke(accessToken, cancellationToken)
            ?? throw new InvalidOperationException("OnValidateLicense not configured");
    }

    public Task<FeatureAccessData> CheckFeatureAsync(string accessToken, string featureCode, CancellationToken cancellationToken = default)
        => OnCheckFeature?.Invoke(accessToken, featureCode, cancellationToken)
            ?? throw new InvalidOperationException("OnCheckFeature not configured");

    // Unused members — never called by the licensing services.
    public Task<string> GetPublicKeyAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AuthResponseData> RegisterAsync(RegisterRequest r, string p, CancellationToken ct = default)
        => OnRegister?.Invoke(r, p) ?? throw new NotSupportedException();
    public Task<AuthResponseData> LoginAsync(string e, string p, CancellationToken ct = default)
        => OnLogin?.Invoke(e, p) ?? throw new NotSupportedException();
    public Task<TokenRefreshData> RefreshAsync(string r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task LogoutAsync(string a, string? r, bool all = false, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ForgotPasswordAsync(string e, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ResetPasswordAsync(string t, string p, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ChangePasswordAsync(string a, string c, string n, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<UserProfileData> GetProfileAsync(string a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateProfileAsync(string a, UpdateProfileRequest r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<GdprRequestData> RequestDataExportAsync(string a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<GdprRequestData> RequestAccountDeletionAsync(string a, string e, string? reason = null, CancellationToken ct = default) => throw new NotSupportedException();

    // IssueSvc (REQ-UI-032/033/047, REQ-FN-027) — the licensing units never raise a support issue,
    // so these stay in the "must never be called" group. SupportWireContractTests drives the real
    // client over StubHttpMessageHandler instead of scripting a fake.
    public Task<IReadOnlyList<SupportIssueData>> ListIssuesAsync(string a, string? status = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SupportIssueData> GetIssueAsync(string a, int issueId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<CreatedIssueData> CreateIssueAsync(string a, CreateIssueRequest r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddIssueCommentAsync(string a, int issueId, string comment, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CloseIssueAsync(string a, int issueId, CancellationToken ct = default) => throw new NotSupportedException();

    // LicenseSvc catalogue and PaymentSvc billing (REQ-UI-029/030/031, REQ-FN-026) — the licensing
    // units never read the price list or the payment history, so these stay in the "must never be
    // called" group. BillingWireContractTests drives the real client over StubHttpMessageHandler.
    public Task<IReadOnlyList<LicenseTypeData>> GetLicenseTypesAsync(string? currency = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<UserLicenseData>> GetLicensesAsync(string a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeactivateDeviceAsync(string a, int licenseId, int deviceId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<SubscriptionData>> GetSubscriptionsAsync(string a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CancelSubscriptionAsync(string a, int subscriptionId, bool immediate = false, string? reason = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PagedResultData<TransactionData>> GetTransactionsAsync(string a, int page = 1, int pageSize = 20, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PagedResultData<InvoiceData>> GetInvoicesAsync(string a, int page = 1, int pageSize = 20, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<InvoiceDownloadData> DownloadInvoiceAsync(string a, int invoiceId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PromoCodeData> ValidatePromoCodeAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>In-memory <see cref="ILicenseCacheRepository"/> for grace-window tests.</summary>
public sealed class InMemoryLicenseCacheRepository : ILicenseCacheRepository
{
    private readonly Dictionary<string, LicenseCache> store = new();

    public Task UpsertAsync(string userId, string payloadJson, DateTime validatedAt)
    {
        store[userId] = new LicenseCache { UserId = userId, PayloadJson = payloadJson, ValidatedAt = validatedAt };
        return Task.CompletedTask;
    }

    public Task<LicenseCache?> GetAsync(string userId)
        => Task.FromResult(store.TryGetValue(userId, out var c) ? c : null);
}

/// <summary>A token refresher that reports the session valid without any HTTP call.</summary>
public sealed class StubTokenRefresher : ITokenRefresher
{
    public Task<bool> EnsureValidTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}

/// <summary>A user context returning a fixed user.</summary>
public sealed class StubUserContext : ITechieDeskUserContext
{
    public StubUserContext(TechieDeskUser user) => CurrentUser = user;

    public TechieDeskUser CurrentUser { get; }
}

/// <summary>An <see cref="ILicenseService"/> whose current status is set directly by the test.</summary>
public sealed class FakeLicenseService : ILicenseService
{
    public FakeLicenseService(LicenseStatus current) => Current = current;

    public LicenseStatus Current { get; set; }

    public Task<LicenseStatus> ValidateAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task<LicenseStatus> EnsureFreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
}
