using System.IO.Compression;
using System.Text;

using TechieDesk.Services.Backup;

using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Backup;

/// <summary>
/// The five restore-safety clauses of REQ-FN-047 (BRD-145), one region each.
/// </summary>
/// <remarks>
/// Each clause maps to a failure someone has actually shipped, so each is tested for the failure
/// rather than for the happy path: a mismatched model must be REFUSED, a hostile path must NOT be
/// written, a conflict must NOT be silently resolved, and a damaged archive must leave the install
/// byte-for-byte as it was.
/// </remarks>
public sealed class RestoreSafetyTests : IDisposable
{
    private readonly BackupTestHost source = new();
    private readonly string archivePath =
        Path.Combine(Path.GetTempPath(), $"techiedesk-safety-{Guid.NewGuid():N}.tdbak");

    // -- (a) embedding-model identity ---------------------------------------------------------

    /// <summary>An archive embedded with a different model is refused.</summary>
    /// <remarks>
    /// The nastiest failure in the feature: the foreign vectors are the same width, so nothing would
    /// throw and retrieval would simply get quietly worse. The refusal has to happen on the model
    /// NAME, which is the only thing that separates two 1024-wide models.
    /// </remarks>
    [Fact]
    public void AnArchiveFromADifferentEmbeddingModelIsRefused()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        using var target = new BackupTestHost("text-embedding-3-small", 1024);
        var preflight = target.Service.Preflight(archivePath);

        Assert.False(preflight.CanRestore);
        Assert.Equal(RestoreBlockReason.EmbeddingModelMismatch, preflight.BlockReason);
        Assert.True(preflight.CanReEmbed);
        Assert.Equal(BackupService.BlockEmbeddingMismatchKey, preflight.BlockDetailKey);

        // The MODEL NAMES are arguments, not translated text: they name real models and must read
        // the same in every language (REQ-UI-055).
        using var resources = new ResourceHarness("hi");
        var rendered = resources.Require(preflight.BlockDetailKey!, [.. preflight.BlockDetailArguments]);
        Assert.Contains("bge-m3", rendered, StringComparison.Ordinal);
        Assert.Contains("text-embedding-3-small", rendered, StringComparison.Ordinal);
    }

    /// <summary>Matching dimensions alone are not enough to accept an archive.</summary>
    /// <remarks>
    /// Stated as its own test because "the dimensions line up, so the vectors are compatible" is the
    /// exact reasoning that produces the silent corruption. Both installs here are 1024-wide.
    /// </remarks>
    [Fact]
    public void MatchingDimensionsAloneDoNotMakeAnArchiveAcceptable()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        using var target = new BackupTestHost("nomic-embed-text", 1024);

        Assert.Equal(
            1024, target.Service.ReadEmbeddingIdentity().Dimensions);
        Assert.Equal(
            RestoreBlockReason.EmbeddingModelMismatch,
            target.Service.Preflight(archivePath).BlockReason);
    }

    /// <summary>A model mismatch blocks the restore outright unless re-embed is chosen.</summary>
    [Fact]
    public void AMismatchedRestoreThrowsUnlessReEmbedIsChosen()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        using var target = new BackupTestHost("text-embedding-3-small", 1024);

        Assert.Throws<InvalidOperationException>(() =>
            target.Service.Restore(archivePath, new RestoreChoices()));

        Assert.Empty(BackupTestHost.WorkspaceNames(target.RagStorePath));
    }

    /// <summary>Choosing re-embed imports the content with no foreign vectors at all.</summary>
    /// <remarks>
    /// The chunk text must arrive so it can be re-embedded here, and the vector must arrive as null
    /// rather than as the archive's. Visibly un-embedded is a state the app can fix; invisibly
    /// mis-embedded is not.
    /// </remarks>
    [Fact]
    public void ReEmbedImportsTheContentWithoutTheForeignVectors()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        using var target = new BackupTestHost("text-embedding-3-small", 1024);
        var outcome = target.Service.Restore(
            archivePath, new RestoreChoices { ReEmbedOnModelMismatch = true });

        Assert.Equal(1, outcome.WorkspacesImported);
        Assert.Equal(1, outcome.ChunksImported);
        Assert.Equal(1, outcome.VectorsDiscarded);
        Assert.Equal(["alpha chunk text"], BackupTestHost.ChunkTexts(target.VectorDbPath));
        Assert.Null(BackupTestHost.VectorFor(target.VectorDbPath, "alpha chunk text"));
    }

    /// <summary>Model names differing only by case are treated as the same model.</summary>
    [Fact]
    public void ModelNamesDifferingOnlyByCaseAreTheSameModel()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        using var target = new BackupTestHost("BGE-M3", 1024);

        Assert.True(target.Service.Preflight(archivePath).CanRestore);
    }

    // -- (b) zip-slip -------------------------------------------------------------------------

    /// <summary>A hostile archive claiming a traversing path writes nothing outside the staging root.</summary>
    /// <remarks>
    /// The end-to-end counterpart of <see cref="BackupPathSafetyTests"/>: a real ZIP is built with a
    /// <c>../</c> entry, fed to the real restore path, and the file it tried to plant is asserted
    /// absent afterwards. Asserting only that an exception was thrown would not prove the write did
    /// not happen first.
    /// </remarks>
    [Fact]
    public void AHostileArchiveCannotWriteOutsideTheDataDirectory()
    {
        using var target = new BackupTestHost();
        var plantedPath = Path.Combine(Path.GetTempPath(), $"techiedesk-pwned-{Guid.NewGuid():N}.txt");

        BuildHostileArchive(archivePath, "../../../../" + Path.GetFileName(plantedPath));

        var preflight = target.Service.Preflight(archivePath);
        Assert.False(preflight.CanRestore);

        Assert.ThrowsAny<Exception>(() => target.Service.Restore(archivePath, new RestoreChoices()));

        Assert.False(File.Exists(plantedPath));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), Path.GetFileName(plantedPath))));
    }

    /// <summary>An archive carrying an entry this format does not define is refused.</summary>
    [Fact]
    public void AnArchiveCarryingAnUndeclaredEntryIsRefused()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        using (var file = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Update))
        {
            var smuggled = archive.CreateEntry("payload.sh");
            using var stream = smuggled.Open();
            stream.Write(Encoding.UTF8.GetBytes("#!/bin/sh\nrm -rf ~\n"));
        }

        using var target = new BackupTestHost();
        var preflight = target.Service.Preflight(archivePath);

        Assert.False(preflight.CanRestore);
        Assert.Equal(RestoreBlockReason.UnsafeEntry, preflight.BlockReason);
    }

    // -- (c) conflict choice ------------------------------------------------------------------

    /// <summary>An existing workspace is named in the pre-flight before anything is decided.</summary>
    [Fact]
    public void PreflightNamesTheConflictsBeforeAnythingChanges()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        using var target = new BackupTestHost();
        target.CreateStores();
        target.SeedWorkspace("ws-alpha", "Alpha as it is here", "local chunk text");

        var preflight = target.Service.Preflight(archivePath);

        Assert.True(preflight.CanRestore);
        Assert.True(preflight.HasConflicts);

        var conflict = Assert.Single(preflight.Workspaces);
        Assert.True(conflict.AlreadyExists);
        Assert.Equal("Alpha", conflict.Name);
        Assert.Equal("Alpha as it is here", conflict.ExistingName);
        Assert.Equal(1, conflict.DocumentCount);
        Assert.Equal(1, conflict.ThreadCount);

        // The report is read-only: nothing has changed yet.
        Assert.Equal(["Alpha as it is here"], BackupTestHost.WorkspaceNames(target.RagStorePath));
    }

    /// <summary>Skip leaves the existing workspace exactly as it was.</summary>
    [Fact]
    public void SkipLeavesTheExistingWorkspaceUntouched()
    {
        var outcome = RestoreOverExisting(WorkspaceConflictResolution.Skip, out var target);
        using (target)
        {
            Assert.Equal(0, outcome.WorkspacesImported);
            Assert.Equal(1, outcome.WorkspacesSkipped);
            Assert.Equal(["Alpha as it is here"], BackupTestHost.WorkspaceNames(target.RagStorePath));
            Assert.Equal(["local chunk text"], BackupTestHost.ChunkTexts(target.VectorDbPath));
        }
    }

    /// <summary>Duplicate keeps both copies, under distinct identifiers.</summary>
    [Fact]
    public void DuplicateKeepsBothCopies()
    {
        var outcome = RestoreOverExisting(WorkspaceConflictResolution.Duplicate, out var target);
        using (target)
        {
            Assert.Equal(1, outcome.WorkspacesImported);
            Assert.Equal(0, outcome.WorkspacesSkipped);
            Assert.Equal(
                ["Alpha (restored)", "Alpha as it is here"],
                BackupTestHost.WorkspaceNames(target.RagStorePath));
            Assert.Equal(2, BackupTestHost.CountRows(target.RagStorePath, "TrWorkspace"));

            // Both conversations survive: the duplicate got fresh thread identifiers rather than
            // stealing the existing workspace's history.
            Assert.Equal(2, BackupTestHost.CountRows(target.RagStorePath, "TrThread"));
        }
    }

    /// <summary>Replace swaps the workspace's content for the archived one.</summary>
    [Fact]
    public void ReplaceSwapsTheWorkspaceContent()
    {
        var outcome = RestoreOverExisting(WorkspaceConflictResolution.Replace, out var target);
        using (target)
        {
            Assert.Equal(1, outcome.WorkspacesImported);
            Assert.Equal(["Alpha"], BackupTestHost.WorkspaceNames(target.RagStorePath));
            Assert.Equal(1, BackupTestHost.CountRows(target.RagStorePath, "TrWorkspace"));
            Assert.Equal(1, BackupTestHost.CountRows(target.RagStorePath, "TrThread"));
        }
    }

    /// <summary>Skip is what happens when the caller expresses no preference.</summary>
    /// <remarks>
    /// The default has to be the member that cannot lose data. A default of Replace would turn a
    /// mis-click into an unrecoverable overwrite of the user's own work.
    /// </remarks>
    [Fact]
    public void TheDefaultConflictChoiceIsSkip() =>
        Assert.Equal(WorkspaceConflictResolution.Skip, new RestoreChoices().Conflict);

    // -- (d) integrity before applying --------------------------------------------------------

    /// <summary>A tampered archive is refused, and the install is left byte-for-byte unchanged.</summary>
    [Fact]
    public void ATamperedArchiveIsRefusedAndChangesNothing()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        TamperWithChunks(archivePath);

        using var target = new BackupTestHost();
        target.CreateStores();
        target.SeedWorkspace("ws-local", "Local", "local chunk text");

        var before = File.ReadAllBytes(target.RagStorePath);
        var vectorsBefore = File.ReadAllBytes(target.VectorDbPath);

        var preflight = target.Service.Preflight(archivePath);
        Assert.False(preflight.CanRestore);
        Assert.Equal(RestoreBlockReason.IntegrityFailed, preflight.BlockReason);

        Assert.Throws<InvalidOperationException>(() =>
            target.Service.Restore(archivePath, new RestoreChoices()));

        Assert.Equal(before, File.ReadAllBytes(target.RagStorePath));
        Assert.Equal(vectorsBefore, File.ReadAllBytes(target.VectorDbPath));
    }

    /// <summary>A file that is not an archive at all is reported, not thrown from.</summary>
    [Fact]
    public void AFileThatIsNotAnArchiveIsReportedNotThrown()
    {
        File.WriteAllText(archivePath, "this is not a zip file");

        using var target = new BackupTestHost();
        var preflight = target.Service.Preflight(archivePath);

        Assert.False(preflight.CanRestore);
        Assert.Equal(RestoreBlockReason.NotAnArchive, preflight.BlockReason);
        Assert.NotNull(preflight.BlockDetailKey);
    }

    /// <summary>A missing file is reported, not thrown from.</summary>
    [Fact]
    public void AMissingFileIsReportedNotThrown()
    {
        using var target = new BackupTestHost();
        var preflight = target.Service.Preflight(
            Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.tdbak"));

        Assert.Equal(RestoreBlockReason.NotAnArchive, preflight.BlockReason);
    }

    /// <summary>An archive from a newer format version is refused with an actionable message.</summary>
    [Fact]
    public void AnArchiveFromANewerFormatIsRefused()
    {
        source.CreateStores();
        source.Service.Export(archivePath, BackupScope.Instance);
        RewriteManifestFormatVersion(archivePath, BackupArchive.FormatVersion + 1);

        using var target = new BackupTestHost();
        var preflight = target.Service.Preflight(archivePath);

        Assert.Equal(RestoreBlockReason.UnsupportedFormatVersion, preflight.BlockReason);
        Assert.Equal(BackupService.BlockNewerFormatKey, preflight.BlockDetailKey);

        using var resources = new ResourceHarness("en");
        Assert.Contains(
            "Update TechieDesk",
            resources.Require(preflight.BlockDetailKey!, [.. preflight.BlockDetailArguments]),
            StringComparison.Ordinal);
    }

    /// <summary>No staging directory survives a completed restore.</summary>
    [Fact]
    public void StagingIsCleanedUpAfterARestore()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        using var target = new BackupTestHost();
        target.Service.Restore(archivePath, new RestoreChoices());

        var staging = Path.Combine(target.Directory, "restore-staging");
        Assert.True(!Directory.Exists(staging) || Directory.GetDirectories(staging).Length == 0);
    }

    // -- helpers ------------------------------------------------------------------------------

    /// <summary>Exports one workspace and restores it over an install that already has it.</summary>
    /// <param name="conflict">The conflict choice to apply.</param>
    /// <param name="target">The install restored into; the caller disposes it.</param>
    /// <returns>What the restore did.</returns>
    private RestoreOutcome RestoreOverExisting(
        WorkspaceConflictResolution conflict, out BackupTestHost target)
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        target = new BackupTestHost();
        target.CreateStores();
        target.SeedWorkspace("ws-alpha", "Alpha as it is here", "local chunk text");

        return target.Service.Restore(archivePath, new RestoreChoices { Conflict = conflict });
    }

    /// <summary>Builds a ZIP whose only entry claims a traversing path.</summary>
    /// <param name="path">Archive to create.</param>
    /// <param name="entryName">The hostile entry name.</param>
    private static void BuildHostileArchive(string path, string entryName)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);

        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes("pwned"));
    }

    /// <summary>Corrupts an archive's chunk stream without touching its manifest.</summary>
    /// <param name="path">Archive to damage.</param>
    private static void TamperWithChunks(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var archive = new ZipArchive(file, ZipArchiveMode.Update);

        var entry = archive.GetEntry(BackupArchive.ChunksEntryName);
        Assert.NotNull(entry);

        using var stream = entry.Open();
        stream.SetLength(0);
        stream.Write(Encoding.UTF8.GetBytes(
            """{"id":"forged","documentId":"forged","text":"forged","createdAt":"2026-01-01"}"""));
    }

    /// <summary>Rewrites the manifest's declared format version in place.</summary>
    /// <param name="path">Archive to edit.</param>
    /// <param name="version">Version to claim.</param>
    private static void RewriteManifestFormatVersion(string path, int version)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var archive = new ZipArchive(file, ZipArchiveMode.Update);

        var entry = archive.GetEntry(BackupArchive.ManifestEntryName);
        Assert.NotNull(entry);

        string json;
        using (var read = new StreamReader(entry.Open(), Encoding.UTF8))
        {
            json = read.ReadToEnd();
        }

        json = json.Replace(
            $"\"archiveFormatVersion\": {BackupArchive.FormatVersion}",
            $"\"archiveFormatVersion\": {version}",
            StringComparison.Ordinal);

        using var write = entry.Open();
        write.SetLength(0);
        write.Write(Encoding.UTF8.GetBytes(json));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        source.Dispose();
        try
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
        catch (IOException)
        {
            // A leftover temp archive must never fail a run.
        }
    }
}
