using System.Reflection;
using TechieDesk.Services.Auth;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-036 / BRD-129 + REQ-FN-041: account-free launch, and no access decision left anywhere in
/// the route guard. It has no login redirect, and since the role/capability matrix was deleted it
/// has no redirect of any kind — only a sign-in-state report the shell uses for its menu.
/// </summary>
/// <remarks>
/// These tests replace the REQ-FN-003 / BRD-20 suite that asserted an unauthenticated visitor on an
/// AppManager-configured install was sent to <c>/login?returnUrl={deep link}</c>. REQ-FN-041 then
/// retired two further cases outright — <c>UnderPrivilegedUserSentToDenied</c> and
/// <c>PrivilegedUserPassesCapabilityRoute</c> — because their subject, <c>GetRedirect(Capability)</c>,
/// no longer exists: one install serves one person, who is always the local owner, so there is no
/// under-privileged caller to divert to <c>/denied</c>.
/// </remarks>
public sealed class RouteGuardTests
{
    private static RouteGuard Guard(TechieDeskUser? sessionUser)
    {
        var sessions = SessionTestHarness.Store();
        var handle = sessionUser is null ? null : SessionTestHarness.SignIn(sessions, sessionUser);
        return new RouteGuard(SessionTestHarness.Circuit(sessions, handle));
    }

    /// <summary>
    /// THE acceptance: a first launch with no AppManager configuration and nobody signed in reaches
    /// every route. The guard exposes nothing that could send the user anywhere.
    /// </summary>
    [Fact]
    public void LaunchNeverRedirectsAnywhere()
    {
        var guard = Guard(null);

        Assert.False(guard.IsSignedIn);
        Assert.DoesNotContain(
            typeof(IRouteGuard).GetMembers(BindingFlags.Public | BindingFlags.Instance),
            member => member.Name.Contains("Redirect", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The re-expression of the old "AnonymousRedirectedWithReturnUrl" case, which asserted that a
    /// configured-AppManager install bounced an unauthenticated visitor to /login. A configured
    /// install can no longer behave differently at all: the guard has no auth-mode dependency left
    /// to branch on, so having AppManager set up is not a state the routing layer can observe.
    /// </summary>
    [Fact]
    public void ConfiguredInstanceCannotBehaveDifferently()
    {
        var parameters = typeof(RouteGuard).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(
            parameters, parameter => parameter.ParameterType == typeof(ITechieDeskAuthModeProvider));

        Assert.False(Guard(null).IsSignedIn);
    }

    /// <summary>
    /// Structural guarantee: the gate cannot come back by flipping a condition. No member of
    /// <see cref="IRouteGuard"/> mentions login, and after REQ-FN-041 none mentions a capability
    /// either — reinstating either turns this test red.
    /// </summary>
    [Fact]
    public void GuardExposesNoAccessDecision()
    {
        var members = typeof(IRouteGuard).GetMembers(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(
            members, member => member.Name.Contains("Login", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            members, member => member.Name.Contains("Capability", StringComparison.OrdinalIgnoreCase));

        // The whole contract is one read-only property. Anything else is an access decision.
        var property = Assert.Single(typeof(IRouteGuard).GetProperties(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(nameof(IRouteGuard.IsSignedIn), property.Name);
        Assert.DoesNotContain(
            typeof(IRouteGuard).GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => !method.IsSpecialName);
    }

    /// <summary>
    /// Sign-in state is reported from the SESSION, not from the auth mode, so the shell can offer
    /// "Sign in" versus "Log out" without that answer ever gating a route.
    /// </summary>
    [Fact]
    public void SignedInSessionIsReportedSignedIn()
    {
        var user = new TechieDeskUser(123, "jane.doe@example.com", "Jane Doe", ProductRole.Admin, true);

        Assert.True(Guard(user).IsSignedIn);
        Assert.False(Guard(null).IsSignedIn);
    }
}
