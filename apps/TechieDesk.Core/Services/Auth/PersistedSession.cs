using TechieDesk.Services.AppManager.Models;

namespace TechieDesk.Services.Auth;

/// <summary>
/// The exact shape written into the OS credential store for the one live session (REQ-FN-039).
/// </summary>
/// <remarks>
/// Deliberately internal: this is the only place token material is serialized, and nothing outside
/// <see cref="SessionStore"/> has any business holding it. The opaque session handle is NOT a member
/// — a handle is minted fresh on every restore, so one that leaked from a previous run is already
/// dead when the app comes back up.
/// <para>
/// Both expiry bounds travel with the record. Restoring with a fresh absolute deadline would let a
/// session be renewed indefinitely by restarting the app, which is precisely the property the hard
/// cap exists to deny.
/// </para>
/// </remarks>
/// <param name="UserId">The AppManager user identifier.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="DisplayName">The user's display name.</param>
/// <param name="Role">The mapped product role.</param>
/// <param name="AccessToken">The AppManager JWT access token.</param>
/// <param name="RefreshToken">The AppManager refresh token.</param>
/// <param name="TokenExpiresAt">The access-token expiry timestamp.</param>
/// <param name="AbsoluteExpiresAt">The session's hard lifetime deadline.</param>
/// <param name="IdleExpiresAt">The session's sliding idle deadline as of the last write.</param>
/// <param name="ActiveLicense">The active license captured at sign-in (BRD-13), if any.</param>
internal sealed record PersistedSession(
    int UserId,
    string Email,
    string DisplayName,
    ProductRole Role,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset TokenExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    DateTimeOffset IdleExpiresAt,
    ActiveLicenseData? ActiveLicense)
{
    /// <summary>Rebuilds the authenticated user this session belongs to.</summary>
    /// <returns>The user.</returns>
    public TechieDeskUser ToUser() => new(UserId, Email, DisplayName, Role, true);
}
