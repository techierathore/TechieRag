using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Auth;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-002 / BRD-15: silent access-token refresh ahead of expiry, server-side-only tokens,
/// and sign-out on refresh failure.
/// </summary>
public sealed class TokenRefreshTests
{
    private static TechieDeskUser SessionUser()
    {
        return new TechieDeskUser(123, "jane.doe@example.com", "Jane Doe", ProductRole.User, true);
    }

    private static TokenRefresher Refresher(StubHttpMessageHandler handler, SessionTokenStore store, bool appManagerEnabled = true)
    {
        var options = appManagerEnabled
            ? TestFactory.DefaultOptions()
            : new TechieDesk.Services.AppManager.AppManagerOptions { BaseUrl = string.Empty };
        return new TokenRefresher(
            TestFactory.Client(handler, options),
            store,
            TestFactory.Mode(appManagerEnabled),
            Options.Create(options),
            NullLogger<TokenRefresher>.Instance);
    }

    /// <summary>
    /// A token expiring within the lead window is silently refreshed via POST /AuthSvc/refresh
    /// and the store receives the new token pair.
    /// </summary>
    [Fact]
    public async Task NearExpiryTokenIsRefreshed()
    {
        var handler = new StubHttpMessageHandler((request, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.RefreshResponse()));
        var store = new SessionTokenStore();
        store.SetSession(SessionUser(), "access-token-1", "refresh-token-1", DateTimeOffset.UtcNow.AddSeconds(30));

        var result = await Refresher(handler, store).EnsureValidTokenAsync();

        Assert.True(result);
        Assert.Equal("access-token-2", store.AccessToken);
        Assert.Equal("refresh-token-2", store.RefreshToken);
        var call = handler.Calls.Single();
        Assert.Equal("/AuthSvc/refresh", call.PathAndQuery);
        Assert.Contains("\"refreshToken\":\"refresh-token-1\"", call.Body);
    }

    /// <summary>A token comfortably outside the lead window triggers no HTTP call.</summary>
    [Fact]
    public async Task ValidTokenSkipsRefresh()
    {
        var handler = new StubHttpMessageHandler((request, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.RefreshResponse()));
        var store = new SessionTokenStore();
        store.SetSession(SessionUser(), "access-token-1", "refresh-token-1", DateTimeOffset.UtcNow.AddHours(1));

        var result = await Refresher(handler, store).EnsureValidTokenAsync();

        Assert.True(result);
        Assert.Empty(handler.Calls);
        Assert.Equal("access-token-1", store.AccessToken);
    }

    /// <summary>
    /// A failed refresh (expired refresh token) clears the session so route protection sends
    /// the user to /login, and reports false to the caller.
    /// </summary>
    [Fact]
    public async Task RefreshFailureClearsSession()
    {
        var handler = new StubHttpMessageHandler((request, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized,
                TestFactory.ErrorResponse("EXPIRED_REFRESH_TOKEN", "Refresh token has expired", 401)));
        var store = new SessionTokenStore();
        store.SetSession(SessionUser(), "access-token-1", "refresh-token-1", DateTimeOffset.UtcNow.AddSeconds(5));

        var result = await Refresher(handler, store).EnsureValidTokenAsync();

        Assert.False(result);
        Assert.False(store.HasSession);
        Assert.Null(store.User);
    }

    /// <summary>Without any session, EnsureValidTokenAsync reports false and makes no call.</summary>
    [Fact]
    public async Task NoSessionReportsFalse()
    {
        var handler = new StubHttpMessageHandler((request, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.RefreshResponse()));

        var result = await Refresher(handler, new SessionTokenStore()).EnsureValidTokenAsync();

        Assert.False(result);
        Assert.Empty(handler.Calls);
    }

    /// <summary>In offline mode the refresher is a no-op that always reports a valid session.</summary>
    [Fact]
    public async Task OfflineModeAlwaysValid()
    {
        var handler = new StubHttpMessageHandler((request, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.RefreshResponse()));

        var result = await Refresher(handler, new SessionTokenStore(), appManagerEnabled: false).EnsureValidTokenAsync();

        Assert.True(result);
        Assert.Empty(handler.Calls);
    }
}
