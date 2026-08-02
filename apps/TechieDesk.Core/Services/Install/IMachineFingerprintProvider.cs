namespace TechieDesk.Services.Install;

/// <summary>
/// Supplies the machine-derived half of the install identity (REQ-FN-051 clause 1).
/// </summary>
/// <remarks>
/// A seam rather than a static call so a test can assert what happens when the SAME data directory
/// is opened on a DIFFERENT machine — the clone case the requirement exists for — without needing a
/// second machine.
/// </remarks>
public interface IMachineFingerprintProvider
{
    /// <summary>Gets the fingerprint of the host this process is running on.</summary>
    /// <returns>
    /// A fingerprint. Never null and never throws: a host that yields no usable identifier returns
    /// one whose <see cref="MachineFingerprint.IsPlatformStable"/> is false, because an install
    /// identity that cannot be computed must degrade, not fail a launch.
    /// </returns>
    MachineFingerprint Get();
}
