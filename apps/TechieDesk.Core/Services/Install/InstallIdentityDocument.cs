namespace TechieDesk.Services.Install;

/// <summary>
/// The on-disk shape of <c>install-identity.json</c>. Internal: the file is a private
/// implementation detail of <see cref="InstallIdentityStore"/>, not a supported format.
/// </summary>
internal sealed class InstallIdentityDocument
{
    /// <summary>Format version, so a later build can recognise an older file rather than guess.</summary>
    public int Version { get; set; } = 1;

    /// <summary>The minted per-install identifier.</summary>
    public string? InstallId { get; set; }

    /// <summary>The salted machine fingerprint recorded when the file was last written.</summary>
    public string? MachineFingerprint { get; set; }

    /// <summary>When the identity was first minted.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}
