namespace TechieDesk.Services.Auth;

/// <summary>
/// Reusable route-protection guard for the UI wave (BRD-20): pages/layouts ask it whether the
/// current visitor may stay on a route, and where to send them otherwise. In offline mode no
/// redirect is ever issued (BRD-54).
/// </summary>
public interface IRouteGuard
{
    /// <summary>Gets a value indicating whether the current visitor is authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Computes the login redirect for a protected route, preserving the originally requested
    /// route so the user lands back on it after login.
    /// </summary>
    /// <param name="returnUrl">The app-relative route the visitor requested (deep link).</param>
    /// <returns>
    /// <c>/login?returnUrl={escaped deep link}</c> when the visitor must log in first;
    /// null when access is allowed (offline mode, or already authenticated).
    /// </returns>
    string? GetLoginRedirect(string returnUrl);

    /// <summary>
    /// Computes the redirect for a capability-gated route.
    /// </summary>
    /// <param name="returnUrl">The app-relative route the visitor requested.</param>
    /// <param name="capability">The capability the route requires.</param>
    /// <returns>
    /// The login redirect when unauthenticated, <c>/denied</c> when authenticated but lacking
    /// the capability, or null when access is allowed.
    /// </returns>
    string? GetRedirect(string returnUrl, Capability capability);
}
