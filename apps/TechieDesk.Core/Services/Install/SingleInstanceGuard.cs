using System.Diagnostics;
using System.Text.Json;

namespace TechieDesk.Services.Install;

/// <summary>
/// Stops two copies of TechieDesk racing on one data directory (REQ-FN-051 clause 3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists in addition to <c>LSMultipleInstancesProhibited</c>.</b> That Info.plist key
/// covers the Finder/Dock case and nothing else: launching the binary directly, or launching a
/// second COPY of the bundle, or pointing a second copy at the same <c>AppDb:DataDirectory</c>, all
/// sail straight past it. The acceptance clause is about two copies "racing on one data directory",
/// and the only thing that can enforce that is a lock scoped to the directory itself. Both are
/// shipped; neither replaces the other.
/// </para>
/// <para>
/// <b>Two files, and the split matters.</b> <c>techiedesk.lock</c> is an empty sentinel held open
/// with <see cref="FileShare.None"/> — the exclusion is the open handle, which the OS releases on
/// crash, force-quit and power loss alike, so the guard cannot outlive the process that set it.
/// <c>techiedesk.instance.json</c> is a plain readable record of who holds it, kept separate
/// precisely because a <see cref="FileShare.None"/> handle cannot be read by anyone else — a
/// one-file design would have made the refusal message impossible to write.
/// </para>
/// <para>
/// <b>Only the desktop head calls this, and that is not an oversight.</b> The
/// <c>TechieDeskScheduler</c> helper hosts the same scheduler against the same data directory ON
/// PURPOSE (REQ-FN-042 / BRD-139 / ADR-009) — whichever process is alive runs the schedules, and the
/// per-schedule in-flight guard is what stops both hosting one job. Applying this guard there would
/// stop the helper and the app coexisting, which is the arrangement BRD-139 specifies. The failure
/// mode this type prevents is two copies of the WINDOWED APP, each opening the database and each
/// believing it owns it.
/// </para>
/// <para>
/// <b>It cannot brick the app.</b> Every failure that is not "another live process has it" resolves
/// to letting the app start: an ownership record naming a dead process is discarded
/// (<see cref="SingleInstanceOutcome.ReclaimedStaleLock"/>), and a directory that cannot be locked
/// at all is reported <see cref="SingleInstanceOutcome.Unenforceable"/> and proceeds. There is no
/// input to this type that can be tampered with to keep a user out of their own data.
/// </para>
/// </remarks>
public static class SingleInstanceGuard
{
    /// <summary>Name of the empty sentinel file whose open handle IS the lock.</summary>
    public const string LockFileName = "techiedesk.lock";

    /// <summary>Name of the readable record naming the process that holds the lock.</summary>
    public const string OwnerFileName = "techiedesk.instance.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Attempts to take exclusive ownership of a data directory for this process.
    /// </summary>
    /// <param name="dataDirectory">
    /// The absolute data directory to guard, normally <c>DataDirectory.ResolveAndCreate</c>. Created
    /// when missing.
    /// </param>
    /// <param name="liveness">
    /// How a recorded owner's liveness is decided; defaults to the real process table.
    /// </param>
    /// <param name="timeProvider">Clock stamped into the ownership record; defaults to the system clock.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>
    /// The outcome. Check <see cref="SingleInstanceResult.IsPrimaryInstance"/>; when it is true the
    /// caller must keep <see cref="SingleInstanceResult.Lock"/> alive for the life of the process.
    /// </returns>
    /// <remarks>
    /// Never throws for an ordinary environment failure. A caller that cannot start because this
    /// method threw would be a worse defect than the one it prevents.
    /// </remarks>
    public static SingleInstanceResult TryAcquire(
        string dataDirectory,
        IProcessLiveness? liveness = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        liveness ??= SystemProcessLiveness.Instance;
        timeProvider ??= TimeProvider.System;

        var lockPath = Path.Combine(dataDirectory, LockFileName);
        var ownerPath = Path.Combine(dataDirectory, OwnerFileName);

        try
        {
            Directory.CreateDirectory(dataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex,
                "The data directory {DataDirectory} could not be created, so the single-instance "
                + "guard cannot be applied; startup continues unguarded", dataDirectory);
            return new SingleInstanceResult(
                SingleInstanceOutcome.Unenforceable, dataDirectory, null, null);
        }

        var owner = TryReadOwner(ownerPath, logger);

        FileStream stream;
        try
        {
            stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            // Contention: some process has the sentinel open. That process is authoritative whether
            // or not the ownership record agrees, because the OS would have released the handle if
            // its holder had died.
            var ownerIsAlive = owner is not null && liveness.IsAlive(owner.ProcessId, owner.StartedAtUtc);
            logger?.LogWarning(
                "TechieDesk is already running against {DataDirectory} (owner pid {ProcessId}, "
                + "recorded owner alive: {OwnerIsAlive}); this instance will refuse to start",
                dataDirectory, owner?.ProcessId, ownerIsAlive);
            return new SingleInstanceResult(
                SingleInstanceOutcome.AlreadyRunning, dataDirectory, owner?.ProcessId, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or NotSupportedException)
        {
            logger?.LogWarning(ex,
                "The data directory {DataDirectory} cannot be locked on this file system; startup "
                + "continues unguarded", dataDirectory);
            return new SingleInstanceResult(
                SingleInstanceOutcome.Unenforceable, dataDirectory, null, null);
        }

        // The lock is ours. Anything the previous owner left behind is by definition stale.
        var reclaimed = owner is not null && !liveness.IsAlive(owner.ProcessId, owner.StartedAtUtc);
        if (reclaimed)
        {
            logger?.LogInformation(
                "Reclaimed the data directory {DataDirectory} from process {ProcessId}, which is no "
                + "longer running", dataDirectory, owner!.ProcessId);
        }

        WriteOwner(ownerPath, timeProvider, logger);

        return new SingleInstanceResult(
            reclaimed ? SingleInstanceOutcome.ReclaimedStaleLock : SingleInstanceOutcome.Acquired,
            dataDirectory,
            Environment.ProcessId,
            new SingleInstanceLock(stream, ownerPath));
    }

    /// <summary>Reads the ownership record, treating every failure as "no record".</summary>
    /// <param name="ownerPath">Absolute path of the ownership record.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>The record, or null when there is none that can be read.</returns>
    private static SingleInstanceOwnerDocument? TryReadOwner(string ownerPath, ILogger? logger)
    {
        if (!File.Exists(ownerPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SingleInstanceOwnerDocument>(
                File.ReadAllText(ownerPath), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex,
                "The instance ownership record at {Path} could not be read; treating it as absent", ownerPath);
            return null;
        }
    }

    /// <summary>Records this process as the owner of the data directory.</summary>
    /// <param name="ownerPath">Absolute path of the ownership record.</param>
    /// <param name="timeProvider">Clock for the acquisition stamp.</param>
    /// <param name="logger">Optional logger.</param>
    private static void WriteOwner(string ownerPath, TimeProvider timeProvider, ILogger? logger)
    {
        DateTimeOffset? startedAtUtc = null;
        try
        {
            using var current = Process.GetCurrentProcess();
            startedAtUtc = new DateTimeOffset(current.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            // Without a start time the record still works; it just cannot rule out a recycled pid.
            logger?.LogDebug(ex, "Could not read this process's start time for the ownership record");
        }

        var document = new SingleInstanceOwnerDocument
        {
            ProcessId = Environment.ProcessId,
            StartedAtUtc = startedAtUtc,
            AcquiredAtUtc = timeProvider.GetUtcNow(),
            MachineName = Environment.MachineName
        };

        try
        {
            File.WriteAllText(ownerPath, JsonSerializer.Serialize(document, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex,
                "Could not write the instance ownership record to {Path}; the lock is still held, "
                + "but a second instance will not be able to name this one", ownerPath);
        }
    }
}
