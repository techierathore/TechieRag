namespace TechieDesk.Services.Auth;

/// <summary>
/// Names and identifiers for the TechieDesk browser session (REQ-FN-032).
/// </summary>
/// <remarks>
/// The browser only ever receives <see cref="Name"/>, a Data-Protection–signed cookie whose
/// single claim is the opaque handle produced by <see cref="SessionHandle"/>. No JWT, no email,
/// no role, and no license data leaves the server: those live in <see cref="ISessionStore"/>
/// keyed by that handle, which preserves the REQ-NFR-004 "tokens never leave the server"
/// property across circuits.
/// </remarks>
public static class SessionCookie
{
    /// <summary>The browser cookie that carries the opaque session handle.</summary>
    public const string Name = "td.sid";

    /// <summary>The authentication scheme that signs and reads <see cref="Name"/>.</summary>
    public const string AuthenticationScheme = "TechieDeskSession";

    /// <summary>The single claim type stored in the cookie ticket — the opaque handle.</summary>
    public const string HandleClaimType = "td:sid";
}
