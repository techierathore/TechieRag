namespace TechieDesk.Services.Auth;

/// <summary>
/// Resolves the current scope's session handle and the shared session state it points at
/// (REQ-FN-032).
/// </summary>
/// <remarks>
/// REQ-FN-035 changed what a "scope" is. Under the retired Blazor Server head a scope was either an
/// HTTP request (handle from the signed <c>td.sid</c> cookie) or a circuit (handle attached at
/// circuit start), and the whole indirection existed so a full-page navigation would not sign the
/// user out. The desktop head has neither requests nor circuits: one process serves one person, so
/// <see cref="DesktopSessionContext"/> holds a single session for the lifetime of the app and there
/// is nothing to lose state across. The interface survives unchanged so every consumer —
/// <c>TechieDeskUserContext</c>, the authentication state provider, licensing and token refresh —
/// carries over untouched; only the implementation was swapped.
/// </remarks>
public interface ISessionContext
{
    /// <summary>Gets the opaque session handle for this scope, or null when signed out.</summary>
    string? Handle { get; }

    /// <summary>
    /// Gets the shared session state for this scope. Never null — an unauthenticated scope gets
    /// an empty, detached store whose <see cref="SessionTokenStore.HasSession"/> is false.
    /// </summary>
    SessionTokenStore Tokens { get; }

    /// <summary>
    /// Attaches a handle to this scope explicitly. On the desktop head this is how sign-in and
    /// sign-out record their result, since there is no cookie to write.
    /// </summary>
    /// <param name="handle">The opaque handle, or null to mark the scope as signed out.</param>
    void AttachHandle(string? handle);
}
