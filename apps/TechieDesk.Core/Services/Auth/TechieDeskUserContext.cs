namespace TechieDesk.Services.Auth;

/// <summary>
/// Default <see cref="ITechieDeskUserContext"/>: the signed-in AppManager user when one is
/// present, and the local owner (built-in Admin) otherwise.
/// </summary>
/// <remarks>
/// REQ-FN-032: this deliberately depends on <see cref="ISessionContext"/> rather than on a
/// captured <see cref="SessionTokenStore"/>, so it always reads the CURRENT session. A scope
/// adopts its handle at start-up, and anything that captured a store before that would otherwise
/// be stuck showing a stale user for the life of the scope.
/// <para>
/// REQ-FN-036 / BRD-129: the mode branch is gone. It used to resolve an unauthenticated visitor on
/// an AppManager-configured install to an anonymous identity — the identity half
/// of the anonymous-vs-authenticated split, which then denied that visitor every capability over
/// their OWN local data. One desktop install serves one person, so the person at the keyboard is
/// the owner whether or not they have activated a licence. Signing in replaces that identity with
/// the AppManager account (and its role); it never creates one where there was none.
/// </para>
/// </remarks>
public sealed class TechieDeskUserContext : ITechieDeskUserContext
{
    private readonly ISessionContext sessionContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TechieDeskUserContext"/> class.
    /// </summary>
    /// <param name="sessionContext">The current scope's session handle and shared state.</param>
    public TechieDeskUserContext(ISessionContext sessionContext)
    {
        this.sessionContext = sessionContext ?? throw new ArgumentNullException(nameof(sessionContext));
    }

    /// <inheritdoc />
    public TechieDeskUser CurrentUser => sessionContext.Tokens.User ?? TechieDeskUser.BuiltInAdmin;
}
