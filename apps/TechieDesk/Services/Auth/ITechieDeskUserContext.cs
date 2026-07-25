namespace TechieDesk.Services.Auth;

/// <summary>
/// Provides the current user for authorization decisions. In offline mode this is always the
/// built-in Admin (BRD-54); in AppManager mode it is the signed-in user from the per-circuit
/// <see cref="SessionTokenStore"/>, or <see cref="TechieDeskUser.Anonymous"/> before login.
/// </summary>
public interface ITechieDeskUserContext
{
    /// <summary>Gets the current user (never null).</summary>
    TechieDeskUser CurrentUser { get; }
}
