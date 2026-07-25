namespace TechieDesk.Services.Auth;

/// <summary>
/// Default <see cref="ITechieDeskUserContext"/>: built-in Admin in offline mode, otherwise
/// the session user from the per-circuit token store.
/// </summary>
public sealed class TechieDeskUserContext : ITechieDeskUserContext
{
    private readonly ITechieDeskAuthModeProvider modeProvider;
    private readonly SessionTokenStore tokenStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="TechieDeskUserContext"/> class.
    /// </summary>
    /// <param name="modeProvider">The auth-mode switch.</param>
    /// <param name="tokenStore">The per-circuit session token store.</param>
    public TechieDeskUserContext(ITechieDeskAuthModeProvider modeProvider, SessionTokenStore tokenStore)
    {
        this.modeProvider = modeProvider;
        this.tokenStore = tokenStore;
    }

    /// <inheritdoc />
    public TechieDeskUser CurrentUser =>
        modeProvider.IsAppManagerEnabled
            ? tokenStore.User ?? TechieDeskUser.Anonymous
            : TechieDeskUser.BuiltInAdmin;
}
