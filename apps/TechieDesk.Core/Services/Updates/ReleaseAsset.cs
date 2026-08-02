namespace TechieDesk.Services.Updates;

/// <summary>
/// A downloadable file attached to a release (REQ-FN-038b).
/// </summary>
/// <param name="Name">The file name, e.g. <c>TechieDesk-1.2.0-macos-universal-unsigned.dmg</c>.</param>
/// <param name="DownloadUrl">Direct download URL.</param>
/// <param name="SizeBytes">Size the feed reports, used to show progress and to detect a truncated download.</param>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long SizeBytes);
