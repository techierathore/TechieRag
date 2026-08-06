using System.IO.Compression;
using System.Text.Json;

using TechieDesk.Services.Backup;

using Xunit;

namespace TechieDesk.Tests.Backup;

/// <summary>
/// Export and restore of a <c>.tdbak</c> archive (REQ-FN-046, BRD-144).
/// </summary>
/// <remarks>
/// The acceptance criterion is that an archive restores on a DIFFERENT install, so every round trip
/// here exports from one scratch data directory and restores into a second, empty one — never back
/// into the source. Restoring into the install that produced the archive would pass even if the
/// archive carried nothing at all.
/// </remarks>
public sealed class BackupRoundTripTests : IDisposable
{
    private readonly BackupTestHost source = new();
    private readonly BackupTestHost target = new();
    private readonly string archivePath =
        Path.Combine(Path.GetTempPath(), $"techiedesk-roundtrip-{Guid.NewGuid():N}.tdbak");

    /// <summary>Everything in an instance export comes back on a fresh install.</summary>
    [Fact]
    public void AnInstanceExportRestoresOnToAFreshInstall()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.SeedWorkspace("ws-beta", "Beta", "beta chunk text");

        var outcome = source.Service.Export(archivePath, BackupScope.Instance);

        Assert.Equal(2, outcome.Manifest.Counts.Workspaces);
        Assert.Equal(2, outcome.Manifest.Counts.Documents);
        Assert.Equal(2, outcome.Manifest.Counts.Chunks);
        Assert.Equal(2, outcome.Manifest.Counts.ChunksWithVector);
        Assert.True(outcome.SizeBytes > 0);

        var preflight = target.Service.Preflight(archivePath);
        Assert.True(preflight.CanRestore);
        Assert.False(preflight.HasConflicts);

        var restored = target.Service.Restore(archivePath, new RestoreChoices());

        Assert.Equal(2, restored.WorkspacesImported);
        Assert.Equal(2, restored.DocumentsImported);
        Assert.Equal(2, restored.ChunksImported);
        Assert.Equal(0, restored.VectorsDiscarded);
        Assert.Equal(["Alpha", "Beta"], BackupTestHost.WorkspaceNames(target.RagStorePath));
        Assert.Equal(
            ["alpha chunk text", "beta chunk text"], BackupTestHost.ChunkTexts(target.VectorDbPath));
    }

    /// <summary>The embedding vectors survive the round trip byte for byte.</summary>
    /// <remarks>
    /// The point of the whole feature is that a colleague does not have to re-ingest and re-embed.
    /// Restoring documents and chunk text while quietly dropping the vectors would look like a
    /// success and deliver none of the value, so the bytes are compared rather than the row count.
    /// </remarks>
    [Fact]
    public void TheEmbeddingVectorsSurviveTheRoundTrip()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");

        source.Service.Export(archivePath, BackupScope.Instance);
        target.Service.Restore(archivePath, new RestoreChoices());

        var expected = BackupTestHost.MakeVector("alpha chunk text");
        var actual = BackupTestHost.VectorFor(target.VectorDbPath, "alpha chunk text");

        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
    }

    /// <summary>Threads and messages come back with the workspace they belong to.</summary>
    [Fact]
    public void ThreadsAndMessagesComeBackWithTheirWorkspace()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");

        source.Service.Export(archivePath, BackupScope.Instance);
        var restored = target.Service.Restore(archivePath, new RestoreChoices());

        Assert.Equal(1, restored.ThreadsImported);
        Assert.Equal(1, restored.MessagesImported);
        Assert.Equal(1, BackupTestHost.CountRows(target.RagStorePath, "TrThread"));
        Assert.Equal(1, BackupTestHost.CountRows(target.RagStorePath, "TrMessage"));
    }

    /// <summary>A workspace-scoped export carries that workspace only.</summary>
    /// <remarks>
    /// Per-workspace granularity is a requirement, not a nicety — instance-only export is what made
    /// the benchmark's version unusable, so an export of one workspace must leave the other one out
    /// of the archive entirely rather than merely out of the restore.
    /// </remarks>
    [Fact]
    public void AWorkspaceScopedExportCarriesOnlyThatWorkspace()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.SeedWorkspace("ws-beta", "Beta", "beta chunk text");

        var outcome = source.Service.Export(archivePath, BackupScope.Workspace, ["ws-beta"]);

        Assert.Equal(BackupScope.Workspace, outcome.Manifest.Scope);
        Assert.Equal(1, outcome.Manifest.Counts.Workspaces);
        Assert.Equal(1, outcome.Manifest.Counts.Chunks);
        Assert.Equal(["Beta"], outcome.Manifest.WorkspaceNames);

        target.Service.Restore(archivePath, new RestoreChoices());

        Assert.Equal(["Beta"], BackupTestHost.WorkspaceNames(target.RagStorePath));
        Assert.Equal(["beta chunk text"], BackupTestHost.ChunkTexts(target.VectorDbPath));
    }

    /// <summary>The manifest records the versions and the embedding identity.</summary>
    /// <remarks>
    /// Asserted on the bytes in the archive rather than on the returned object, because it is the
    /// written manifest a future build will have to read.
    /// </remarks>
    [Fact]
    public void TheManifestRecordsTheVersionsAndTheEmbeddingIdentity()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry(BackupArchive.ManifestEntryName);
        Assert.NotNull(entry);

        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        Assert.Equal(BackupArchive.FormatVersion, root.GetProperty("archiveFormatVersion").GetInt32());
        Assert.Equal("1.2.3-test", root.GetProperty("appVersion").GetString());
        Assert.Equal("Instance", root.GetProperty("scope").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("createdAtUtc").GetString()));

        var embedding = root.GetProperty("embedding");
        Assert.Equal("bge-m3", embedding.GetProperty("model").GetString());
        Assert.Equal(1024, embedding.GetProperty("dimensions").GetInt32());

        // Every content stream must be covered by an integrity record, or nothing can be verified.
        var entries = root.GetProperty("entries").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToList();
        foreach (var required in BackupArchive.ContentEntryNames)
        {
            Assert.Contains(required, entries);
        }
    }

    /// <summary>The archive holds only the entries this format defines.</summary>
    [Fact]
    public void TheArchiveHoldsOnlyTheDefinedEntries()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");
        source.Service.Export(archivePath, BackupScope.Instance);

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            Assert.True(
                BackupArchive.IsKnownEntryName(entry.FullName),
                $"The archive carried an undeclared entry: {entry.FullName}");
        }
    }

    /// <summary>An install with nothing in it still produces a valid, restorable archive.</summary>
    [Fact]
    public void AnEmptyInstallStillProducesAValidArchive()
    {
        var outcome = source.Service.Export(archivePath, BackupScope.Instance);

        Assert.Equal(0, outcome.Manifest.Counts.Workspaces);

        var preflight = target.Service.Preflight(archivePath);
        Assert.True(preflight.CanRestore);
        Assert.Empty(preflight.Workspaces);
    }

    /// <summary>A failed export leaves no half-written archive behind.</summary>
    /// <remarks>
    /// An archive is expected to land in a shared folder, where a truncated file would be picked up
    /// and restored by a colleague. The sidecar-then-move sequence is what prevents that, so the
    /// absence of both the target and the <c>.partial</c> is asserted.
    /// </remarks>
    [Fact]
    public void ACancelledExportLeavesNoArchiveBehind()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            source.Service.Export(archivePath, BackupScope.Instance, null, cancellation.Token));

        Assert.False(File.Exists(archivePath));
        Assert.False(File.Exists(archivePath + ".partial"));
    }

    /// <summary>The suggested file name always carries the archive extension.</summary>
    [Fact]
    public void TheSuggestedFileNameCarriesTheArchiveExtension()
    {
        Assert.EndsWith(
            BackupArchive.FileExtension, BackupService.SuggestFileName(BackupScope.Instance, null));

        var named = BackupService.SuggestFileName(BackupScope.Workspace, "Q3 Planning / Notes");
        Assert.EndsWith(BackupArchive.FileExtension, named);
        Assert.DoesNotContain('/', named);
    }

    /// <summary>Listing the workspaces reports what each one holds.</summary>
    [Fact]
    public void ListingWorkspacesReportsWhatEachOneHolds()
    {
        source.CreateStores();
        source.SeedWorkspace("ws-alpha", "Alpha", "alpha chunk text");

        var listed = source.Service.ListWorkspaces();

        var only = Assert.Single(listed);
        Assert.Equal("Alpha", only.Name);
        Assert.Equal(1, only.DocumentCount);
        Assert.Equal(1, only.ThreadCount);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        source.Dispose();
        target.Dispose();
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
