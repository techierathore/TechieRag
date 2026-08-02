namespace TechieDesk.Services.Install;

/// <summary>
/// The identity of ONE installation of TechieDesk (REQ-FN-051 clause 1).
/// </summary>
/// <remarks>
/// <para>
/// It is two values, not one, and the split is the whole point. <see cref="InstallId"/> is minted
/// once into the data directory and never changes, so it survives restart, upgrade and a machine
/// rename — that is the "stable" half. <see cref="MachineFingerprint"/> is re-derived from the host
/// on every launch and is never trusted from disk — that is the "not trivially clonable" half.
/// <see cref="CompositeId"/> binds them, so the identity presented to AppManager differs between two
/// machines even when the data directory was copied byte-for-byte between them.
/// </para>
/// <para>
/// <b>Honest limits.</b> A copied data directory keeps the same <see cref="InstallId"/>, which is
/// deliberate: it is what lets a server tell "the same install moved" apart from "a second install".
/// It resists a user copying a folder to a second Mac. It does <b>not</b> resist a cloned VM image,
/// a patched binary, or anyone willing to edit the identity file and spoof the fingerprint read —
/// all of that runs with the user's own privileges. Nothing here is a DRM claim.
/// </para>
/// </remarks>
public sealed class InstallIdentity
{
    /// <summary>Gets the opaque per-install identifier, minted once and stored in the data directory.</summary>
    /// <remarks>A GUID in "N" format. Carries no personal or hardware data.</remarks>
    public required string InstallId { get; init; }

    /// <summary>Gets the salted hash of the host's platform identifier, re-derived this launch.</summary>
    public required string MachineFingerprint { get; init; }

    /// <summary>Gets the value presented to the licence server: a hash of both halves together.</summary>
    /// <remarks>
    /// Lower-case hexadecimal SHA-256. This — not <see cref="InstallId"/> and not the fingerprint —
    /// is what clause (2) puts on the wire, so no raw hardware identifier and no stable
    /// cross-application key ever leaves the machine.
    /// </remarks>
    public required string CompositeId { get; init; }

    /// <summary>Gets when this install first minted its identity, in UTC.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets a value indicating whether the fingerprint came from a platform-stable source.</summary>
    /// <remarks>
    /// False means the anti-clone half degraded to the host name (see
    /// <see cref="MachineFingerprintSource"/>). The identity is still usable and the app still runs;
    /// what is lost is the ability to distinguish two installs on two machines.
    /// </remarks>
    public required bool IsMachineBound { get; init; }

    /// <summary>
    /// Gets a value indicating whether the stored fingerprint differed from the one measured now.
    /// </summary>
    /// <remarks>
    /// True on the first launch after a data directory has been copied to another machine — or after
    /// the fingerprint source degraded, which looks identical from here. It is reported, logged and
    /// carried to the server; it never blocks anything locally. Degrade, never lock.
    /// </remarks>
    public required bool HasMovedMachine { get; init; }
}
