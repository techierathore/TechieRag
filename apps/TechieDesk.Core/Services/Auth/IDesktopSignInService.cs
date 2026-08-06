using TechieDesk.Services.AppManager.Models;

namespace TechieDesk.Services.Auth;

/// <summary>
/// Signs a licence account in and out from inside the desktop process (REQ-FN-039, REQ-UI-007).
/// </summary>
/// <remarks>
/// Replaces the <c>POST /auth/login</c> and <c>POST /auth/register</c> endpoints, which died with
/// the web host in REQ-FN-035 and left the auth screens submitting HTML forms into nothing. There is
/// no HTTP boundary left to cross: a component calls this on the same thread, and it talks to
/// AppManager and to <see cref="ISessionStore"/> directly.
/// <para>
/// It exists as a service rather than as component code-behind so the security-critical part —
/// rotating the handle before establishing the new session, and never leaving a half-signed-in
/// state behind on failure — is testable without a WebView.
/// </para>
/// </remarks>
public interface IDesktopSignInService
{
    /// <summary>
    /// Authenticates against AppManager and establishes a rotated local session.
    /// </summary>
    /// <param name="email">The account email address.</param>
    /// <param name="password">The plaintext password (RSA-encrypted by the client before transmission).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome, carrying an error code to render on failure.</returns>
    Task<SignInOutcome> SignInAsync(string? email, string? password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an AppManager account and signs it straight in on the same mechanism as
    /// <see cref="SignInAsync"/>.
    /// </summary>
    /// <param name="request">The registration details (no password).</param>
    /// <param name="password">The plaintext password.</param>
    /// <param name="confirmPassword">The confirmation the user typed, checked before any call is made.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome, carrying an error code to render on failure.</returns>
    Task<SignInOutcome> RegisterAsync(
        RegisterRequest request,
        string? password,
        string? confirmPassword,
        CancellationToken cancellationToken = default);
}
