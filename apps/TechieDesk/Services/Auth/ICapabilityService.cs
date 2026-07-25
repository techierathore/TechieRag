namespace TechieDesk.Services.Auth;

/// <summary>
/// Role-to-capability matrix service (BRD-24): answers whether a role — or the current
/// user — holds a given <see cref="Capability"/>.
/// </summary>
public interface ICapabilityService
{
    /// <summary>
    /// Checks whether a role holds a capability per the BRD §5 matrix.
    /// </summary>
    /// <param name="role">The product role.</param>
    /// <param name="capability">The capability to check.</param>
    /// <returns>True when the role holds the capability.</returns>
    bool Has(ProductRole role, Capability capability);

    /// <summary>
    /// Gets all capabilities held by a role.
    /// </summary>
    /// <param name="role">The product role.</param>
    /// <returns>The set of capabilities for that role.</returns>
    IReadOnlySet<Capability> GetCapabilities(ProductRole role);
}
