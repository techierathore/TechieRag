using TechieDesk.Services.Install;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Install;

/// <summary>
/// REQ-FN-051 clause 3 (BRD-143): a single-instance guard prevents two copies of the same install
/// racing on one data directory — and clause 5's other half, the second-instance refusal, plus the
/// stale-lock recovery the requirement calls out by name.
/// </summary>
/// <remarks>
/// These drive the real <see cref="SingleInstanceGuard"/> against sandbox directories and assert on
/// what happens to real files and real OS locks. The only thing stubbed is process liveness, because
/// the case that matters — a lock left by a process that has since died — cannot be produced by
/// killing a process inside a test run, and it is the exact case that would brick a user's app after
/// a crash if it were wrong.
/// </remarks>
public sealed class SingleInstanceGuardTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

    private readonly List<string> directories = [];

    /// <summary>Removes every sandbox directory the test created.</summary>
    public void Dispose()
    {
        foreach (var directory in directories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string NewDataDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "techiedesk-single-instance-" + Guid.NewGuid().ToString("N"));
        directories.Add(directory);
        return directory;
    }

    /// <summary>A first launch takes the directory and holds a lock.</summary>
    [Fact]
    public void FirstInstanceAcquiresTheDataDirectory()
    {
        var directory = NewDataDirectory();

        var result = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.AllAlive, new FixedTimeProvider(Now));
        using var held = result.Lock;

        Assert.Equal(SingleInstanceOutcome.Acquired, result.Outcome);
        Assert.True(result.IsPrimaryInstance);
        Assert.NotNull(result.Lock);
        Assert.True(File.Exists(Path.Combine(directory, SingleInstanceGuard.LockFileName)));
        Assert.True(File.Exists(Path.Combine(directory, SingleInstanceGuard.OwnerFileName)));
    }

    /// <summary>
    /// A SECOND instance against the same data directory is refused while the first still holds it.
    /// This is the acceptance clause: two copies racing on one data directory.
    /// </summary>
    [Fact]
    public void SecondInstanceIsRefusedWhileTheFirstHoldsTheDirectory()
    {
        var directory = NewDataDirectory();

        var first = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.AllAlive, new FixedTimeProvider(Now));
        using var held = first.Lock;

        var second = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.AllAlive, new FixedTimeProvider(Now));

        Assert.Equal(SingleInstanceOutcome.AlreadyRunning, second.Outcome);
        Assert.False(second.IsPrimaryInstance);
        Assert.Null(second.Lock);
        Assert.Equal(Environment.ProcessId, second.OwnerProcessId);
    }

    /// <summary>The refusal names the data folder and the owning process, so it cannot read as a crash.</summary>
    [Fact]
    public void TheRefusalMessageNamesTheFolderAndTheOwner()
    {
        var directory = NewDataDirectory();

        var first = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.AllAlive, new FixedTimeProvider(Now));
        using var held = first.Lock;

        var second = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.AllAlive, new FixedTimeProvider(Now));
        var state = new SingleInstanceState(second);

        Assert.Equal(SingleInstanceState.RefusalMessageWithOwnerKey, state.RefusalDetailKey);

        // Rendered through the REAL localizer, in HINDI, because that is the install the refusal
        // used to be illegible on (REQ-UI-055). The folder and the pid are arguments and are
        // byte-identical whatever the language.
        using var resources = new ResourceHarness("hi");
        var message = resources.Require(state.RefusalDetailKey, [.. state.RefusalDetailArguments]);

        Assert.Contains(directory, message, StringComparison.Ordinal);
        Assert.Contains(Environment.ProcessId.ToString(), message, StringComparison.Ordinal);
    }

    /// <summary>Releasing the lock hands the directory back, so a normal quit-and-relaunch works.</summary>
    [Fact]
    public void ReleasingTheLockLetsTheNextInstanceIn()
    {
        var directory = NewDataDirectory();

        var first = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.AllAlive, new FixedTimeProvider(Now));
        first.Lock!.Dispose();

        var second = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.AllAlive, new FixedTimeProvider(Now));
        using var held = second.Lock;

        Assert.Equal(SingleInstanceOutcome.Acquired, second.Outcome);
        Assert.True(second.IsPrimaryInstance);
    }

    /// <summary>
    /// <b>Stale-lock recovery.</b> After a crash the ownership record survives on disk naming a
    /// process that is gone. The next launch must reclaim the directory and start — a guard that
    /// bricks the app after a crash is worse than no guard.
    /// </summary>
    [Fact]
    public void AStaleOwnershipRecordFromACrashedProcessIsReclaimed()
    {
        var directory = NewDataDirectory();
        var ownerPath = Path.Combine(directory, SingleInstanceGuard.OwnerFileName);

        // Take the directory, then simulate a crash: the record the owner wrote is still there, and
        // the OS has released the handle because the process is gone.
        var first = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.AllAlive, new FixedTimeProvider(Now));
        var crashedRecord = File.ReadAllText(ownerPath);
        first.Lock!.Dispose();
        File.WriteAllText(ownerPath, crashedRecord);

        var afterCrash = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.NoneAlive, new FixedTimeProvider(Now.AddMinutes(1)));
        using var held = afterCrash.Lock;

        Assert.Equal(SingleInstanceOutcome.ReclaimedStaleLock, afterCrash.Outcome);
        Assert.True(afterCrash.IsPrimaryInstance);
        Assert.NotNull(afterCrash.Lock);
    }

    /// <summary>
    /// The lock is scoped to the DATA DIRECTORY, not to the application: two copies pointed at two
    /// directories (<c>AppDb:DataDirectory</c>) are not a conflict and are both allowed.
    /// </summary>
    [Fact]
    public void TwoDataDirectoriesAreTwoIndependentInstances()
    {
        var firstDirectory = NewDataDirectory();
        var secondDirectory = NewDataDirectory();

        var first = SingleInstanceGuard.TryAcquire(
            firstDirectory, StubProcessLiveness.AllAlive, new FixedTimeProvider(Now));
        using var firstLock = first.Lock;
        var second = SingleInstanceGuard.TryAcquire(
            secondDirectory, StubProcessLiveness.AllAlive, new FixedTimeProvider(Now));
        using var secondLock = second.Lock;

        Assert.Equal(SingleInstanceOutcome.Acquired, first.Outcome);
        Assert.Equal(SingleInstanceOutcome.Acquired, second.Outcome);
    }

    /// <summary>An unreadable ownership record is treated as absent, never as a reason to refuse.</summary>
    [Fact]
    public void ACorruptOwnershipRecordDoesNotBlockStartup()
    {
        var directory = NewDataDirectory();
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, SingleInstanceGuard.OwnerFileName), "not json at all");

        var result = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.NoneAlive, new FixedTimeProvider(Now));
        using var held = result.Lock;

        Assert.True(result.IsPrimaryInstance);
        Assert.NotNull(result.Lock);
    }

    /// <summary>
    /// <b>BRD-129 regression guard.</b> The guard is not a licence check and has no licence input: a
    /// brand-new data directory with no account, no session, no licence cache and no AppManager
    /// configuration starts normally. Nothing about REQ-FN-051 may gate an account-free launch.
    /// </summary>
    [Fact]
    public void AnAccountFreeInstallWithNoLicenceArtefactsStartsNormally()
    {
        var directory = NewDataDirectory();

        var result = SingleInstanceGuard.TryAcquire(
            directory, StubProcessLiveness.NoneAlive, new FixedTimeProvider(Now));
        using var held = result.Lock;

        var identity = InstallIdentityStore.Load(
            directory,
            new MachineFingerprint(
                PlatformMachineFingerprintProvider.Hash("host"), MachineFingerprintSource.Supplied, true),
            new FixedTimeProvider(Now));

        Assert.True(result.IsPrimaryInstance);
        Assert.False(string.IsNullOrWhiteSpace(identity.CompositeId));

        // No licence artefact was created, read or required to get this far.
        Assert.False(File.Exists(Path.Combine(directory, "techiedesk.db")));
        Assert.Empty(Directory.GetFiles(directory, "*.db"));
    }
}
