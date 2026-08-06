using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Auth;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// The custom AuthenticationStateProvider: the local owner (built-in Admin) until an AppManager
/// account is signed in for licensing, then that account with its mapped role as a role claim.
/// </summary>
/// <remarks>
/// REQ-FN-036 / BRD-129: the provider no longer produces an anonymous principal. Two tests below
/// asserted that an AppManager-configured install started (and reverted to) an UNAUTHENTICATED
/// state — the identity half of the anonymous-vs-authenticated split. That is wrong by design now:
/// one desktop install is operated by its owner, and signing in activates a licence rather than
/// creating the right to use the app. Both are re-expressed rather than dropped.
/// </remarks>
public sealed class AuthStateProviderTests
{
    private static TechieDeskAuthenticationStateProvider Provider(ISessionStore store, string? handle)
    {
        return new TechieDeskAuthenticationStateProvider(
            SessionTestHarness.Circuit(store, handle),
            NullLogger<TechieDeskAuthenticationStateProvider>.Instance);
    }

    /// <summary>
    /// With no AppManager configured every scope is authenticated as the built-in Admin without a
    /// login — the launch state REQ-FN-036 requires to be a working one.
    /// </summary>
    [Fact]
    public async Task AccountFreeStateIsAdmin()
    {
        var provider = Provider(SessionTestHarness.Store(), null);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.True(state.User.IsInRole(nameof(ProductRole.Admin)));
    }

    /// <summary>
    /// Re-expresses "AppManagerModeStartsAnonymous": a scope with no session handle is the local
    /// owner, not an anonymous visitor. Configuring AppManager does not create a signed-out state
    /// the app has to be rescued from.
    /// </summary>
    [Fact]
    public async Task NoSessionResolvesToTheLocalOwner()
    {
        var provider = Provider(SessionTestHarness.Store(), null);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.Equal(TechieDeskUser.BuiltInAdmin.Email, state.User.FindFirstValue(ClaimTypes.Email));
    }

    /// <summary>
    /// A circuit holding a live handle exposes the app-scoped role mapped at login (BRD-23) and
    /// the session's email, while the tokens stay in the server-side store.
    /// </summary>
    [Fact]
    public async Task SessionHandleExposesMappedRole()
    {
        var store = SessionTestHarness.Store();
        var user = new TechieDeskUser(123, "jane.doe@example.com", "Jane Doe", ProductRole.Manager, true);
        var handle = SessionTestHarness.SignIn(store, user);
        var provider = Provider(store, handle);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.True(state.User.IsInRole(nameof(ProductRole.Manager)));
        Assert.Equal("jane.doe@example.com", state.User.FindFirstValue(ClaimTypes.Email));
        Assert.Equal("access-token-1", store.Resolve(handle)!.AccessToken);
    }

    /// <summary>
    /// Re-expresses "InvalidatedSessionBecomesAnonymous": invalidating the session drops the
    /// AppManager identity and falls back to the local owner. The user keeps working on their own
    /// data; what they lose is the licence, not the app.
    /// </summary>
    [Fact]
    public async Task InvalidatedSessionFallsBackToTheLocalOwner()
    {
        var store = SessionTestHarness.Store();
        var handle = SessionTestHarness.SignIn(store, SessionTestHarness.User());
        var provider = Provider(store, handle);

        store.Invalidate(handle);
        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.Equal(TechieDeskUser.BuiltInAdmin.Email, state.User.FindFirstValue(ClaimTypes.Email));
    }
}
