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
/// REQ-FN-014/BRD-50: feature gating via FeatureSvc (binary + level), offline Free-tier gating,
/// and grace-expired degradation (REQ-FN-015).
/// </summary>
public sealed class FeatureGateTests
{
    private static TechieDeskUser User() => new(123, "jane@example.com", "Jane Doe", ProductRole.User, true);

    private static FeatureGateService Build(
        FakeAppManagerClient client,
        bool appManagerEnabled = true,
        bool withSession = true,
        LicenseStatus? current = null,
        LicensingOptions? options = null)
    {
        var store = new SessionTokenStore();
        if (withSession)
        {
            store.SetSession(User(), "access-1", "refresh-1", DateTimeOffset.UtcNow.AddHours(1));
        }

        return new FeatureGateService(
            client,
            TestFactory.Mode(appManagerEnabled),
            store,
            new StubTokenRefresher(),
            new FakeLicenseService(current ?? LicenseStatus.Offline),
            Options.Create(options ?? new LicensingOptions()),
            NullLogger<FeatureGateService>.Instance);
    }

    /// <summary>AppManager grants access to a binary feature.</summary>
    [Fact]
    public async Task AppManagerGrantsBinaryFeature()
    {
        var client = new FakeAppManagerClient
        {
            OnCheckFeature = (_, code, _) => Task.FromResult(new FeatureAccessData
            {
                FeatureCode = code, FeatureType = "Binary", HasAccess = true, Source = "license"
            })
        };
        var gate = Build(client, current: LiveStatus());

        var decision = await gate.EvaluateAsync("CONNECTORS");

        Assert.True(decision.IsEnabled);
    }

    /// <summary>A level feature returns its granted level.</summary>
    [Fact]
    public async Task AppManagerReturnsFeatureLevel()
    {
        var client = new FakeAppManagerClient
        {
            OnCheckFeature = (_, code, _) => Task.FromResult(new FeatureAccessData
            {
                FeatureCode = code, FeatureType = "Level", HasAccess = true, Level = 10000
            })
        };
        var gate = Build(client, current: LiveStatus());

        var level = await gate.GetLevelAsync("API_ACCESS");

        Assert.Equal(10000, level);
    }

    /// <summary>A denied feature carries the required license tier for the upgrade prompt.</summary>
    [Fact]
    public async Task AppManagerDeniesWithRequiredLicense()
    {
        var client = new FakeAppManagerClient
        {
            OnCheckFeature = (_, code, _) => Task.FromResult(new FeatureAccessData
            {
                FeatureCode = code, HasAccess = false, RequiredLicense = "Enterprise", Reason = "Upgrade required"
            })
        };
        var gate = Build(client, current: LiveStatus());

        var decision = await gate.EvaluateAsync("WHITE_LABEL");

        Assert.False(decision.IsEnabled);
        Assert.Equal("Enterprise", decision.RequiredLicense);
        Assert.Equal("Upgrade required", decision.Reason);
    }

    /// <summary>Offline mode gates premium features (Free tier) and allows everything else.</summary>
    [Theory]
    [InlineData("CONNECTORS", false)]
    [InlineData("AGENTS", false)]
    [InlineData("API_ACCESS", false)]
    [InlineData("RAG_CHAT", true)]
    [InlineData("DOCUMENT_INGEST", true)]
    public async Task OfflineModeGatesPremiumFeatures(string feature, bool expectedEnabled)
    {
        var client = new FakeAppManagerClient();   // must not be called in offline mode
        var gate = Build(client, appManagerEnabled: false);

        var decision = await gate.EvaluateAsync(feature);

        Assert.Equal(expectedEnabled, decision.IsEnabled);
        if (!expectedEnabled)
        {
            Assert.Equal("Professional", decision.RequiredLicense);
        }
    }

    /// <summary>REQ-FN-015: once the grace window has expired, premium features are denied.</summary>
    [Fact]
    public async Task GraceExpiredDeniesFeature()
    {
        var client = new FakeAppManagerClient();   // FeatureSvc not consulted when grace expired
        var graceExpired = new LicenseStatus { Availability = LicenseAvailability.GraceExpired };
        var gate = Build(client, current: graceExpired);

        var decision = await gate.EvaluateAsync("CONNECTORS");

        Assert.False(decision.IsEnabled);
    }

    /// <summary>
    /// When FeatureSvc is unreachable but the license is cached within grace, access is honored
    /// so a transient outage does not lock a licensed feature.
    /// </summary>
    [Fact]
    public async Task FeatureSvcOutageHonoredWithinGrace()
    {
        var client = new FakeAppManagerClient
        {
            OnCheckFeature = (_, _, _) => throw new HttpRequestException("connection refused")
        };
        var cached = new LicenseStatus { Availability = LicenseAvailability.Cached, LicenseName = "Professional", Status = "Active" };
        var gate = Build(client, current: cached);

        var decision = await gate.EvaluateAsync("CONNECTORS");

        Assert.True(decision.IsEnabled);
    }

    private static LicenseStatus LiveStatus() => new()
    {
        Availability = LicenseAvailability.Live,
        LicenseName = "Professional",
        Status = "Active"
    };
}
