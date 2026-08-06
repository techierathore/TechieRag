using TechieDeskDb;

namespace TechieDesk.Services.Updates;

/// <summary>
/// A published desktop release as advertised by the update feed (REQ-FN-038b).
/// </summary>
/// <param name="Version">The parsed version.</param>
/// <param name="TagName">The tag the release was published from, e.g. <c>desktop-v1.2.0</c>.</param>
/// <param name="Name">The release's display title.</param>
/// <param name="IsPrerelease">Whether the publisher marked this release as a prerelease.</param>
/// <param name="ReleaseNotes">The release body, as authored. May be empty.</param>
/// <param name="ReleasePageUrl">Human-facing page for this release.</param>
/// <param name="PublishedAtUtc">When the release was published, or null when unknown.</param>
/// <param name="Assets">Downloadable files attached to the release.</param>
public sealed record AvailableRelease(
    ReleaseVersion Version,
    string TagName,
    string Name,
    bool IsPrerelease,
    string ReleaseNotes,
    string ReleasePageUrl,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>
    /// Selects the asset that installs this release on the given platform.
    /// </summary>
    /// <param name="platform">The host platform.</param>
    /// <returns>The matching asset, or null when the release carries no build for it.</returns>
    /// <remarks>
    /// Matched on file extension rather than on the full artefact name. The names the packaging
    /// workflow produces today carry an <c>-unsigned</c> segment that will disappear the moment
    /// REQ-FN-038c supplies a signing identity; matching the whole name would mean every existing
    /// install silently stopped finding downloads on the first signed release.
    /// </remarks>
    public ReleaseAsset? AssetFor(DataDirectoryPlatform platform)
    {
        var extension = platform switch
        {
            DataDirectoryPlatform.MacOS => ".dmg",
            DataDirectoryPlatform.Windows => ".zip",
            _ => null,
        };

        return extension is null
            ? null
            : Assets.FirstOrDefault(asset =>
                asset.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }
}
