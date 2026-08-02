using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace TechieDesk.Services.Auth;

/// <summary>
/// Custom <see cref="AuthenticationStateProvider"/> for TechieDesk. The state reflects the
/// handle-keyed <see cref="ISessionStore"/> entry when an AppManager account has been signed in
/// for licensing, and the local owner (built-in Admin) otherwise — tokens stay server-side only.
/// </summary>
/// <remarks>
/// REQ-FN-032: this also implements <see cref="IHostEnvironmentAuthenticationStateProvider"/>,
/// which is how a new scope learns which session it belongs to; the host calls
/// <see cref="SetAuthenticationState"/> with a principal whose single claim is the opaque session
/// handle.
/// <para>
/// REQ-FN-036 / BRD-129: the mode branch and the anonymous principal are gone. Previously an
/// AppManager-configured install presented an unauthenticated <see cref="ClaimsPrincipal"/> until
/// someone signed in, which is the same anonymous-vs-authenticated split the route guard enforced.
/// A desktop install is always operated by its owner, so the principal is always authenticated;
/// signing in only swaps the local owner for the AppManager account and its role.
/// </para>
/// </remarks>
public sealed class TechieDeskAuthenticationStateProvider
    : AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider
{
    /// <summary>The authentication type stamped on authenticated principals.</summary>
    public const string AuthenticationType = "TechieDesk";

    private readonly ISessionContext sessionContext;
    private readonly ILogger<TechieDeskAuthenticationStateProvider> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TechieDeskAuthenticationStateProvider"/> class.
    /// </summary>
    /// <param name="sessionContext">The current scope's session handle and shared session state.</param>
    /// <param name="logger">Logger for session-continuity security events.</param>
    public TechieDeskAuthenticationStateProvider(
        ISessionContext sessionContext,
        ILogger<TechieDeskAuthenticationStateProvider> logger)
    {
        this.sessionContext = sessionContext;
        this.logger = logger;
    }

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = sessionContext.Tokens.User is { IsAuthenticated: true } signedIn
            ? signedIn
            : TechieDeskUser.BuiltInAdmin;
        return Task.FromResult(new AuthenticationState(BuildPrincipal(user)));
    }

    /// <summary>
    /// Adopts the host-supplied principal for a new circuit: its single session-handle claim is
    /// attached to this scope so every service resolves the same server-side session (REQ-FN-032).
    /// </summary>
    /// <param name="authenticationStateTask">The principal Blazor authenticated the circuit with.</param>
    public void SetAuthenticationState(Task<AuthenticationState> authenticationStateTask)
    {
        ArgumentNullException.ThrowIfNull(authenticationStateTask);

        // Blazor hands this over already completed, at circuit start, BEFORE any component is
        // built. Attaching synchronously is what lets the route guard in the shell's
        // OnInitialized see the restored session instead of an anonymous visitor.
        if (authenticationStateTask.IsCompletedSuccessfully)
        {
            AttachHandleFrom(authenticationStateTask.Result.User);
        }

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>Re-reads the session and notifies Blazor (used after logout).</summary>
    public void NotifySessionChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private void AttachHandleFrom(ClaimsPrincipal principal)
    {
        var handle = principal.FindFirst(SessionCookie.HandleClaimType)?.Value;
        sessionContext.AttachHandle(handle);
        logger.LogDebug(
            "Circuit adopted a session handle from the connection principal (present: {Present})",
            !string.IsNullOrEmpty(handle));
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
