using TechieDesk.Services.Auth;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-003 / BRD-20: route protection — unauthenticated visitors are redirected to
/// /login?returnUrl={deep link}; offline mode never redirects.
/// </summary>
public sealed class RouteGuardTests
{
    private static RouteGuard Guard(bool appManagerEnabled, TechieDeskUser? sessionUser)
    {
        var store = new SessionTokenStore();
        if (sessionUser != null)
        {
            store.SetSession(sessionUser, "access-token-1", "refresh-token-1", DateTimeOffset.UtcNow.AddHours(1));
        }

        var mode = TestFactory.Mode(appManagerEnabled);
        var context = new TechieDeskUserContext(mode, store);
        return new RouteGuard(mode, context, new CapabilityService());
    }

    /// <summary>
    /// An unauthenticated visitor hitting a protected deep link is redirected to /login with
    /// the originally requested route (including its query string) preserved in returnUrl.
    /// </summary>
    [Fact]
    public void AnonymousRedirectedWithReturnUrl()
    {
        var guard = Guard(true, null);

        var redirect = guard.GetLoginRedirect("/settings?tab=providers");

        Assert.Equal("/login?returnUrl=%2Fsettings%3Ftab%3Dproviders", redirect);
    }

    /// <summary>An authenticated user is not redirected from a protected route.</summary>
    [Fact]
    public void AuthenticatedUserNotRedirected()
    {
        var user = new TechieDeskUser(123, "jane.doe@example.com", "Jane Doe", ProductRole.User, true);
        var guard = Guard(true, user);

        Assert.Null(guard.GetLoginRedirect("/chat"));
        Assert.True(guard.IsAuthenticated);
    }

    /// <summary>
    /// In offline single-user mode nothing requires login: no redirect is ever issued and the
    /// visitor counts as authenticated (built-in Admin, BRD-54).
    /// </summary>
    [Fact]
    public void OfflineModeNeverRedirects()
    {
        var guard = Guard(false, null);

        Assert.Null(guard.GetLoginRedirect("/qdrant-admin"));
        Assert.Null(guard.GetRedirect("/qdrant-admin", Capability.ManageQdrant));
        Assert.True(guard.IsAuthenticated);
    }

    /// <summary>
    /// A capability-gated route sends an authenticated but under-privileged user to /denied,
    /// not to /login.
    /// </summary>
    [Fact]
    public void UnderPrivilegedUserSentToDenied()
    {
        var user = new TechieDeskUser(123, "jane.doe@example.com", "Jane Doe", ProductRole.User, true);
        var guard = Guard(true, user);

        Assert.Equal("/denied", guard.GetRedirect("/admin/settings", Capability.AccessAdminConsole));
    }

    /// <summary>
    /// A capability-gated route lets a sufficiently privileged user through with no redirect.
    /// </summary>
    [Fact]
    public void PrivilegedUserPassesCapabilityRoute()
    {
        var admin = new TechieDeskUser(1, "admin@example.com", "Admin", ProductRole.Admin, true);
        var guard = Guard(true, admin);

        Assert.Null(guard.GetRedirect("/admin/settings", Capability.AccessAdminConsole));
    }
}
