using TechieDesk.Services.Auth;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-007 / BRD-25: server-side authorization on every operation. A forged call to a
/// role-gated service method as User is denied at the service layer, regardless of UI state.
/// </summary>
public sealed class AuthGuardTests
{
    private static AuthGuard Guard(bool appManagerEnabled, TechieDeskUser? sessionUser)
    {
        var store = new SessionTokenStore();
        if (sessionUser != null)
        {
            store.SetSession(sessionUser, "access-token-1", "refresh-token-1", DateTimeOffset.UtcNow.AddHours(1));
        }

        var context = new TechieDeskUserContext(TestFactory.Mode(appManagerEnabled), store);
        return new AuthGuard(context, new CapabilityService());
    }

    private static TechieDeskUser UserOf(ProductRole role)
    {
        return new TechieDeskUser(123, "jane.doe@example.com", "Jane Doe", role, true);
    }

    /// <summary>
    /// Forging a call to a role-gated operation as User is denied: the service-layer guard
    /// throws CapabilityDeniedException before the operation body runs.
    /// </summary>
    [Fact]
    public void UserForgingAdminCallDenied()
    {
        var workspaceService = new FakeWorkspaceAdminService(Guard(true, UserOf(ProductRole.User)));

        var exception = Assert.Throws<CapabilityDeniedException>(() => workspaceService.DeleteWorkspace(42));

        Assert.Equal(Capability.ManageWorkspaces, exception.Capability);
        Assert.Equal(ProductRole.User, exception.Role);
        Assert.False(workspaceService.Deleted);
    }

    /// <summary>An unauthenticated caller is denied even for User-level capabilities.</summary>
    [Fact]
    public void AnonymousCallerDenied()
    {
        var guard = Guard(true, null);

        var exception = Assert.Throws<CapabilityDeniedException>(
            () => guard.Require(Capability.ChatInAssignedWorkspaces));

        Assert.False(exception.IsAuthenticated);
    }

    /// <summary>A Manager passes the guard for workspace management operations.</summary>
    [Fact]
    public void ManagerPassesWorkspaceGuard()
    {
        var workspaceService = new FakeWorkspaceAdminService(Guard(true, UserOf(ProductRole.Manager)));

        workspaceService.DeleteWorkspace(42);

        Assert.True(workspaceService.Deleted);
    }

    /// <summary>A Manager is denied instance-administration operations.</summary>
    [Fact]
    public void ManagerDeniedInstanceSettings()
    {
        var guard = Guard(true, UserOf(ProductRole.Manager));

        Assert.Throws<CapabilityDeniedException>(() => guard.Require(Capability.ManageInstanceSettings));
    }

    /// <summary>An Admin passes every guard check.</summary>
    [Fact]
    public void AdminPassesAllGuards()
    {
        var guard = Guard(true, UserOf(ProductRole.Admin));

        Assert.All(Enum.GetValues<Capability>(), capability => Assert.True(guard.Allows(capability)));
    }

    /// <summary>
    /// In offline single-user mode (no AppManager configured) the current user is the built-in
    /// Admin, so guard checks pass without any login (BRD-54).
    /// </summary>
    [Fact]
    public void OfflineModeGrantsAdmin()
    {
        var workspaceService = new FakeWorkspaceAdminService(Guard(false, null));

        workspaceService.DeleteWorkspace(42);

        Assert.True(workspaceService.Deleted);
    }
}

/// <summary>
/// Minimal stand-in for a later service cluster: injects <see cref="IAuthGuard"/> and calls
/// <see cref="IAuthGuard.Require"/> as its first statement, the pattern all app services use.
/// </summary>
public sealed class FakeWorkspaceAdminService
{
    private readonly IAuthGuard guard;

    /// <summary>Initializes the fake service with a guard.</summary>
    /// <param name="guard">The authorization guard.</param>
    public FakeWorkspaceAdminService(IAuthGuard guard)
    {
        this.guard = guard;
    }

    /// <summary>Gets a value indicating whether the gated operation body ran.</summary>
    public bool Deleted { get; private set; }

    /// <summary>Role-gated operation: requires ManageWorkspaces before doing anything.</summary>
    /// <param name="workspaceId">The workspace to delete.</param>
    public void DeleteWorkspace(int workspaceId)
    {
        guard.Require(Capability.ManageWorkspaces);
        Deleted = true;
    }
}
