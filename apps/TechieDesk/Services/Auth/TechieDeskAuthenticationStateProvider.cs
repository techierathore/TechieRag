using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using TechieDesk.Services.AppManager.Models;

namespace TechieDesk.Services.Auth;

/// <summary>
/// Custom <see cref="AuthenticationStateProvider"/> for TechieDesk (BRD-20). In offline mode
/// every circuit is authenticated as the built-in Admin (BRD-54); in AppManager mode the state
/// reflects the per-circuit <see cref="SessionTokenStore"/> — tokens stay server-side only.
/// </summary>
public sealed class TechieDeskAuthenticationStateProvider : AuthenticationStateProvider
{
    /// <summary>The authentication type stamped on authenticated principals.</summary>
    public const string AuthenticationType = "TechieDesk";

    private readonly ITechieDeskAuthModeProvider modeProvider;
    private readonly SessionTokenStore tokenStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="TechieDeskAuthenticationStateProvider"/> class.
    /// </summary>
    /// <param name="modeProvider">The auth-mode switch.</param>
    /// <param name="tokenStore">The per-circuit session token store.</param>
    public TechieDeskAuthenticationStateProvider(
        ITechieDeskAuthModeProvider modeProvider,
        SessionTokenStore tokenStore)
    {
        this.modeProvider = modeProvider;
        this.tokenStore = tokenStore;
    }

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!modeProvider.IsAppManagerEnabled)
        {
            return Task.FromResult(new AuthenticationState(BuildPrincipal(TechieDeskUser.BuiltInAdmin)));
        }

        var user = tokenStore.User;
        var principal = user is { IsAuthenticated: true }
            ? BuildPrincipal(user)
            : new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(principal));
    }

    /// <summary>
    /// Establishes the circuit session from a successful login/register response: maps the
    /// app-scoped role (BRD-23), stores the tokens server-side, and notifies Blazor.
    /// </summary>
    /// <param name="auth">The AppManager auth response.</param>
    public void SignIn(AuthResponseData auth)
    {
        var role = ProductRoleMapper.Map(auth.ApplicationRole);
        var displayName = $"{auth.FirstName} {auth.LastName}".Trim();
        var user = new TechieDeskUser(auth.UserId, auth.Email, displayName, role, true);
        tokenStore.SetSession(user, auth.AccessToken, auth.RefreshToken, auth.TokenExpiresAt);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>Clears the circuit session and notifies Blazor.</summary>
    public void SignOut()
    {
        tokenStore.Clear();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsPrincipal BuildPrincipal(TechieDeskUser user)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        }, AuthenticationType);
        return new ClaimsPrincipal(identity);
    }
}
