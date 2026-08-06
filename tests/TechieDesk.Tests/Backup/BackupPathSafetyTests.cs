using TechieDesk.Services.Backup;

using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Backup;

/// <summary>
/// Zip-slip and sync-folder rules for REQ-FN-047 (BRD-145b), tested as pure functions.
/// </summary>
/// <remarks>
/// Kept separate from the round-trip tests because these two rules are the ones most likely to be
/// weakened by a well-meaning edit — "the archive is ours, why validate it?" — and they need to fail
/// loudly and instantly when that happens, without a database or an archive in the way.
/// </remarks>
public sealed class BackupPathSafetyTests
{
    private static readonly string Root =
        Path.Combine(Path.GetTempPath(), "techiedesk-zipslip-root");

    /// <summary>A traversing or absolute entry name is refused, however it is spelled.</summary>
    /// <param name="entryName">The hostile name an archive might claim.</param>
    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("../../evil.txt")]
    [InlineData("../../../../../../etc/passwd")]
    [InlineData("subdir/../../evil.txt")]
    [InlineData("manifest.json/../../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("..\\..\\Windows\\System32\\evil.dll")]
    [InlineData("chunks.jsonl\\..\\..\\evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("/tmp/evil.txt")]
    [InlineData("\\\\server\\share\\evil.txt")]
    [InlineData("C:\\Windows\\evil.dll")]
    [InlineData("C:evil.dll")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("   ")]
    public void TraversingEntryNamesAreRefused(string entryName)
    {
        Assert.False(BackupArchive.TryResolveSafePath(Root, entryName, out var resolved));
        Assert.Null(resolved);
    }

    /// <summary>A null entry name is refused rather than throwing.</summary>
    [Fact]
    public void ANullEntryNameIsRefused()
    {
        Assert.False(BackupArchive.TryResolveSafePath(Root, null, out var resolved));
        Assert.Null(resolved);
    }

    /// <summary>An ordinary entry name resolves to a path inside the root.</summary>
    /// <param name="entryName">A benign name.</param>
    [Theory]
    [InlineData("manifest.json")]
    [InlineData("chunks.jsonl")]
    [InlineData("nested/file.jsonl")]
    public void OrdinaryEntryNamesResolveInsideTheRoot(string entryName)
    {
        Assert.True(BackupArchive.TryResolveSafePath(Root, entryName, out var resolved));
        Assert.NotNull(resolved);
        Assert.StartsWith(Root + Path.DirectorySeparatorChar, resolved);
    }

    /// <summary>A sibling directory sharing the root's prefix is not inside it.</summary>
    /// <remarks>
    /// The reason the containment test compares against the root PLUS a separator. Without that a
    /// root of <c>/tmp/root</c> would happily accept a path resolving into <c>/tmp/root-evil</c>,
    /// which is a different directory that merely starts with the same characters.
    /// </remarks>
    [Fact]
    public void ASiblingDirectorySharingThePrefixIsRefused()
    {
        Assert.False(BackupArchive.TryResolveSafePath(Root, "../techiedesk-zipslip-rootevil/x", out _));
    }

    /// <summary>Only the entries this format defines are recognised.</summary>
    [Fact]
    public void OnlyTheDefinedEntryNamesAreKnown()
    {
        Assert.True(BackupArchive.IsKnownEntryName("manifest.json"));
        Assert.True(BackupArchive.IsKnownEntryName("chunks.jsonl"));
        Assert.False(BackupArchive.IsKnownEntryName("evil.sh"));
        Assert.False(BackupArchive.IsKnownEntryName("../manifest.json"));
        Assert.False(BackupArchive.IsKnownEntryName(null));
    }

    /// <summary>A data directory inside a consumer sync folder is detected (ADR-013).</summary>
    /// <param name="path">A plausible data-directory path.</param>
    /// <param name="expected">The product name the user should be warned about.</param>
    [Theory]
    [InlineData("/Users/sam/OneDrive/TechieDesk", "OneDrive")]
    [InlineData("/Users/sam/Dropbox/apps/TechieDesk", "Dropbox")]
    [InlineData("/Users/sam/Google Drive/TechieDesk", "Google Drive")]
    [InlineData("/Users/sam/Library/Mobile Documents/com~apple~CloudDocs/TechieDesk", "iCloud Drive")]
    [InlineData("/Users/sam/Library/CloudStorage/OneDrive-Personal/TechieDesk", "OneDrive")]
    [InlineData("C:\\Users\\sam\\OneDrive - Contoso\\TechieDesk", "OneDrive")]
    public void ADataDirectoryInsideASyncFolderIsDetected(string path, string expected)
    {
        var match = SyncFolderDetector.Detect(path);
        Assert.NotNull(match);
        Assert.Equal(expected, match.Name);
    }

    /// <summary>An ordinary data directory raises no sync warning.</summary>
    /// <param name="path">A path that is not synced.</param>
    [Theory]
    [InlineData("/Users/sam/Library/Application Support/TechieDesk")]
    [InlineData("C:\\Users\\sam\\AppData\\Local\\TechieDesk")]
    [InlineData("/home/sam/.local/share/TechieDesk")]
    [InlineData("/Users/megan/Library/Application Support/TechieDesk")]
    [InlineData("/Users/sam/Megabytes/TechieDesk")]
    public void AnOrdinaryDataDirectoryRaisesNoSyncWarning(string path) =>
        Assert.Null(SyncFolderDetector.Detect(path));

    /// <summary>The warning names the product and the risk in plain words.</summary>
    [Fact]
    public void TheSyncWarningNamesTheProductAndTheRisk()
    {
        var match = SyncFolderDetector.Detect("/Users/sam/Dropbox/TechieDesk");
        Assert.NotNull(match);

        using var resources = new ResourceHarness("en");
        var warning = resources.Require(
            SyncFolderDetector.DataDirectoryRiskKey,
            SyncFolderDetector.ProductName(match, resources.Localize));

        Assert.Contains("Dropbox", warning, StringComparison.Ordinal);
        Assert.Contains("corrupt", warning, StringComparison.Ordinal);
        Assert.Contains("export", warning, StringComparison.Ordinal);
    }
}
