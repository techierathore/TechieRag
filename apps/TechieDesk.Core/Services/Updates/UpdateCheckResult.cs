namespace TechieDesk.Services.Updates;

/// <summary>
/// What an update check concluded (REQ-FN-038b).
/// </summary>
public enum UpdateCheckStatus
{
    /// <summary>No check has run yet in this session.</summary>
    NotChecked = 0,

    /// <summary>The installed build is the newest the channel offers.</summary>
    UpToDate = 1,

    /// <summary>A newer build is available.</summary>
    UpdateAvailable = 2,

    /// <summary>The check could not complete. NOT the same as up to date.</summary>
    Failed = 3,
}

/// <summary>
/// The outcome of an update check (REQ-FN-038b).
/// </summary>
/// <param name="Status">What the check concluded.</param>
/// <param name="CurrentVersion">The version the check compared against.</param>
/// <param name="Release">The newer release, when one was found.</param>
/// <param name="Asset">The file that installs <paramref name="Release"/> on this platform, when there is one.</param>
/// <param name="Error">Operator-facing failure description, when the check failed.</param>
/// <param name="CheckedAtUtc">When the check ran.</param>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    ReleaseVersion CurrentVersion,
    AvailableRelease? Release,
    ReleaseAsset? Asset,
    string? Error,
    DateTimeOffset CheckedAtUtc)
{
    /// <summary>
    /// Gets a value indicating whether an update exists but cannot be downloaded in-app.
    /// </summary>
    /// <remarks>
    /// True when a newer release exists but carries no artefact for this platform — which is exactly
    /// what a run of the packaging workflow whose Windows job failed would produce. Surfacing it
    /// separately keeps a broken publish from looking like "no update", and points the operator at
    /// the release page instead of leaving a dead Download button.
    /// </remarks>
    public bool UpdateHasNoDownloadForThisPlatform =>
        Status == UpdateCheckStatus.UpdateAvailable && Asset is null;
}
