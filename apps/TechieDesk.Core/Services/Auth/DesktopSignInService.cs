using TechieDesk.Services.AppManager;
using TechieDesk.Services.AppManager.Models;

namespace TechieDesk.Services.Auth;

/// <summary>
/// In-process <see cref="IDesktopSignInService"/>: the desktop replacement for the retired
/// <c>SessionEndpoints</c> handlers (REQ-FN-039, REQ-UI-007).
/// </summary>
/// <remarks>
/// Everything the endpoints did that was not about HTTP is carried over unchanged — field
/// validation, the password policy, the AppManager call, handle rotation before the new session is
/// created, and the error-code vocabulary the screens render banners from. Everything that WAS about
/// HTTP is gone with the host: no antiforgery token (a desktop app has no cross-site request to
/// forge), no cookie to write, no redirect, and no <c>returnUrl</c> to sanitise here — the screen
/// navigates in-circuit and filters its own return path with <see cref="AuthScreenCodes.SafeReturnUrl"/>.
/// </remarks>
public sealed class DesktopSignInService : IDesktopSignInService
{
    private readonly IAppManagerClient client;
    private readonly ISessionStore sessionStore;
    private readonly ISessionContext sessionContext;
    private readonly ITechieDeskAuthModeProvider modeProvider;
    private readonly ILogger<DesktopSignInService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DesktopSignInService"/> class.
    /// </summary>
    /// <param name="client">The AppManager API client.</param>
    /// <param name="sessionStore">The session store that owns expiry, rotation and revocation.</param>
    /// <param name="sessionContext">The app-wide session, told which handle to present.</param>
    /// <param name="modeProvider">The auth-mode switch (is there a licence server at all?).</param>
    /// <param name="logger">Logger for sign-in security events.</param>
    public DesktopSignInService(
        IAppManagerClient client,
        ISessionStore sessionStore,
        ISessionContext sessionContext,
        ITechieDeskAuthModeProvider modeProvider,
        ILogger<DesktopSignInService> logger)
    {
        this.client = client;
        this.sessionStore = sessionStore;
        this.sessionContext = sessionContext;
        this.modeProvider = modeProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    public Task<SignInOutcome> SignInAsync(
        string? email, string? password, CancellationToken cancellationToken = default)
    {
        var trimmedEmail = email?.Trim() ?? string.Empty;
        if (trimmedEmail.Length == 0 || string.IsNullOrEmpty(password))
        {
            return Task.FromResult(SignInOutcome.Failure(AuthScreenCodes.MissingFields));
        }

        return AuthenticateAsync(
            () => client.LoginAsync(trimmedEmail, password, cancellationToken), trimmedEmail);
    }

    /// <inheritdoc />
    public Task<SignInOutcome> RegisterAsync(
        RegisterRequest request,
        string? password,
        string? confirmPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationError = Validate(request, password, confirmPassword);
        if (validationError is not null)
        {
            return Task.FromResult(SignInOutcome.Failure(validationError));
        }

        return AuthenticateAsync(
            () => client.RegisterAsync(request, password!, cancellationToken), request.Email);
    }

    /// <summary>
    /// Applies the registration rules that must hold before any credential leaves the machine.
    /// </summary>
    /// <returns>The error code to render, or null when the details are acceptable.</returns>
    private static string? Validate(RegisterRequest request, string? password, string? confirmPassword)
    {
        if (request.FirstName.Trim().Length == 0
            || request.LastName.Trim().Length == 0
            || !request.Email.Contains('@'))
        {
            return AuthScreenCodes.MissingFields;
        }

        if (!PasswordPolicy.IsValid(password))
        {
            return AuthScreenCodes.WeakPassword;
        }

        return password == confirmPassword ? null : AuthScreenCodes.PasswordMismatch;
    }

    private async Task<SignInOutcome> AuthenticateAsync(Func<Task<AuthResponseData>> authenticate, string email)
    {
        if (!modeProvider.IsAppManagerEnabled)
        {
            // BRD-129: there is nothing to sign in to, and nothing is lost by that — local use was
            // never gated. Say so rather than pretending the attempt failed.
            logger.LogInformation("Sign-in requested with no licence server configured; nothing to do");
            return SignInOutcome.Failure(AuthScreenCodes.NoLicenceServer);
        }

        try
        {
            var auth = await authenticate().ConfigureAwait(false);
            EstablishSession(auth);
            logger.LogInformation("Sign-in succeeded for {Email}; licence session established", email);
            return SignInOutcome.Success();
        }
        catch (AppManagerException ex)
        {
            logger.LogWarning("Sign-in failed for {Email} ({ErrorCode})", email, ex.ErrorCode);
            return SignInOutcome.Failure(ex.ErrorCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while signing {Email} in", email);
            return SignInOutcome.Failure(AuthScreenCodes.Unexpected);
        }
    }

    /// <summary>
    /// Rotates the handle and establishes the new session, which is also what commits the tokens to
    /// the OS credential store (REQ-FN-039).
    /// </summary>
    private void EstablishSession(AuthResponseData auth)
    {
        // Session-fixation defence, carried over verbatim from the retired endpoint: whatever handle
        // this process was already holding is destroyed BEFORE the new one is minted, so a session
        // that existed before the sign-in can never be promoted to an authenticated one.
        if (sessionStore.Invalidate(sessionContext.Handle))
        {
            logger.LogInformation("Rotated the session handle presented at sign-in (fixation defence)");
        }

        // REQ-FN-041 (2026-07-26): the AppManager applicationRole is no longer mapped onto a
        // product role. One install serves one person, who is the local owner of everything on this
        // machine, so signing in to activate a licence must not be able to demote them. The role
        // label is kept only to identify that owner as Admin.
        var user = new TechieDeskUser(
            auth.UserId,
            auth.Email,
            $"{auth.FirstName} {auth.LastName}".Trim(),
            ProductRole.Admin,
            true);

        var handle = sessionStore.CreateSession(
            user, auth.AccessToken, auth.RefreshToken, auth.TokenExpiresAt, auth.ActiveLicense);
        sessionContext.AttachHandle(handle);
    }
}
