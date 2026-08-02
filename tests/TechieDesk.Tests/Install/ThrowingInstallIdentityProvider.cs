using TechieDesk.Services.Install;

namespace TechieDesk.Tests.Install;

/// <summary>
/// An <see cref="IInstallIdentityProvider"/> that always fails, standing in for a host where the
/// identity cannot be computed at all (REQ-FN-051 — degrade, never lock).
/// </summary>
public sealed class ThrowingInstallIdentityProvider : IInstallIdentityProvider
{
    /// <inheritdoc />
    public InstallIdentity Current =>
        throw new InvalidOperationException("The install identity is unavailable on this host.");
}
