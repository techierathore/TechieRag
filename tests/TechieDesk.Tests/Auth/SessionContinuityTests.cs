using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services.Auth;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-032 acceptance, expressed against the pieces that used to break it: a NEW scope (what a
/// login redirect or an F5 created) adopts the session from the handle it is handed, the route guard
/// then lets it stay on the requested route, and offline single-user mode is unaffected.
/// </summary>
/// <remarks>
/// REQ-FN-035 swapped the implementation under test from the HttpContext-backed <c>SessionContext</c>
/// to <see cref="DesktopSessionContext"/>. Every assertion here is about the <see cref="ISessionContext"/>
/// contract — handle adoption, session resolution, staleness — which is identical across both, so the
/// coverage carries over rather than being dropped. What these tests no longer prove is the *browser*
/// half of the original defect (a cookie surviving circuit destruction); the desktop head has neither
/// cookies nor circuits, so that failure mode is gone by construction rather than guarded against.
/// </remarks>
public sealed class SessionContinuityTests
{
    private static (TechieDeskAuthenticationStateProvider Provider, ISessionContext Context) NewCircuit(
        ISessionStore store)
    {
        // A brand-new scope starts with NOTHING attached — exactly the state the old per-circuit
        // store was left in after NavigateTo(forceLoad: true).
        var context = new DesktopSessionContext(store);
        var provider = new TechieDeskAuthenticationStateProvider(
            context, NullLogger<TechieDeskAuthenticationStateProvider>.Instance);
        return (provider, context);
    }

    private static Task<AuthenticationState> HandlePrincipal(string? handle)
    {
        var identity = string.IsNullOrEmpty(handle)
            ? new ClaimsIdentity()
            : new ClaimsIdentity(
                new[] { new Claim(SessionCookie.HandleClaimType, handle) }, SessionCookie.AuthenticationScheme);
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    /// <summary>
    /// THE regression: a replacement scope handed a principal adopts the session THAT handle points
    /// at, not some other one and not an anonymous state.
    /// </summary>
    /// <remarks>
    /// REQ-FN-039 rewrote the setup, not the subject. This used to open with
    /// <c>Assert.False(context.Tokens.HasSession)</c> — a fresh scope holds nothing until it is told
    /// what to adopt. That premise is deliberately reversed now: a fresh scope restores whatever the
    /// OS credential store is holding, which is the "a restart restores the session" clause, covered
    /// directly in <c>SecretStoreSessionTests</c>. Adoption is therefore asserted the stronger way —
    /// with TWO live sessions in the store, only one of which the principal names — so the test
    /// cannot pass by accident on either path.
    /// </remarks>
    [Fact]
    public void ReplacementCircuitAdoptsTheSession()
    {
        var store = SessionTestHarness.Store();
        var otherHandle = SessionTestHarness.SignIn(store, SessionTestHarness.User(456));
        var handle = SessionTestHarness.SignIn(store, SessionTestHarness.User(123));
        var (provider, context) = NewCircuit(store);

        provider.SetAuthenticationState(HandlePrincipal(otherHandle));
        Assert.Equal(456, context.Tokens.User!.UserId);

        provider.SetAuthenticationState(HandlePrincipal(handle));

        Assert.Equal(handle, context.Handle);
        Assert.True(context.Tokens.HasSession);
        Assert.Equal(123, context.Tokens.User!.UserId);
    }

    /// <summary>
    /// Adoption is visible to the route guard as a change of SIGN-IN state, not of access:
    /// <c>/workspace/acme</c> is served either way. REQ-FN-036 rewrote this from
    /// "RouteGuardStopsRedirectingOnceTheSessionIsAdopted", which asserted that the same scope was
    /// bounced to <c>/login?returnUrl=%2Fworkspace%2Facme</c> before adoption — a gate that no
    /// longer exists, since sign-in activates a licence rather than unlocking local data.
    /// REQ-FN-039 then moved the "before" state from a fresh scope to an explicitly signed-out one,
    /// because a fresh scope now restores the stored session instead of starting empty. REQ-FN-041
    /// dropped the paired <c>GetRedirect</c> assertions: with the capability matrix deleted there is
    /// no access question left to ask, which makes the "not of access" half true by construction.
    /// </summary>
    [Fact]
    public void SessionAdoptionChangesSignInStateNotAccess()
    {
        var store = SessionTestHarness.Store();
        var handle = SessionTestHarness.SignIn(store, SessionTestHarness.User());
        var (provider, context) = NewCircuit(store);
        var guard = Guard(context);

        provider.SetAuthenticationState(HandlePrincipal(null));
        var beforeAdoption = guard.IsSignedIn;
        provider.SetAuthenticationState(HandlePrincipal(handle));

        Assert.False(beforeAdoption);
        Assert.True(guard.IsSignedIn);
    }

    /// <summary>
    /// A scope handed a principal with no handle stays signed out — and still reaches every route.
    /// REQ-FN-036 rewrote this from "CircuitWithoutAHandleStaysAnonymous", whose final assertion
    /// was a <c>/login?returnUrl=%2Fsettings</c> redirect.
    /// </summary>
    [Fact]
    public void ScopeWithoutAHandleStaysSignedOutButNotGated()
    {
        var store = SessionTestHarness.Store();
        var (provider, context) = NewCircuit(store);

        provider.SetAuthenticationState(HandlePrincipal(null));

        Assert.Null(context.Handle);
        Assert.False(context.Tokens.HasSession);
        Assert.False(Guard(context).IsSignedIn);
    }

    /// <summary>
    /// A circuit presenting a handle whose session was invalidated (logout elsewhere, expiry) is
    /// anonymous again — the cookie alone proves nothing.
    /// </summary>
    [Fact]
    public void StaleHandleDoesNotRestoreASession()
    {
        var store = SessionTestHarness.Store();
        var handle = SessionTestHarness.SignIn(store, SessionTestHarness.User());
        store.Invalidate(handle);
        var (provider, context) = NewCircuit(store);

        provider.SetAuthenticationState(HandlePrincipal(handle));

        Assert.False(context.Tokens.HasSession);
    }

    /// <summary>
    /// Account-free operation is completely unaffected by the session machinery: no handle, no
    /// stored session, no redirect — the local owner is the built-in Admin (BRD-54/BRD-129).
    /// REQ-FN-036 replaced this test's <c>GetLoginRedirect("/settings")</c> assertion (and its
    /// offline-only framing) with a capability check, because the login redirect is gone and the
    /// behaviour asserted here is now the NORMAL path rather than an offline exception. REQ-FN-041
    /// then reduced that capability check to a sign-in-state check, the only question the route
    /// guard still answers.
    /// </summary>
    [Fact]
    public async Task AccountFreeUseIsUnaffectedBySessionHandles()
    {
        var store = SessionTestHarness.Store();
        var (provider, context) = NewCircuit(store);

        provider.SetAuthenticationState(HandlePrincipal(null));
        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.True(state.User.IsInRole(nameof(ProductRole.Admin)));
        Assert.False(Guard(context).IsSignedIn);
        Assert.Equal(0, store.ActiveSessionCount);
    }

    private static RouteGuard Guard(ISessionContext context)
    {
        return new RouteGuard(context);
    }
}
