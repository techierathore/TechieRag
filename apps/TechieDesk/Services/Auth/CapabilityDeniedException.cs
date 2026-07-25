namespace TechieDesk.Services.Auth;

/// <summary>
/// Thrown by <see cref="IAuthGuard.Require(Capability)"/> when the current user does not hold
/// the required capability (BRD-25 server-side enforcement).
/// </summary>
public sealed class CapabilityDeniedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CapabilityDeniedException"/> class.
    /// </summary>
    /// <param name="capability">The capability that was required.</param>
    /// <param name="role">The role of the user that was denied.</param>
    /// <param name="isAuthenticated">Whether the denied user was authenticated at all.</param>
    public CapabilityDeniedException(Capability capability, ProductRole role, bool isAuthenticated)
        : base(isAuthenticated
            ? $"Role {role} is not permitted to perform {capability}"
            : $"Authentication is required to perform {capability}")
    {
        Capability = capability;
        Role = role;
        IsAuthenticated = isAuthenticated;
    }

    /// <summary>Gets the capability that was required.</summary>
    public Capability Capability { get; }

    /// <summary>Gets the role of the denied user.</summary>
    public ProductRole Role { get; }

    /// <summary>Gets a value indicating whether the denied user was authenticated.</summary>
    public bool IsAuthenticated { get; }
}
