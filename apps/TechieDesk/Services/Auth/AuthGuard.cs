namespace TechieDesk.Services.Auth;

/// <summary>
/// Default <see cref="IAuthGuard"/>: resolves the current user from
/// <see cref="ITechieDeskUserContext"/> and checks the role matrix via
/// <see cref="ICapabilityService"/> (BRD-24, BRD-25).
/// </summary>
public sealed class AuthGuard : IAuthGuard
{
    private readonly ITechieDeskUserContext userContext;
    private readonly ICapabilityService capabilityService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthGuard"/> class.
    /// </summary>
    /// <param name="userContext">The current-user provider.</param>
    /// <param name="capabilityService">The role-to-capability matrix.</param>
    public AuthGuard(ITechieDeskUserContext userContext, ICapabilityService capabilityService)
    {
        this.userContext = userContext;
        this.capabilityService = capabilityService;
    }

    /// <inheritdoc />
    public bool Allows(Capability capability)
    {
        var user = userContext.CurrentUser;
        return user.IsAuthenticated && capabilityService.Has(user.Role, capability);
    }

    /// <inheritdoc />
    public void Require(Capability capability)
    {
        var user = userContext.CurrentUser;
        if (!user.IsAuthenticated || !capabilityService.Has(user.Role, capability))
        {
            throw new CapabilityDeniedException(capability, user.Role, user.IsAuthenticated);
        }
    }
}
