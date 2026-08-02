namespace TechieDesk.Services.Auth;

/// <summary>
/// Provides the current user. This is the AppManager account from the session-scoped
/// <see cref="SessionTokenStore"/> once someone has signed in to activate a licence, and the
/// local owner — the built-in Admin (BRD-54) — at every other time, including before any sign-in
/// on an AppManager-configured install (REQ-FN-036 / BRD-129).
/// </summary>
public interface ITechieDeskUserContext
{
    /// <summary>Gets the current user (never null).</summary>
    TechieDeskUser CurrentUser { get; }
}
