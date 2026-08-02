namespace TechieDesk.Services.Auth;

/// <summary>
/// Default <see cref="IRouteGuard"/>: no route is ever gated (REQ-FN-036, BRD-129, REQ-FN-041).
/// The guard now reports sign-in state and nothing else.
/// </summary>
/// <remarks>
/// REQ-FN-036 removed <c>GetLoginRedirect</c>, which returned <c>/login?returnUrl={deep link}</c>
/// for an unauthenticated visitor whenever AppManager was configured — the launch gate that
/// inverted the product model by treating account-free operation as the exception.
/// <para>
/// REQ-FN-041 then removed <c>GetRedirect(Capability)</c>, the surviving capability half. The
/// desktop pivot made TechieDesk single-user: the person at the keyboard is always the local owner
/// (built-in Admin), so the role/capability matrix could only ever return "allowed" and the
/// <c>/denied</c> route it pointed at was unreachable. Deleting the member — rather than leaving it
/// returning null — is what stops a caller re-introducing an access decision here.
/// </para>
/// </remarks>
public sealed class RouteGuard : IRouteGuard
{
    private readonly ISessionContext sessionContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="RouteGuard"/> class.
    /// </summary>
    /// <param name="sessionContext">The app-wide session, used only to report sign-in state.</param>
    public RouteGuard(ISessionContext sessionContext)
    {
        this.sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Read from the session, not from the auth mode: a configured AppManager install that nobody
    /// has signed into is NOT signed in, and must still serve every route.
    /// </remarks>
    public bool IsSignedIn => sessionContext.Tokens.HasSession;
}
