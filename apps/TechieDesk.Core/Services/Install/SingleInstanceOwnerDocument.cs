namespace TechieDesk.Services.Install;

/// <summary>
/// The on-disk shape of <c>techiedesk.instance.json</c>. Internal: a diagnostic record, not a
/// supported format, and never the authority on whether the directory is held.
/// </summary>
internal sealed class SingleInstanceOwnerDocument
{
    /// <summary>Process id of the instance that took the lock.</summary>
    public int ProcessId { get; set; }

    /// <summary>When that process started, used to reject a recycled process id.</summary>
    public DateTimeOffset? StartedAtUtc { get; set; }

    /// <summary>When the lock was taken.</summary>
    public DateTimeOffset AcquiredAtUtc { get; set; }

    /// <summary>Host name, so a record found on a shared volume is legible.</summary>
    public string? MachineName { get; set; }
}
