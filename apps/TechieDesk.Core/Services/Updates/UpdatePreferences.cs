namespace TechieDesk.Services.Updates;

/// <summary>
/// The operator's own update choices for this install (REQ-FN-038b).
/// </summary>
/// <param name="AutoCheckOnLaunch">Whether the app checks for updates by itself at launch.</param>
/// <param name="IncludePrerelease">Whether prerelease builds are offered.</param>
/// <param name="LastCheckedUtc">When a check last completed, or null if never.</param>
public sealed record UpdatePreferences(
    bool AutoCheckOnLaunch,
    bool IncludePrerelease,
    DateTimeOffset? LastCheckedUtc);
