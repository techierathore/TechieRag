namespace TechieDesk.Services.Install;

/// <summary>
/// What the data-directory single-instance guard concluded (REQ-FN-051 clause 3).
/// </summary>
/// <remarks>
/// Three of the four members mean "carry on and open the window". Only
/// <see cref="AlreadyRunning"/> refuses, and it refuses because another LIVE process holds the same
/// data directory — never because of anything to do with a licence (BRD-129).
/// </remarks>
public enum SingleInstanceOutcome
{
    /// <summary>The lock was taken cleanly; this process owns the data directory.</summary>
    Acquired = 0,

    /// <summary>
    /// The lock was taken after discarding an ownership record left by a process that is no longer
    /// alive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This covers a crash or a force-quit, and also an ordinary quit: a UIKit app is terminated by
    /// the system rather than unwinding, so the ownership record is normally left behind and cleaned
    /// up here on the next launch. It is therefore reported, not treated as evidence of a fault, and
    /// above all it is never a reason to refuse — a guard that bricks the app after a crash is worse
    /// than no guard.
    /// </para>
    /// <para>
    /// The lock itself is never inherited from the record: the OS released the previous holder's
    /// handle when its process ended, which is what makes this outcome reachable at all.
    /// </para>
    /// </remarks>
    ReclaimedStaleLock = 1,

    /// <summary>Another live process already holds this data directory. This one must refuse.</summary>
    AlreadyRunning = 2,

    /// <summary>
    /// The guard could not be applied here — a read-only or otherwise unlockable directory. The app
    /// continues unguarded rather than refusing to start.
    /// </summary>
    Unenforceable = 3
}
