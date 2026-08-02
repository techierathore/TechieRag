namespace TechieDesk.Services.Install;

/// <summary>
/// Answers whether a recorded lock owner is still running (REQ-FN-051 clause 3).
/// </summary>
/// <remarks>
/// A seam because the case that matters most — a lock left behind by a process that has since died —
/// cannot be produced reliably by killing a real process inside a test run, and a stale-lock bug is
/// exactly the kind that only shows up on a user's machine after a crash.
/// </remarks>
public interface IProcessLiveness
{
    /// <summary>Determines whether the identified process is still running.</summary>
    /// <param name="processId">The recorded owner's process id.</param>
    /// <param name="startedAtUtc">
    /// The recorded owner's start time, used to reject a recycled process id; null when the record
    /// did not carry one.
    /// </param>
    /// <returns>
    /// True when a process with that id is running and (when a start time was recorded) started at
    /// that time. False otherwise, including when the answer cannot be determined — an unknown owner
    /// must never keep the app shut.
    /// </returns>
    bool IsAlive(int processId, DateTimeOffset? startedAtUtc);
}
