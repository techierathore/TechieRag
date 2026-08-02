namespace TechieDesk.Services.Auth;

/// <summary>
/// Route policy for the desktop shell (REQ-FN-036, BRD-129). Signing in activates a licence; it
/// never gates the user's own local data, so this guard has no concept of a login redirect and no
/// anonymous-vs-authenticated routing split.
/// </summary>
/// <remarks>
/// The interface deliberately exposes NO member that can produce a <c>/login</c> URL. That is the
/// structural half of REQ-FN-036: the launch gate cannot be reinstated by flipping a condition,
/// only by re-adding a member — which <c>RouteGuardTests</c> asserts against.
/// <para>
/// REQ-FN-041 removed <c>GetRedirect(Capability)</c>, the last redirect the guard could issue. One
/// install serves one person, who is always the local owner (built-in Admin), so the capability
/// matrix it consulted could never answer anything but "allowed". What remains is a pure sign-in
/// state report, which is not an access gate at all.
/// </para>
/// </remarks>
public interface IRouteGuard
{
    /// <summary>
    /// Gets a value indicating whether an AppManager account is signed in for licensing purposes.
    /// This is informational only — the shell uses it to choose between offering "Sign in" and
    /// offering "Log out". It must never be used to decide whether a route may be served.
    /// </summary>
    bool IsSignedIn { get; }
}
