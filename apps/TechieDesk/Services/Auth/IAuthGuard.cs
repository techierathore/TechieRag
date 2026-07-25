namespace TechieDesk.Services.Auth;

/// <summary>
/// Server-side authorization guard (BRD-25): app services inject this and call
/// <see cref="Require(Capability)"/> at the top of every role-gated operation, so a forged
/// call is denied at the service layer regardless of UI state. In offline mode the current
/// user is the built-in Admin, so every check passes (BRD-54).
/// </summary>
public interface IAuthGuard
{
    /// <summary>
    /// Checks whether the current user holds a capability.
    /// </summary>
    /// <param name="capability">The capability to check.</param>
    /// <returns>True when the current user is authenticated and their role holds the capability.</returns>
    bool Allows(Capability capability);

    /// <summary>
    /// Enforces that the current user holds a capability.
    /// </summary>
    /// <param name="capability">The capability required by the operation.</param>
    /// <exception cref="CapabilityDeniedException">
    /// When the current user is unauthenticated or their role does not hold the capability.
    /// </exception>
    void Require(Capability capability);
}
