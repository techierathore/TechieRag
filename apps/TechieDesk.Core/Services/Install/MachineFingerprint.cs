namespace TechieDesk.Services.Install;

/// <summary>
/// The machine-derived half of the install identity, already hashed (REQ-FN-051 clause 1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The raw platform value never leaves this type.</b> <see cref="Value"/> is a salted SHA-256 of
/// the hardware/OS identifier, so nothing that could act as a cross-application hardware tracker is
/// written to disk or handed to the licence server — which is what REQ-NFR-008 (no telemetry, no
/// beacons) requires of anything new that goes on the wire.
/// </para>
/// <para>
/// <b>What this does and does not resist.</b> It resists the ordinary case the requirement names:
/// copying a data directory (or a whole restored backup) onto a second machine produces a different
/// fingerprint, so the two installs are distinguishable. It does <b>not</b> resist an adversary who
/// controls the machine — the value comes from a user-space read that can be intercepted, patched
/// out or virtualised, and a VM image cloned wholesale carries the same identifier. This is
/// seat-accounting hygiene, not copy protection.
/// </para>
/// </remarks>
/// <param name="Value">Lower-case hexadecimal SHA-256 of the app-salted platform value.</param>
/// <param name="Source">Which mechanism produced it.</param>
/// <param name="IsPlatformStable">
/// True when the source is a hardware- or OS-issued identifier that survives a rename, a new user
/// account and an in-place OS upgrade; false for the <see cref="MachineFingerprintSource.MachineName"/>
/// fallback, which a user can change from System Settings.
/// </param>
public sealed record MachineFingerprint(string Value, MachineFingerprintSource Source, bool IsPlatformStable);
