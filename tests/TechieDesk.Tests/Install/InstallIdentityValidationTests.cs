using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.AppManager;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Services.Auth;
using TechieDesk.Services.Install;
using TechieDesk.Services.Licensing;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Install;

/// <summary>
/// REQ-FN-051 clause 2 (BRD-143) — the CLIENT half only: the install identity is computed and made
/// available to licence validation, behind a flag that ships OFF because AppManager has no
/// documented endpoint that consumes it. Also the BRD-129 guarantee that none of this touches an
/// account-free install.
/// </summary>
public sealed class InstallIdentityValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

    private static TechieDeskUser User() => new(123, "jane@example.com", "Jane Doe", ProductRole.User, true);

    private static (LicenseService service, FakeAppManagerClient client, CountingInstallIdentityProvider identity)
        Build(bool appManagerEnabled = true, bool sendInstallIdentity = false)
    {
        var client = new FakeAppManagerClient();
        var identity = new CountingInstallIdentityProvider();
        var store = new SessionTokenStore();
        store.SetSession(User(), "access-1", "refresh-1", Now.AddHours(1));

        var service = new LicenseService(
            client,
            new InMemoryLicenseCacheRepository(),
            TestFactory.Mode(appManagerEnabled),
            new StubUserContext(User()),
            store,
            new StubTokenRefresher(),
            Options.Create(new LicensingOptions { SendInstallIdentity = sendInstallIdentity }),
            new FixedTimeProvider(Now),
            NullLogger<LicenseService>.Instance,
            identity);

        return (service, client, identity);
    }

    private static LicenseValidationData Valid() => new()
    {
        IsValid = true,
        License = new ActiveLicenseData
        {
            LicenseId = 1,
            LicenseName = "Professional",
            Status = "Active",
            ExpiryDate = Now.AddDays(200),
            DaysRemaining = 200
        }
    };

    /// <summary>
    /// The stock install sends NOTHING new. Clause 2's server contract does not exist, so the
    /// default request must be byte-for-byte the pre-REQ-FN-051 one.
    /// </summary>
    [Fact]
    public async Task ValidationSendsNoInstallIdentityByDefault()
    {
        var (service, client, identity) = Build();
        client.OnValidateLicense = (_, _) => Task.FromResult(Valid());

        await service.ValidateAsync();

        Assert.Null(client.LastInstallId);
        Assert.Equal(0, identity.Resolutions);
    }

    /// <summary>
    /// With the flag on, validation presents the composite install id — the value AppManager would
    /// bind a seat to once it has somewhere to put it.
    /// </summary>
    [Fact]
    public async Task ValidationPresentsTheInstallIdentityWhenEnabled()
    {
        var (service, client, identity) = Build(sendInstallIdentity: true);
        client.OnValidateLicense = (_, _) => Task.FromResult(Valid());

        await service.ValidateAsync();

        Assert.Equal(identity.Current.CompositeId, client.LastInstallId);
        Assert.NotEqual(identity.Current.InstallId, client.LastInstallId);
    }

    /// <summary>
    /// <b>BRD-129 regression guard.</b> An install with no AppManager configured never resolves an
    /// install identity at all — no fingerprint probe, no file written, nothing on the launch path
    /// of an account-free user. The counter, not a flag, is what proves it.
    /// </summary>
    [Fact]
    public async Task AnOfflineAccountFreeInstallNeverComputesAnInstallIdentity()
    {
        var (service, client, identity) = Build(appManagerEnabled: false, sendInstallIdentity: true);

        var status = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.Offline, status.Availability);
        Assert.Equal(0, client.ValidateLicenseCalls);
        Assert.Equal(0, identity.Resolutions);
    }

    /// <summary>
    /// An install identity that cannot be computed degrades to sending nothing rather than failing
    /// the validation — a machine that will not answer a fingerprint probe must still be licensable.
    /// </summary>
    [Fact]
    public async Task AFailingInstallIdentityDoesNotFailValidation()
    {
        var client = new FakeAppManagerClient();
        client.OnValidateLicense = (_, _) => Task.FromResult(Valid());
        var store = new SessionTokenStore();
        store.SetSession(User(), "access-1", "refresh-1", Now.AddHours(1));

        var service = new LicenseService(
            client,
            new InMemoryLicenseCacheRepository(),
            TestFactory.Mode(appManagerEnabled: true),
            new StubUserContext(User()),
            store,
            new StubTokenRefresher(),
            Options.Create(new LicensingOptions { SendInstallIdentity = true }),
            new FixedTimeProvider(Now),
            NullLogger<LicenseService>.Instance,
            new ThrowingInstallIdentityProvider());

        var status = await service.ValidateAsync();

        Assert.Equal(LicenseAvailability.Live, status.Availability);
        Assert.Null(client.LastInstallId);
    }

    /// <summary>
    /// On the wire the identity travels as the <c>X-Install-Id</c> HEADER and the request body stays
    /// absent — the guide specifies <c>POST /LicenseSvc/validate</c> as taking no body, so adding one
    /// is the change that could break the live contract.
    /// </summary>
    [Fact]
    public async Task TheIdentityTravelsAsAHeaderAndAddsNoRequestBody()
    {
        var handler = ValidateHandler();
        var client = TestFactory.Client(handler);

        await client.ValidateLicenseAsync("access-token-1", "composite-install-id");

        var call = handler.Calls.Single();
        Assert.Equal("composite-install-id", call.Headers[AppManagerClient.InstallIdentityHeaderName]);
        Assert.Null(call.Body);
        Assert.Equal("/LicenseSvc/validate?aApplicationId=7", call.PathAndQuery);
    }

    /// <summary>
    /// Without an identity the request carries no such header at all, so an install running against
    /// today's AppManager is indistinguishable from one built before this requirement.
    /// </summary>
    [Fact]
    public async Task NoHeaderIsSentWhenThereIsNoIdentity()
    {
        var handler = ValidateHandler();
        var client = TestFactory.Client(handler);

        await client.ValidateLicenseAsync("access-token-1");

        var call = handler.Calls.Single();
        Assert.False(call.Headers.ContainsKey(AppManagerClient.InstallIdentityHeaderName));
        Assert.Null(call.Body);
    }

    private static StubHttpMessageHandler ValidateHandler()
    {
        return new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            "{\"success\":true,\"data\":{\"isValid\":true,\"license\":{\"licenseId\":1,"
                + "\"licenseName\":\"Professional\",\"status\":\"Active\"}},\"message\":\"ok\"}"));
    }
}
