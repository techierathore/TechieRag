namespace TechieDesk.Services.Install;

/// <summary>
/// The outcome of one attempt to take the data-directory lock (REQ-FN-051 clause 3).
/// </summary>
public sealed class SingleInstanceResult
{
    /// <summary>Initializes a new instance of the <see cref="SingleInstanceResult"/> class.</summary>
    /// <param name="outcome">What the guard concluded.</param>
    /// <param name="dataDirectory">The data directory the attempt was scoped to.</param>
    /// <param name="ownerProcessId">The recorded owner's process id, when one was readable.</param>
    /// <param name="heldLock">The held lock, when the attempt succeeded.</param>
    internal SingleInstanceResult(
        SingleInstanceOutcome outcome,
        string dataDirectory,
        int? ownerProcessId,
        SingleInstanceLock? heldLock)
    {
        Outcome = outcome;
        DataDirectory = dataDirectory;
        OwnerProcessId = ownerProcessId;
        Lock = heldLock;
    }

    /// <summary>Gets what the guard concluded.</summary>
    public SingleInstanceOutcome Outcome { get; }

    /// <summary>Gets the data directory the guard was scoped to.</summary>
    /// <remarks>
    /// The guard is per-directory, not per-application. Two copies of TechieDesk pointed at two
    /// different directories (via <c>AppDb:DataDirectory</c>) are not a conflict and are not refused
    /// — the acceptance clause is about two copies racing on ONE data directory.
    /// </remarks>
    public string DataDirectory { get; }

    /// <summary>Gets the process id recorded by the current owner, when it could be read.</summary>
    /// <remarks>Null when no ownership record existed or it was unreadable.</remarks>
    public int? OwnerProcessId { get; }

    /// <summary>Gets the held lock; null unless this process took the directory.</summary>
    /// <remarks>Hold it for the life of the process and dispose it on shutdown.</remarks>
    public SingleInstanceLock? Lock { get; }

    /// <summary>
    /// Gets a value indicating whether this process may proceed to open its window.
    /// </summary>
    /// <remarks>
    /// True for every outcome except <see cref="SingleInstanceOutcome.AlreadyRunning"/>. This is
    /// NOT a licence decision and has no licence input: an install with no account, no licence
    /// server and no session is always allowed through (BRD-129).
    /// </remarks>
    public bool IsPrimaryInstance => Outcome != SingleInstanceOutcome.AlreadyRunning;
}
