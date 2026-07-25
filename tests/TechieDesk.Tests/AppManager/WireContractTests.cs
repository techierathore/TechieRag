using System.Net;
using TechieDesk.Services.AppManager;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.AppManager;

/// <summary>
/// REQ-FN-004 / BRD-21: wire-contract details — API key headers on every call, bearer tokens,
/// and the v1.4 a-prefixed URL parameter names.
/// </summary>
public sealed class WireContractTests : IDisposable
{
    private readonly RsaKeyFixture keys = new();

    /// <inheritdoc />
    public void Dispose()
    {
        keys.Dispose();
    }

    private StubHttpMessageHandler OkHandler()
    {
        return new StubHttpMessageHandler((request, body) =>
            request.RequestUri!.AbsolutePath switch
            {
                "/AuthSvc/public-key" => StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.PublicKeyResponse(keys.PublicKeyPem)),
                "/AuthSvc/login" => StubHttpMessageHandler.Json(HttpStatusCode.OK, TestFactory.LoginResponse()),
                _ => StubHttpMessageHandler.Json(HttpStatusCode.OK,
                    "{\"success\":true,\"data\":{\"isValid\":true,\"featureCode\":\"AGENTS\",\"hasAccess\":true,\"requestId\":\"r1\",\"userId\":123,\"email\":\"jane.doe@example.com\",\"firstName\":\"Jane\",\"lastName\":\"Doe\",\"accessToken\":\"a\",\"refreshToken\":\"r\"},\"message\":\"ok\"}")
            });
    }

    /// <summary>
    /// Every call carries the X-Api-Key and X-Api-Secret headers, including anonymous ones
    /// (public-key, login) and bearer-authenticated ones (profile).
    /// </summary>
    [Fact]
    public async Task ApiKeyHeadersSentOnEveryCall()
    {
        var handler = OkHandler();
        var client = TestFactory.Client(handler);

        await client.LoginAsync("jane.doe@example.com", "P@ssw0rd!");
        await client.GetProfileAsync("access-token-1");

        Assert.NotEmpty(handler.Calls);
        Assert.All(handler.Calls, call =>
        {
            Assert.Equal("ak_test_key", call.Headers["X-Api-Key"]);
            Assert.Equal("test_secret", call.Headers["X-Api-Secret"]);
        });
    }

    /// <summary>
    /// Authenticated endpoints carry the Authorization: Bearer header with the supplied
    /// access token.
    /// </summary>
    [Fact]
    public async Task BearerTokenSentOnAuthenticatedCalls()
    {
        var handler = OkHandler();
        var client = TestFactory.Client(handler);

        await client.GetProfileAsync("access-token-1");

        var call = handler.Calls.Single();
        Assert.Equal("Bearer access-token-1", call.Headers["Authorization"]);
    }

    /// <summary>
    /// License validation uses the v1.4 a-prefixed query parameter name aApplicationId when an
    /// explicit ApplicationId is configured.
    /// </summary>
    [Fact]
    public async Task LicenseValidateUsesAPrefixedParam()
    {
        var handler = OkHandler();
        var client = TestFactory.Client(handler);

        await client.ValidateLicenseAsync("access-token-1");

        var call = handler.Calls.Single();
        Assert.Equal("/LicenseSvc/validate?aApplicationId=7", call.PathAndQuery);
    }

    /// <summary>
    /// Feature checks hit the a-prefixed route template GET /FeatureSvc/{aFeatureCode} with the
    /// code substituted into the path.
    /// </summary>
    [Fact]
    public async Task FeatureCheckUsesRouteCode()
    {
        var handler = OkHandler();
        var client = TestFactory.Client(handler);

        var access = await client.CheckFeatureAsync("access-token-1", "AGENTS");

        Assert.True(access.HasAccess);
        Assert.Equal("/FeatureSvc/AGENTS", handler.Calls.Single().PathAndQuery);
    }

    /// <summary>
    /// Logout posts the refresh token and the logoutAllDevices flag under bearer auth.
    /// </summary>
    [Fact]
    public async Task LogoutSendsAllDevicesFlag()
    {
        var handler = OkHandler();
        var client = TestFactory.Client(handler);

        await client.LogoutAsync("access-token-1", "refresh-token-1", logoutAllDevices: true);

        var call = handler.Calls.Single(recorded => recorded.PathAndQuery == "/AuthSvc/logout");
        Assert.Contains("\"logoutAllDevices\":true", call.Body);
        Assert.Contains("\"refreshToken\":\"refresh-token-1\"", call.Body);
    }

    /// <summary>
    /// When no AppManager base URL is configured, any client call fails fast with the typed
    /// NotConfigured error instead of attempting network traffic (offline mode, BRD-54).
    /// </summary>
    [Fact]
    public async Task UnconfiguredClientThrowsNotConfigured()
    {
        var handler = OkHandler();
        var client = TestFactory.Client(handler, new AppManagerOptions { BaseUrl = string.Empty });

        var exception = await Assert.ThrowsAsync<AppManagerException>(
            () => client.GetProfileAsync("access-token-1"));

        Assert.Equal(AppManagerError.NotConfigured, exception.Error);
        Assert.Empty(handler.Calls);
    }
}
