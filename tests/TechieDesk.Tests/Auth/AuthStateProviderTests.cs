using System.Security.Claims;
using TechieDesk.Services.AppManager.Models;
using TechieDesk.Services.Auth;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// REQ-FN-003/005: the custom AuthenticationStateProvider — offline mode is authenticated as
/// the built-in Admin, AppManager mode reflects the per-circuit session with the mapped role
/// exposed as a role claim.
/// </summary>
public sealed class AuthStateProviderTests
{
    private static (TechieDeskAuthenticationStateProvider Provider, SessionTokenStore Store) Build(bool appManagerEnabled)
    {
        var store = new SessionTokenStore();
        var provider = new TechieDeskAuthenticationStateProvider(TestFactory.Mode(appManagerEnabled), store);
        return (provider, store);
    }

    /// <summary>
    /// In offline mode every circuit is authenticated as the built-in Admin without a login.
    /// </summary>
    [Fact]
    public async Task OfflineStateIsAdmin()
    {
        var (provider, _) = Build(false);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.True(state.User.IsInRole(nameof(ProductRole.Admin)));
    }

    /// <summary>In AppManager mode a circuit without a session is anonymous.</summary>
    [Fact]
    public async Task AppManagerModeStartsAnonymous()
    {
        var (provider, _) = Build(true);

        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity!.IsAuthenticated);
    }

    /// <summary>
    /// SignIn maps the app-scoped applicationRole to a product role claim (BRD-23), stores the
    /// tokens server-side only, and produces an authenticated principal.
    /// </summary>
    [Fact]
    public async Task SignInExposesMappedRole()
    {
        var (provider, store) = Build(true);
        var auth = new AuthResponseData
        {
            UserId = 123,
            Email = "jane.doe@example.com",
            FirstName = "Jane",
            LastName = "Doe",
            ApplicationRole = "Manager",
            AccessToken = "access-token-1",
            RefreshToken = "refresh-token-1",
            TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        provider.SignIn(auth);
        var state = await provider.GetAuthenticationStateAsync();

        Assert.True(state.User.Identity!.IsAuthenticated);
        Assert.True(state.User.IsInRole(nameof(ProductRole.Manager)));
        Assert.Equal("jane.doe@example.com", state.User.FindFirstValue(ClaimTypes.Email));
        Assert.Equal("access-token-1", store.AccessToken);
        Assert.Equal(ProductRole.Manager, store.User!.Role);
    }

    /// <summary>SignOut clears the server-side session and returns the circuit to anonymous.</summary>
    [Fact]
    public async Task SignOutClearsSession()
    {
        var (provider, store) = Build(true);
        provider.SignIn(new AuthResponseData
        {
            UserId = 123,
            Email = "jane.doe@example.com",
            FirstName = "Jane",
            LastName = "Doe",
            ApplicationRole = "User",
            AccessToken = "access-token-1",
            RefreshToken = "refresh-token-1",
            TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });

        provider.SignOut();
        var state = await provider.GetAuthenticationStateAsync();

        Assert.False(state.User.Identity!.IsAuthenticated);
        Assert.False(store.HasSession);
    }
}
