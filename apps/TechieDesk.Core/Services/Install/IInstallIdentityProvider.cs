namespace TechieDesk.Services.Install;

/// <summary>
/// Exposes this installation's identity to the rest of the app (REQ-FN-051 clauses 1 and 2).
/// </summary>
/// <remarks>
/// Resolution is lazy and cached: an install that never signs in never needs an identity, and
/// BRD-129 makes that the normal case, so nothing is computed or written until something asks.
/// </remarks>
public interface IInstallIdentityProvider
{
    /// <summary>Gets the identity of this install, computing and persisting it on first access.</summary>
    /// <returns>The identity. Never null; never throws for an ordinary environment failure.</returns>
    InstallIdentity Current { get; }
}
