using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Licensing;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Licensing;

/// <summary>
/// REQ-FN-013/BRD-49 (validate + status mapping) and REQ-FN-015/BRD-51 (AppManager-outage grace
/// window): the license service validates via LicenseSvc, caches last-known-good, and honors the
/// cache for the configured grace period when AppManager is unreachable.
/// </summary>
public sealed class LicenseServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static TechieDeskUser User() => new(123, "jane@example.com", "Jane Doe", ProductRole.User, true);

    private static (LicenseService service, FakeAppManagerClient client, InMemoryLicenseCacheRepository cache, FixedTimeProvider time)
        Build(bool appManagerEnabled = true, bool withSession = true, int graceHours = 72, int revalidateMinutes = 60)
    {
        var client = new FakeAppManagerClient();
        var cache = new InMemoryLicenseCacheRepository();
        var time = new FixedTimeProvider(Now);
        var store = new SessionTokenStore();
        if (withSession)
        {
            store.SetSession(User(), "access-1", "refresh-1", Now.AddHours(1));
        }

        var options = Options.Create(new LicensingOptions
        {
            LicenseGraceHours = graceHours,
            LicenseRevalidationMinutes = revalidateMinutes
        });

        var service = new LicenseService(
            client,
            cache,
            TestFactory.Mode(appManagerEnabled),
            new StubUserContext(User()),
            store,
            new StubTokenRefresher(),
            options,
            time,
            NullLogger<LicenseService>.Instance);

        return (service, client, cache, time);
    }

    private static LicenseValidationData ValidLicense(string name = "Professional", string status = "Active", int days = 200)
        => new()
        {
            IsValid = true,
            License = new ActiveLicenseData
            {
                LicenseId = 1,
                LicenseName = name,
                Status = status,
                ExpiryDate = Now.AddDays(days),
                DaysRemaining = days
            }
        };

    /// <summary>A successful validation maps name/status/expiry and marks the state Live.</summary>
    [Fact]
    public async Task ValidateMapsLiveLicense()
    {
        var (service, client, _, _) = Build();
        client.OnValidateLicense = (_, _) => Task.FromResult(ValidLicense());

        var status = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.Live, status.Availability);
        Assert.Equal("Professional", status.LicenseName);
        Assert.Equal("Active", status.Status);
        Assert.Equal(200, status.DaysRemaining);
        Assert.True(status.FeaturesPermitted);
    }

    /// <summary>Offline mode never calls AppManager and reports the local Free tier.</summary>
    [Fact]
    public async Task OfflineModeReportsFreeTierWithoutCallingAppManager()
    {
        var (service, client, _, _) = Build(appManagerEnabled: false);

        var status = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.Offline, status.Availability);
        Assert.True(status.FeaturesPermitted);
        Assert.Equal(0, client.ValidateLicenseCalls);
    }

    /// <summary>A reachable AppManager reporting an invalid license maps to Invalid (no grace).</summary>
    [Fact]
    public async Task InvalidLicenseIsNotTreatedAsGrace()
    {
        var (service, client, _, _) = Build();
        client.OnValidateLicense = (_, _) => Task.FromResult(new LicenseValidationData { IsValid = false, License = null });

        var status = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.Invalid, status.Availability);
        Assert.False(status.FeaturesPermitted);
    }

    /// <summary>
    /// REQ-FN-015: after a successful validation caches the payload, an outage within the grace
    /// window is served from the cache and features remain permitted.
    /// </summary>
    [Fact]
    public async Task OutageWithinGraceHonorsCachedLicense()
    {
        var (service, client, _, time) = Build(graceHours: 72);
        client.OnValidateLicense = (_, _) => Task.FromResult(ValidLicense());

        // First: a good validation seeds the cache.
        await service.ValidateAsync();

        // Then AppManager goes unreachable 24h later (within the 72h window).
        time.Advance(TimeSpan.FromHours(24));
        client.OnValidateLicense = (_, _) => throw new HttpRequestException("connection refused");

        var status = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.Cached, status.Availability);
        Assert.Equal("Professional", status.LicenseName);
        Assert.True(status.FeaturesPermitted);
        Assert.True(status.IsFromCache);
    }

    /// <summary>REQ-FN-015: past the grace window the cached license is no longer honored.</summary>
    [Fact]
    public async Task OutagePastGraceDegrades()
    {
        var (service, client, _, time) = Build(graceHours: 72);
        client.OnValidateLicense = (_, _) => Task.FromResult(ValidLicense());
        await service.ValidateAsync();

        // 73h later — one hour past the grace window — AppManager still unreachable.
        time.Advance(TimeSpan.FromHours(73));
        client.OnValidateLicense = (_, _) => throw new HttpRequestException("connection refused");

        var status = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.GraceExpired, status.Availability);
        Assert.False(status.FeaturesPermitted);
    }

    /// <summary>A 5xx from AppManager is treated as an outage (grace path), not an invalid license.</summary>
    [Fact]
    public async Task ServerErrorFallsBackToCache()
    {
        var (service, client, _, time) = Build(graceHours: 48);
        client.OnValidateLicense = (_, _) => Task.FromResult(ValidLicense());
        await service.ValidateAsync();

        time.Advance(TimeSpan.FromHours(1));
        client.OnValidateLicense = (_, _) =>
            throw new AppManagerException("INTERNAL_ERROR", "boom", 500);

        var status = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.Cached, status.Availability);
    }

    /// <summary>An outage with no cached license degrades immediately (nothing to honor).</summary>
    [Fact]
    public async Task OutageWithoutCacheDegrades()
    {
        var (service, client, _, _) = Build();
        client.OnValidateLicense = (_, _) => throw new HttpRequestException("connection refused");

        var status = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.GraceExpired, status.Availability);
        Assert.False(status.FeaturesPermitted);
    }

    /// <summary>EnsureFreshAsync validates once, then serves the cached status until the interval elapses.</summary>
    [Fact]
    public async Task EnsureFreshRevalidatesOnlyAfterInterval()
    {
        var (service, client, _, time) = Build(revalidateMinutes: 60);
        client.OnValidateLicense = (_, _) => Task.FromResult(ValidLicense());

        await service.EnsureFreshAsync();
        await service.EnsureFreshAsync();          // within interval — no new call
        Assert.Equal(1, client.ValidateLicenseCalls);

        time.Advance(TimeSpan.FromMinutes(61));     // interval elapsed
        await service.EnsureFreshAsync();
        Assert.Equal(2, client.ValidateLicenseCalls);
    }

    /// <summary>The cached payload round-trips through the repository as camelCase JSON.</summary>
    [Fact]
    public async Task SuccessfulValidationWritesCache()
    {
        var (service, client, cache, _) = Build();
        client.OnValidateLicense = (_, _) => Task.FromResult(ValidLicense());

        await service.ValidateAsync();

        var row = await cache.GetAsync("123");
        Assert.NotNull(row);
        var parsed = JsonSerializer.Deserialize<LicenseValidationData>(row!.PayloadJson, JsonOptions);
        Assert.Equal("Professional", parsed!.License!.LicenseName);
    }
}
