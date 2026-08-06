using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TechieDesk.Services.Install;

/// <summary>
/// Reads, mints and persists the install identity inside the data directory (REQ-FN-051 clause 1).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a pure static over an explicit data directory, in the same shape as
/// <c>TechieDeskDb.DataDirectory</c>: it reads no ambient state, so a test can drive the real
/// production code against a sandbox directory and simulate BOTH a restart and a move to another
/// machine without touching the developer's install.
/// </para>
/// <para>
/// <b>Nothing here can fail a launch.</b> An unreadable or corrupt file is treated as absent and a
/// fresh identity is minted; an unwritable directory yields an identity that is correct for this
/// process but will be re-minted next time. Both are logged. REQ-FN-051 must never gate an
/// account-free local user (BRD-129), so there is no throwing path.
/// </para>
/// <para>
/// <b>Backup interaction (REQ-FN-046/047).</b> <c>install-identity.json</c> is NOT in the
/// <c>.tdbak</c> archive: the packer reads a fixed allow-list of six database tables and never
/// enumerates the data directory, so the identity is unreachable by construction. A restored backup
/// therefore MINTS A NEW IDENTITY, which is the behaviour the seat model wants — a colleague who
/// restores your archive is a different install and must consume their own seat, not silently
/// inherit yours.
/// </para>
/// </remarks>
public static class InstallIdentityStore
{
    /// <summary>Name of the identity file inside the data directory.</summary>
    public const string FileName = "install-identity.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Gets the absolute path of the identity file for a data directory.</summary>
    /// <param name="dataDirectory">An absolute data directory.</param>
    /// <returns>The absolute path of <see cref="FileName"/>. The file may not exist.</returns>
    public static string FilePath(string dataDirectory) => Path.Combine(dataDirectory, FileName);

    /// <summary>
    /// Combines the two halves of the identity into the single value presented to the licence server.
    /// </summary>
    /// <param name="installId">The minted per-install identifier.</param>
    /// <param name="machineFingerprint">The salted machine fingerprint measured this launch.</param>
    /// <returns>Lower-case hexadecimal SHA-256 of both values.</returns>
    /// <remarks>
    /// Hashed rather than concatenated so the wire value discloses neither half — in particular it
    /// cannot be correlated back to a hardware identifier (REQ-NFR-008).
    /// </remarks>
    public static string ComposeId(string installId, string machineFingerprint)
    {
        var bytes = Encoding.UTF8.GetBytes(installId + ":" + machineFingerprint);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Loads the install identity, minting and persisting one on first use.
    /// </summary>
    /// <param name="dataDirectory">
    /// An absolute data directory. Created when missing; the identity is scoped to it, so two
    /// directories are two installs, which is exactly what the single-instance guard also scopes to.
    /// </param>
    /// <param name="fingerprint">The machine fingerprint measured for THIS launch.</param>
    /// <param name="timeProvider">Clock used to stamp a newly minted identity.</param>
    /// <param name="logger">Optional logger; nothing here throws, so diagnostics are the only signal.</param>
    /// <returns>The identity for this install. Never null.</returns>
    /// <remarks>
    /// A stored identifier is always reused, which is what makes the identity stable across restart.
    /// A stored fingerprint that no longer matches is REPLACED and reported through
    /// <see cref="InstallIdentity.HasMovedMachine"/> — the install id is kept, so a server can tell a
    /// move apart from a new install.
    /// </remarks>
    public static InstallIdentity Load(
        string dataDirectory,
        MachineFingerprint fingerprint,
        TimeProvider timeProvider,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var stored = TryRead(dataDirectory, logger);
        var hasMoved = stored is not null
            && !string.IsNullOrWhiteSpace(stored.MachineFingerprint)
            && !string.Equals(stored.MachineFingerprint, fingerprint.Value, StringComparison.Ordinal);

        var installId = stored?.InstallId;
        var createdAtUtc = stored?.CreatedAtUtc ?? timeProvider.GetUtcNow();
        if (string.IsNullOrWhiteSpace(installId))
        {
            installId = Guid.NewGuid().ToString("N");
            createdAtUtc = timeProvider.GetUtcNow();
            logger?.LogInformation("Minted a new TechieDesk install identity in {DataDirectory}", dataDirectory);
        }

        if (hasMoved)
        {
            logger?.LogWarning(
                "The TechieDesk data directory carries an install identity minted against a different "
                + "machine fingerprint. The install id is kept and the fingerprint refreshed; nothing "
                + "is blocked locally (REQ-FN-051, degrade never lock).");
        }

        var needsWrite = stored is null
            || hasMoved
            || !string.Equals(stored.InstallId, installId, StringComparison.Ordinal);

        if (needsWrite)
        {
            Save(dataDirectory, new InstallIdentityDocument
            {
                InstallId = installId,
                MachineFingerprint = fingerprint.Value,
                CreatedAtUtc = createdAtUtc
            }, logger);
        }

        return new InstallIdentity
        {
            InstallId = installId,
            MachineFingerprint = fingerprint.Value,
            CompositeId = ComposeId(installId, fingerprint.Value),
            CreatedAtUtc = createdAtUtc,
            IsMachineBound = fingerprint.IsPlatformStable,
            HasMovedMachine = hasMoved
        };
    }

    /// <summary>Reads the identity file, treating every failure as "absent".</summary>
    /// <param name="dataDirectory">The absolute data directory.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>The stored document, or null when there is none that can be read.</returns>
    private static InstallIdentityDocument? TryRead(string dataDirectory, ILogger? logger)
    {
        var path = FilePath(dataDirectory);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<InstallIdentityDocument>(
                File.ReadAllText(path), JsonOptions);
            return string.IsNullOrWhiteSpace(document?.InstallId) ? null : document;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex,
                "The install identity file at {Path} could not be read; a new identity will be minted", path);
            return null;
        }
    }

    /// <summary>Writes the identity file, treating every failure as non-fatal.</summary>
    /// <param name="dataDirectory">The absolute data directory.</param>
    /// <param name="document">The document to persist.</param>
    /// <param name="logger">Optional logger.</param>
    /// <remarks>
    /// Written through a temporary file and moved into place so a crash mid-write cannot leave a
    /// truncated identity that the next launch would discard, silently re-minting and looking to the
    /// server like a brand new install.
    /// </remarks>
    private static void Save(string dataDirectory, InstallIdentityDocument document, ILogger? logger)
    {
        var path = FilePath(dataDirectory);
        var temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex,
                "The install identity could not be persisted to {Path}; this launch has a usable "
                + "identity but it will not survive a restart", path);
        }
    }
}
