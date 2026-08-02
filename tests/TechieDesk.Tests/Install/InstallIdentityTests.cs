using TechieDesk.Services.Install;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Install;

/// <summary>
/// REQ-FN-051 clause 1 (BRD-143): the app computes a stable local install identity that survives
/// restart and is not trivially clonable, and clause 5's half of that — identity stability across a
/// simulated restart.
/// </summary>
/// <remarks>
/// Every test here drives the REAL <see cref="InstallIdentityStore"/> against a sandbox directory,
/// so what is asserted is what an install will actually do to the file on disk. A "restart" is a
/// second <c>Load</c> against the same directory, which is exactly what a relaunch performs — the
/// store holds no process state, so there is nothing a mock could hide.
/// </remarks>
public sealed class InstallIdentityTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

    private readonly string dataDirectory = Path.Combine(
        Path.GetTempPath(), "techiedesk-install-identity-" + Guid.NewGuid().ToString("N"));

    /// <summary>Removes the sandbox directory.</summary>
    public void Dispose()
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static MachineFingerprint Machine(string rawValue) =>
        new(PlatformMachineFingerprintProvider.Hash(rawValue), MachineFingerprintSource.Supplied, true);

    /// <summary>A first launch mints an identity and writes it into the data directory.</summary>
    [Fact]
    public void FirstLaunchMintsAndPersistsAnIdentity()
    {
        var identity = InstallIdentityStore.Load(
            dataDirectory, Machine("mac-a"), new FixedTimeProvider(Now));

        Assert.False(string.IsNullOrWhiteSpace(identity.InstallId));
        Assert.True(File.Exists(InstallIdentityStore.FilePath(dataDirectory)));
    }

    /// <summary>
    /// The identity survives a restart: a second load on the same machine returns the same install
    /// id and the same composite id. This is acceptance clause (1)'s "stable across restart".
    /// </summary>
    [Fact]
    public void IdentityIsStableAcrossRestart()
    {
        var first = InstallIdentityStore.Load(dataDirectory, Machine("mac-a"), new FixedTimeProvider(Now));

        // A relaunch: a later clock, a fresh call, the same directory and the same machine.
        var second = InstallIdentityStore.Load(
            dataDirectory, Machine("mac-a"), new FixedTimeProvider(Now.AddDays(30)));

        Assert.Equal(first.InstallId, second.InstallId);
        Assert.Equal(first.CompositeId, second.CompositeId);
        Assert.Equal(first.CreatedAtUtc, second.CreatedAtUtc);
        Assert.False(second.HasMovedMachine);
    }

    /// <summary>
    /// A data directory copied to another machine yields a DIFFERENT composite identity while
    /// keeping the same install id — so a server can tell "the same install moved" from "a second
    /// install", which is the point of clause (1)'s "not trivially clonable".
    /// </summary>
    [Fact]
    public void CopyingTheDataDirectoryToAnotherMachineChangesTheCompositeIdentity()
    {
        var original = InstallIdentityStore.Load(
            dataDirectory, Machine("mac-a"), new FixedTimeProvider(Now));

        var clone = Path.Combine(Path.GetTempPath(), "techiedesk-clone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(clone);
        try
        {
            File.Copy(
                InstallIdentityStore.FilePath(dataDirectory),
                InstallIdentityStore.FilePath(clone));

            var moved = InstallIdentityStore.Load(clone, Machine("mac-b"), new FixedTimeProvider(Now));

            Assert.Equal(original.InstallId, moved.InstallId);
            Assert.NotEqual(original.CompositeId, moved.CompositeId);
            Assert.True(moved.HasMovedMachine);
        }
        finally
        {
            Directory.Delete(clone, recursive: true);
        }
    }

    /// <summary>
    /// A move is reported once and then settles: the refreshed fingerprint is persisted, so the very
    /// next launch on the new machine is an ordinary stable launch rather than a permanent alarm.
    /// </summary>
    [Fact]
    public void AMoveIsReportedOnceAndThenSettles()
    {
        InstallIdentityStore.Load(dataDirectory, Machine("mac-a"), new FixedTimeProvider(Now));
        var moved = InstallIdentityStore.Load(dataDirectory, Machine("mac-b"), new FixedTimeProvider(Now));
        var settled = InstallIdentityStore.Load(dataDirectory, Machine("mac-b"), new FixedTimeProvider(Now));

        Assert.True(moved.HasMovedMachine);
        Assert.False(settled.HasMovedMachine);
        Assert.Equal(moved.CompositeId, settled.CompositeId);
    }

    /// <summary>
    /// The raw machine value never reaches disk or the wire — only its salted hash and a hash of
    /// that (REQ-NFR-008: nothing that could act as a hardware tracker leaves the machine).
    /// </summary>
    [Fact]
    public void NeitherTheStoredFileNorTheCompositeIdCarriesTheRawMachineValue()
    {
        const string RawMachineValue = "519C2FF2-E74D-59BE-B9B4-4A6F3E2BD934";

        var identity = InstallIdentityStore.Load(
            dataDirectory, Machine(RawMachineValue), new FixedTimeProvider(Now));
        var fileText = File.ReadAllText(InstallIdentityStore.FilePath(dataDirectory));

        Assert.DoesNotContain(RawMachineValue, fileText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawMachineValue, identity.CompositeId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(identity.MachineFingerprint, identity.CompositeId, StringComparison.Ordinal);
    }

    /// <summary>
    /// A corrupt identity file is treated as absent and a fresh identity is minted, because a
    /// half-written JSON file must never be able to stop the app (degrade, never lock).
    /// </summary>
    [Fact]
    public void ACorruptIdentityFileIsReplacedRatherThanFatal()
    {
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(InstallIdentityStore.FilePath(dataDirectory), "{ this is not json");

        var identity = InstallIdentityStore.Load(
            dataDirectory, Machine("mac-a"), new FixedTimeProvider(Now));

        Assert.False(string.IsNullOrWhiteSpace(identity.InstallId));
        Assert.False(identity.HasMovedMachine);
    }

    /// <summary>
    /// A host with no platform-stable identifier still gets a usable identity; the loss is reported
    /// through <c>IsMachineBound</c> rather than by failing.
    /// </summary>
    [Fact]
    public void AnUnstableFingerprintStillProducesAUsableIdentity()
    {
        var weak = new MachineFingerprint(
            PlatformMachineFingerprintProvider.Hash("some-host"),
            MachineFingerprintSource.MachineName,
            IsPlatformStable: false);

        var identity = InstallIdentityStore.Load(dataDirectory, weak, new FixedTimeProvider(Now));

        Assert.False(string.IsNullOrWhiteSpace(identity.CompositeId));
        Assert.False(identity.IsMachineBound);
    }

    /// <summary>
    /// The real platform probe answers on this host and answers the SAME way twice — the property
    /// the whole identity rests on. Asserted against the live machine, not a stub.
    /// </summary>
    [Fact]
    public void ThePlatformFingerprintIsRepeatableOnThisHost()
    {
        var provider = new PlatformMachineFingerprintProvider(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PlatformMachineFingerprintProvider>.Instance);

        var first = provider.Get();
        var second = new PlatformMachineFingerprintProvider(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PlatformMachineFingerprintProvider>.Instance)
            .Get();

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(64, first.Value.Length);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            Assert.Equal(MachineFingerprintSource.MacPlatformUuid, first.Source);
            Assert.True(first.IsPlatformStable);
        }
    }

    /// <summary>
    /// The <c>.tdbak</c> archive cannot carry the install identity: the packer's entry allow-list is
    /// fixed, so a restored backup MINTS A NEW identity rather than inheriting the exporter's seat.
    /// Asserted against the shipped allow-list so a future entry cannot quietly change the answer.
    /// </summary>
    [Fact]
    public void TheBackupArchiveCannotCarryTheInstallIdentity()
    {
        var entries = TechieDesk.Services.Backup.BackupArchive.ContentEntryNames;

        Assert.DoesNotContain(entries, entry =>
            entry.Contains("install", StringComparison.OrdinalIgnoreCase)
            || entry.Contains("identity", StringComparison.OrdinalIgnoreCase));
        Assert.False(
            TechieDesk.Services.Backup.BackupArchive.IsKnownEntryName(InstallIdentityStore.FileName),
            "install-identity.json must not be an archive entry — a restored backup is a different "
                + "install and must mint its own identity (REQ-FN-051 / REQ-FN-046).");
    }
}
