namespace TechieDesk.Services.Auth;

/// <summary>
/// The authenticated (or built-in) user as seen by TechieDesk's authorization layer.
/// </summary>
/// <param name="UserId">The AppManager user identifier (0 for built-in/anonymous users).</param>
/// <param name="Email">The user's email address.</param>
/// <param name="DisplayName">The user's display name.</param>
/// <param name="Role">The mapped product role (BRD-23).</param>
/// <param name="IsAuthenticated">Whether the user is authenticated.</param>
public sealed record TechieDeskUser(
    int UserId,
    string Email,
    string DisplayName,
    ProductRole Role,
    bool IsAuthenticated)
{
    /// <summary>
    /// The built-in Admin used in offline single-user mode (BRD-54): everything is
    /// authenticated-as-Admin without a login.
    /// </summary>
    public static TechieDeskUser BuiltInAdmin { get; } =
        new(0, "admin@techiedesk.local", "Administrator", ProductRole.Admin, true);

    /// <summary>The unauthenticated visitor (AppManager mode, before login).</summary>
    public static TechieDeskUser Anonymous { get; } =
        new(0, string.Empty, "Anonymous", ProductRole.User, false);
}
