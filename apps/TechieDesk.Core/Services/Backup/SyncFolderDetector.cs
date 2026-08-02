using TechieDesk.Services.Localization;

namespace TechieDesk.Services.Backup;

/// <summary>A consumer file-sync product recognised by <see cref="SyncFolderDetector"/>.</summary>
/// <param name="Name">
/// The product's BRAND name — <c>OneDrive</c>, <c>Dropbox</c>, <c>iCloud Drive</c> — which is written
/// in Latin script inside a Devanagari sentence, exactly as the localization standard requires of
/// every product noun. Empty when the match was a generic sync location with no brand behind it.
/// </param>
/// <param name="NameKey">
/// Resource key naming the product when there is no brand to print — the macOS
/// <c>Library/CloudStorage</c> mount, which could be any of three clients. Null for a branded match.
/// </param>
/// <param name="MatchedSegment">The path segment that identified it.</param>
/// <remarks>
/// REQ-UI-055 (BRD-91): the generic name used to be the English words "a cloud-storage provider",
/// interpolated straight into a localized alert title, so a Hindi install rendered a Devanagari
/// heading with an English noun phrase inside it.
/// </remarks>
public sealed record SyncFolderMatch(string Name, string? NameKey, string MatchedSegment);

/// <summary>
/// Detects whether a path sits inside a consumer cloud-sync folder (REQ-FN-047, ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// This exists to enforce one prohibition and to permit its mirror image, and the difference between
/// the two is the whole point of ADR-013.
/// </para>
/// <para>
/// <b>Prohibited: the live data directory inside a sync folder.</b> That directory holds a live
/// SQLite database and a live embedded vector store. Consumer sync clients perform partial-write
/// sync with no locking semantics and resolve races by producing "conflict copies", which corrupts
/// both — and the corruption surfaces later, as unreadable pages or silently missing rows, not as a
/// failure at the moment of sync. Pointing the data directory at OneDrive is the obvious way to
/// share a workspace and it is the one that destroys data, so it is warned about explicitly rather
/// than left to be discovered.
/// </para>
/// <para>
/// <b>Intended: an ARCHIVE file inside a sync folder.</b> A <c>.tdbak</c> is inert — written once,
/// closed, never held open — so dropping it in a shared folder is exactly how BRD-144 expects a team
/// to hand work to a colleague. The detector is therefore never applied to an export destination.
/// </para>
/// <para>
/// Detection is by path segment rather than by interrogating the sync clients, which is cheap,
/// needs no entitlements and no network, and cannot itself fail. It is a best-effort warning, not a
/// gate: a renamed sync root will not be recognised, so nothing is blocked on the strength of it.
/// </para>
/// </remarks>
public static class SyncFolderDetector
{
    /// <summary>
    /// Path segments that identify a sync root, mapped to the product name to report.
    /// </summary>
    /// <remarks>
    /// <c>Library/CloudStorage</c> is macOS's File-Provider location, which is where every modern
    /// OneDrive, Dropbox and Google Drive client actually mounts — matching only the vendor names
    /// would miss all three on a current macOS install. <c>com~apple~CloudDocs</c> is iCloud Drive's
    /// on-disk spelling; users never see it, so matching only the words "iCloud Drive" would miss it.
    /// </remarks>
    private static readonly (string Segment, string Product, string? ProductKey)[] KnownSyncSegments =
    [
        ("onedrive", "OneDrive", null),
        ("dropbox", "Dropbox", null),
        ("google drive", "Google Drive", null),
        ("googledrive", "Google Drive", null),
        ("gdrive", "Google Drive", null),
        ("icloud drive", "iCloud Drive", null),
        ("com~apple~clouddocs", "iCloud Drive", null),
        ("library/cloudstorage", "", GenericProviderKey),
        ("box sync", "Box", null),
        ("pcloudrive", "pCloud", null),
        ("mega", "MEGA", null),
        ("syncthing", "Syncthing", null)
    ];

    /// <summary>Resource key naming a sync client that could not be identified by brand.</summary>
    public const string GenericProviderKey = "BackupSyncProviderGeneric";

    /// <summary>
    /// Resource key for the warning shown when the live data directory is inside a sync folder.
    /// </summary>
    /// <remarks>
    /// Takes one placeholder: the product name, which is either a brand written in Latin script or
    /// the value of <see cref="GenericProviderKey"/> resolved by the caller.
    /// </remarks>
    public const string DataDirectoryRiskKey = "BackupSyncRiskDetail";

    /// <summary>Determines whether a path appears to sit inside a cloud-sync folder.</summary>
    /// <param name="path">An absolute path; typically the resolved data directory.</param>
    /// <returns>The matched product, or null when nothing recognisable was found.</returns>
    public static SyncFolderMatch? Detect(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // Compare on a separator-normalised, lowercased form so one segment list covers both hosts
        // and so "Library/CloudStorage" matches a Windows-style path just as well.
        var normalised = path.Replace('\\', '/').ToLowerInvariant();

        foreach (var (segment, product, productKey) in KnownSyncSegments)
        {
            if (!normalised.Contains(segment, StringComparison.Ordinal))
            {
                continue;
            }

            // Require the hit to be a whole path segment, or the start of one. Without this, a user
            // whose surname happens to contain a product name — or a folder called "Megabytes" —
            // would be warned about a sync client they do not run.
            if (IsSegmentBoundaryMatch(normalised, segment))
            {
                return new SyncFolderMatch(product, productKey, segment);
            }
        }

        return null;
    }

    /// <summary>
    /// Names the detected product for display, resolving the generic case through a localizer.
    /// </summary>
    /// <param name="match">The detected sync product.</param>
    /// <param name="localize">Resolves a resource key in the reader's language.</param>
    /// <returns>The brand name, or the translated generic phrase when there is no brand.</returns>
    /// <exception cref="ArgumentNullException">Either argument was null.</exception>
    /// <remarks>
    /// The one place the brand-or-key choice is made, so the alert TITLE and the alert BODY can never
    /// disagree about what the folder is called.
    /// </remarks>
    public static string ProductName(SyncFolderMatch match, LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(localize);

        return match.NameKey is { Length: > 0 } key ? localize(key) : match.Name;
    }

    /// <summary>Determines whether a substring hit begins at a path-segment boundary.</summary>
    /// <param name="normalised">The lowercased, forward-slash path.</param>
    /// <param name="segment">The candidate segment text.</param>
    /// <returns>True when the hit starts a path segment.</returns>
    private static bool IsSegmentBoundaryMatch(string normalised, string segment)
    {
        var index = 0;
        while ((index = normalised.IndexOf(segment, index, StringComparison.Ordinal)) >= 0)
        {
            var startsSegment = index == 0 || normalised[index - 1] == '/';
            var endIndex = index + segment.Length;
            var endsSegment = endIndex == normalised.Length ||
                              normalised[endIndex] is '/' or '-' or '_' or ' ' or '.';

            if (startsSegment && endsSegment)
            {
                return true;
            }

            index = endIndex;
        }

        return false;
    }
}
