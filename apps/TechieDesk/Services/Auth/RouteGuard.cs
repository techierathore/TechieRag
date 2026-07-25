namespace TechieDesk.Services.Auth;

/// <summary>
/// Default <see cref="IRouteGuard"/>: offline mode never redirects; AppManager mode redirects
/// unauthenticated visitors to <c>/login?returnUrl={deep link}</c> (BRD-20) and users lacking
/// a required capability to <c>/denied</c>.
/// </summary>
public sealed class RouteGuard : IRouteGuard
{
    private readonly ITechieDeskAuthModeProvider modeProvider;
    private readonly ITechieDeskUserContext userContext;
    private readonly ICapabilityService capabilityService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RouteGuard"/> class.
    /// </summary>
    /// <param name="modeProvider">The auth-mode switch.</param>
    /// <param name="userContext">The current-user provider.</param>
    /// <param name="capabilityService">The role-to-capability matrix.</param>
    public RouteGuard(
        ITechieDeskAuthModeProvider modeProvider,
        ITechieDeskUserContext userContext,
        ICapabilityService capabilityService)
    {
        this.modeProvider = modeProvider;
        this.userContext = userContext;
        this.capabilityService = capabilityService;
    }

    /// <inheritdoc />
    public bool IsAuthenticated => userContext.CurrentUser.IsAuthenticated;

    /// <inheritdoc />
    public string? GetLoginRedirect(string returnUrl)
    {
        if (!modeProvider.IsAppManagerEnabled)
        {
            return null;
        }

        if (userContext.CurrentUser.IsAuthenticated)
        {
            return null;
        }

        return $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    /// <inheritdoc />
    public string? GetRedirect(string returnUrl, Capability capability)
    {
        var loginRedirect = GetLoginRedirect(returnUrl);
        if (loginRedirect != null)
        {
            return loginRedirect;
        }

        return capabilityService.Has(userContext.CurrentUser.Role, capability) ? null : "/denied";
    }
}
