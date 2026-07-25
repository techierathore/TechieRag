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

    public int ValidateLicenseCalls { get; private set; }

    public Task<LicenseValidationData> ValidateLicenseAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        ValidateLicenseCalls++;
        return OnValidateLicense?.Invoke(accessToken, cancellationToken)
            ?? throw new InvalidOperationException("OnValidateLicense not configured");
    }

    public Task<FeatureAccessData> CheckFeatureAsync(string accessToken, string featureCode, CancellationToken cancellationToken = default)
        => OnCheckFeature?.Invoke(accessToken, featureCode, cancellationToken)
            ?? throw new InvalidOperationException("OnCheckFeature not configured");

    // Unused members — never called by the licensing services.
    public Task<string> GetPublicKeyAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AuthResponseData> RegisterAsync(RegisterRequest r, string p, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<AuthResponseData> LoginAsync(string e, string p, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TokenRefreshData> RefreshAsync(string r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task LogoutAsync(string a, string? r, bool all = false, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ForgotPasswordAsync(string e, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ResetPasswordAsync(string t, string p, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ChangePasswordAsync(string a, string c, string n, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<UserProfileData> GetProfileAsync(string a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateProfileAsync(string a, UpdateProfileRequest r, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<GdprRequestData> RequestDataExportAsync(string a, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<GdprRequestData> RequestAccountDeletionAsync(string a, string e, string? reason = null, CancellationToken ct = default) => throw new NotSupportedException();
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
