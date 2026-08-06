namespace TechieDesk.Services.Install;

/// <summary>
/// Which platform mechanism produced a <see cref="MachineFingerprint"/> (REQ-FN-051 clause 1).
/// </summary>
/// <remarks>
/// Recorded so the honesty of the fingerprint is legible at the call site rather than assumed. Only
/// the first three members are hardware- or OS-issued; <see cref="MachineName"/> is a user-editable
/// string and is treated as a weak fallback everywhere it appears.
/// </remarks>
public enum MachineFingerprintSource
{
    /// <summary>No machine-derived value could be obtained at all.</summary>
    None = 0,

    /// <summary>macOS <c>IOPlatformUUID</c>, read from <c>IOPlatformExpertDevice</c>.</summary>
    MacPlatformUuid = 1,

    /// <summary>Windows <c>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid</c>.</summary>
    WindowsMachineGuid = 2,

    /// <summary>Linux <c>/etc/machine-id</c> (or the D-Bus copy of it).</summary>
    LinuxMachineId = 3,

    /// <summary>The host name. User-editable, so this is explicitly NOT platform-stable.</summary>
    MachineName = 4,

    /// <summary>A value supplied by a caller — used by tests to simulate a different machine.</summary>
    Supplied = 5
}
