using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechieDesk.Services.Auth;

namespace TechieDesk.Tests.Auth;

/// <summary>
/// Builders and doubles for the REQ-FN-032 session-continuity tests: a real
/// <see cref="SessionStore"/> over a controllable clock, scoped session contexts that stand in
/// for separate Blazor circuits, and the HTTP-pipeline fakes the session endpoints need.
/// </summary>
public static class SessionTestHarness
{
    /// <summary>Builds a real session store over a controllable clock.</summary>
    /// <param name="clock">The time source (defaults to the system clock).</param>
    /// <param name="idleTimeoutMinutes">Sliding idle window.</param>
    /// <param name="absoluteTimeoutHours">Hard lifetime cap.</param>
    /// <param name="secrets">
    /// The credential store the live session is mirrored into (REQ-FN-039). Defaults to a private
    /// in-memory one, so a test that does not care about persistence is unaffected by it.
    /// </param>
    /// <returns>The store.</returns>
    public static SessionStore Store(
        TimeProvider? clock = null,
        int idleTimeoutMinutes = 60,
        int absoluteTimeoutHours = 12,
        ISecretStore? secrets = null)
    {
        var options = Options.Create(new SessionStoreOptions
        {
            IdleTimeoutMinutes = idleTimeoutMinutes,
            AbsoluteTimeoutHours = absoluteTimeoutHours
        });
        return new SessionStore(
            options,
            clock ?? TimeProvider.System,
            secrets ?? new EphemeralSecretStore(),
            NullLogger<SessionStore>.Instance);
    }

    /// <summary>
    /// Builds a scoped session context standing in for one circuit or request, with the given
    /// handle already attached (as Blazor does at circuit start).
    /// </summary>
    /// <param name="store">The shared session store.</param>
    /// <param name="handle">The opaque handle this scope presents, or null for a signed-out scope.</param>
    /// <returns>The session context.</returns>
    public static ISessionContext Circuit(ISessionStore store, string? handle)
    {
        // REQ-FN-035: DesktopSessionContext replaces the HttpContext-backed SessionContext. The
        // handle-attach path this exercises is identical, so every caller is unchanged.
        var context = new DesktopSessionContext(store);
        context.AttachHandle(handle);
        return context;
    }

    // REQ-FN-035: the HttpRequest(...) helper was removed with the HttpContext-backed
    // SessionContext it constructed. It modelled the static-SSR pass discovering a handle from the
    // signed cookie principal — a code path the desktop head does not have. REQ-FN-041 removes what
    // remains of the cookie machinery.

    /// <summary>Builds a test user for the session store.</summary>
    /// <param name="userId">The AppManager user identifier.</param>
    /// <param name="role">The mapped product role.</param>
    /// <returns>The user.</returns>
    public static TechieDeskUser User(int userId = 123, ProductRole role = ProductRole.Manager)
    {
        return new TechieDeskUser(userId, $"user{userId}@example.com", $"User {userId}", role, true);
    }

    /// <summary>Creates a session in the store for a test user.</summary>
    /// <param name="store">The session store.</param>
    /// <param name="user">The user to sign in.</param>
    /// <param name="accessToken">The access token to stash server-side.</param>
    /// <returns>The opaque handle.</returns>
    public static string SignIn(ISessionStore store, TechieDeskUser user, string accessToken = "access-token-1")
    {
        return store.CreateSession(
            user, accessToken, "refresh-token-1", DateTimeOffset.UtcNow.AddHours(1), null);
    }

    /// <summary>
    /// Builds an <see cref="HttpContext"/> whose principal carries the session-handle claim, the
    /// way the cookie middleware presents it after <c>UseAuthentication</c>.
    /// </summary>
    /// <param name="handle">The handle, or null for an anonymous request.</param>
    /// <returns>The HTTP context.</returns>
    public static DefaultHttpContext RequestWithHandle(string? handle)
    {
        var context = new DefaultHttpContext();
        if (string.IsNullOrEmpty(handle))
        {
            return context;
        }

        var identity = new ClaimsIdentity(
            new[] { new Claim(SessionCookie.HandleClaimType, handle) }, SessionCookie.AuthenticationScheme);
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    /// <summary>A manually advanced clock, so expiry is tested without sleeping.</summary>
    public sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now;

        /// <summary>Initializes the clock at a fixed instant.</summary>
        /// <param name="start">The starting instant.</param>
        public TestClock(DateTimeOffset start)
        {
            now = start;
        }

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => now;

        /// <summary>Moves the clock forward.</summary>
        /// <param name="delta">How far to advance.</param>
        public void Advance(TimeSpan delta) => now = now.Add(delta);
    }

    /// <summary>An antiforgery validator that always accepts (CSRF is exercised separately).</summary>
    public sealed class PassingAntiforgery : IAntiforgery
    {
        /// <inheritdoc />
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => GetTokens(httpContext);

        /// <inheritdoc />
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) =>
            new("request-token", "cookie-token", "__RequestVerificationToken", "X-CSRF-TOKEN");

        /// <inheritdoc />
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);

        /// <inheritdoc />
        public void SetCookieTokenAndHeader(HttpContext httpContext)
        {
        }

        /// <inheritdoc />
        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
    }

    /// <summary>An antiforgery validator that always rejects, standing in for a CSRF attempt.</summary>
    public sealed class RejectingAntiforgery : IAntiforgery
    {
        /// <inheritdoc />
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => GetTokens(httpContext);

        /// <inheritdoc />
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) =>
            new("request-token", "cookie-token", "__RequestVerificationToken", "X-CSRF-TOKEN");

        /// <inheritdoc />
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(false);

        /// <inheritdoc />
        public void SetCookieTokenAndHeader(HttpContext httpContext)
        {
        }

        /// <inheritdoc />
        public Task ValidateRequestAsync(HttpContext httpContext) =>
            throw new AntiforgeryValidationException("The antiforgery token was rejected.");
    }

    /// <summary>Captures the principal handed to <c>HttpContext.SignInAsync</c>.</summary>
    public sealed class CapturingAuthenticationService : IAuthenticationService
    {
        /// <summary>Gets the principal the endpoint signed in, or null when none was issued.</summary>
        public ClaimsPrincipal? SignedInPrincipal { get; private set; }

        /// <summary>Gets a value indicating whether the endpoint signed the visitor out.</summary>
        public bool SignedOut { get; private set; }

        /// <inheritdoc />
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        /// <inheritdoc />
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        /// <inheritdoc />
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        /// <inheritdoc />
        public Task SignInAsync(
            HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignedInPrincipal = principal;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut = true;
            return Task.CompletedTask;
        }
    }
}
