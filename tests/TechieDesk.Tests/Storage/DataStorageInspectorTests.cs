using TechieDesk.Services.Storage;
using TechieDesk.Tests.Support;
using TechieDeskDb;
using Xunit;

namespace TechieDesk.Tests.Storage;

/// <summary>
/// REQ-UI-041 (BRD-133): the data/storage surface reports where TechieDesk keeps its state and how
/// much disk it occupies. These tests measure a sandbox directory whose exact byte count is known,
/// so a wrong total is a failure rather than a plausible-looking number.
/// </summary>
public sealed class DataStorageInspectorTests : IDisposable
{
    private readonly string sandbox;

    /// <summary>Creates an empty sandbox data directory for one test.</summary>
    public DataStorageInspectorTests()
    {
        sandbox = Path.Combine(Path.GetTempPath(), "techiedesk-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
    }

    /// <summary>Removes the sandbox.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(sandbox))
            {
                Directory.Delete(sandbox, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    /// <summary>
    /// The headline total is the whole directory, not just the artefacts the inspector recognises.
    /// </summary>
    [Fact]
    public void DiskUsageCountsEveryByteInTheDirectory()
    {
        WriteFile(DataDirectory.AppDbFileName, 400);
        WriteFile(DataDirectory.VectorDbFileName, 1500);
        WriteFile(Path.Combine(DataDirectory.LogDirectoryName, "techiedesk-20260726.log"), 250);
        WriteFile("techiedesk.db-wal", 90);

        var snapshot = DataStorageInspector.Inspect(sandbox);

        Assert.Equal(400 + 1500 + 250 + 90, snapshot.TotalSizeBytes);
    }

    /// <summary>
    /// Every row of the table adds up to the headline figure — a table that under-reports the total
    /// is a report the user cannot act on.
    /// </summary>
    [Fact]
    public void DiskUsageSumsEveryArtefact()
    {
        WriteFile(DataDirectory.AppDbFileName, 400);
        WriteFile(DataDirectory.RagStoreFileName, 120);
        WriteFile(Path.Combine("models", "bge-m3", "model.onnx"), 5000);
        WriteFile(Path.Combine("uploads", "contract.pdf"), 700);
        WriteFile("something-nobody-declared.tmp", 33);

        var snapshot = DataStorageInspector.Inspect(sandbox);

        Assert.Equal(snapshot.TotalSizeBytes, snapshot.Artefacts.Sum(artefact => artefact.SizeBytes));
    }

    /// <summary>Directory artefacts are measured at every depth, not just their top level.</summary>
    [Fact]
    public void DirectoryArtefactsAreMeasuredRecursively()
    {
        WriteFile(Path.Combine("models", "bge-m3", "onnx", "model.onnx"), 2048);
        WriteFile(Path.Combine("models", "readme.txt"), 52);

        var snapshot = DataStorageInspector.Inspect(sandbox);
        var model = Single(snapshot, "StorageArtefactModelName");

        Assert.Equal(2048 + 52, model.SizeBytes);
    }

    /// <summary>Anything the inspector does not recognise is carried, not silently discarded.</summary>
    [Fact]
    public void UnrecognisedFilesAreCarriedInTheOtherRow()
    {
        WriteFile(DataDirectory.AppDbFileName, 100);
        WriteFile("techiedesk.db-shm", 64);
        WriteFile(Path.Combine("scratch", "half-a-download.part"), 11);

        var snapshot = DataStorageInspector.Inspect(sandbox);
        var other = Single(snapshot, DataStorageInspector.OtherArtefactNameKey);

        Assert.Equal(64 + 11, other.SizeBytes);
    }

    /// <summary>
    /// A fresh install has no uploads and no downloaded model. Those rows are reported as empty
    /// rather than dropped, so the table answers "where did my disk go" the same way every run.
    /// </summary>
    [Fact]
    public void MissingArtefactsAreReportedAsEmptyRatherThanDropped()
    {
        WriteFile(DataDirectory.AppDbFileName, 10);

        var snapshot = DataStorageInspector.Inspect(sandbox);
        var uploads = Single(snapshot, "StorageArtefactUploadsName");

        Assert.False(uploads.Exists);
        Assert.Equal(0, uploads.SizeBytes);
        Assert.Null(uploads.LastWrittenUtc);
        Assert.Equal(1, snapshot.PresentArtefactCount);
    }

    /// <summary>An empty data directory reports zero, not a failure and not a missing directory.</summary>
    [Fact]
    public void EmptyDirectoryReportsZeroTotal()
    {
        var snapshot = DataStorageInspector.Inspect(sandbox);

        Assert.True(snapshot.DirectoryExists);
        Assert.Equal(0, snapshot.TotalSizeBytes);
        Assert.Equal(0, snapshot.PresentArtefactCount);
    }

    /// <summary>A data directory that has never been created is reported, not thrown over.</summary>
    [Fact]
    public void AbsentDirectoryIsReportedRatherThanThrowing()
    {
        var missing = Path.Combine(sandbox, "never-created");

        var snapshot = DataStorageInspector.Inspect(missing);

        Assert.False(snapshot.DirectoryExists);
        Assert.Equal(0, snapshot.TotalSizeBytes);
        Assert.Equal(Path.GetFullPath(missing), snapshot.DirectoryPath);
    }

    /// <summary>The reported path is the directory that was measured, absolute and unaltered.</summary>
    [Fact]
    public void ReportsTheDirectoryItMeasured()
    {
        var snapshot = DataStorageInspector.Inspect(sandbox);

        Assert.Equal(Path.GetFullPath(sandbox), snapshot.DirectoryPath);
        Assert.All(
            snapshot.Artefacts,
            artefact => Assert.StartsWith(snapshot.DirectoryPath, artefact.FullPath, StringComparison.Ordinal));
    }

    /// <summary>
    /// The artefact list names the files the app actually writes, taken from DataDirectory — the
    /// single authority for them (REQ-FN-034/037) — so the table cannot drift from reality.
    /// </summary>
    [Fact]
    public void KnownArtefactsNameTheFilesTheAppWrites()
    {
        var paths = DataStorageInspector.KnownArtefacts.Select(artefact => artefact.RelativePath).ToArray();

        Assert.Contains(DataDirectory.AppDbFileName, paths);
        Assert.Contains(DataDirectory.VectorDbFileName, paths);
        Assert.Contains(DataDirectory.RagStoreFileName, paths);
        Assert.Contains(DataDirectory.ConfigFileName, paths);
        Assert.Contains(DataDirectory.KeyRingDirectoryName, paths);
        Assert.Contains(DataDirectory.LogDirectoryName, paths);
    }

    /// <summary>The last-written time of a directory artefact tracks its newest file.</summary>
    [Fact]
    public void LastWrittenTracksTheNewestFileInADirectory()
    {
        var older = WriteFile(Path.Combine(DataDirectory.LogDirectoryName, "old.log"), 10);
        var newer = WriteFile(Path.Combine(DataDirectory.LogDirectoryName, "new.log"), 10);
        File.SetLastWriteTimeUtc(older, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 7, 26, 9, 30, 0, DateTimeKind.Utc));

        var snapshot = DataStorageInspector.Inspect(sandbox);
        var logs = Single(snapshot, "StorageArtefactLogsName");

        Assert.Equal(new DateTimeOffset(2026, 7, 26, 9, 30, 0, TimeSpan.Zero), logs.LastWrittenUtc);
    }

    /// <summary>Sizes render in the units the settings surface displays.</summary>
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.00 GB")]
    public void FormatsSizesForDisplay(long bytes, string expected)
        => Assert.Equal(expected, DataStorageInspector.FormatSize(bytes));

    /// <summary>
    /// An unknown volume size renders the bar empty, never full. A divide that quietly produced
    /// 100% would tell the user their disk was full.
    /// </summary>
    [Fact]
    public void VolumeShareIsZeroWhenTheVolumeSizeIsUnknown()
    {
        var snapshot = new DataStorageSnapshot(sandbox, true, [], 1024, 0, 0);

        Assert.Equal(0, snapshot.VolumeUsedPercent);
    }

    /// <summary>The volume share is a percentage of the volume, clamped to a renderable range.</summary>
    [Fact]
    public void VolumeShareIsAPercentageOfTheVolume()
    {
        var snapshot = new DataStorageSnapshot(sandbox, true, [], 250, 750, 1000);

        Assert.Equal(25d, snapshot.VolumeUsedPercent);
    }

    /// <summary>Writes a file of an exact size, creating its parent directories.</summary>
    /// <param name="relativePath">Path relative to the sandbox.</param>
    /// <param name="bytes">Exact number of bytes to write.</param>
    /// <returns>The absolute path written.</returns>
    private string WriteFile(string relativePath, int bytes)
    {
        var fullPath = Path.Combine(sandbox, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, new byte[bytes]);
        return fullPath;
    }

    /// <summary>Gets the single artefact with a name key, failing the test when it is absent.</summary>
    /// <param name="snapshot">The snapshot to search.</param>
    /// <param name="nameKey">The artefact's resource key.</param>
    /// <returns>The matching artefact.</returns>
    private static DataStorageArtefact Single(DataStorageSnapshot snapshot, string nameKey)
        => Assert.Single(snapshot.Artefacts, artefact => artefact.NameKey == nameKey);

    /// <summary>
    /// REQ-UI-051: every row the table draws resolves through the resources, in both languages.
    /// </summary>
    /// <param name="culture">The culture to render in.</param>
    /// <remarks>
    /// This is the requirement's own defect, asserted directly. The nine artefact names and nine
    /// descriptions were built here as English literals and rendered verbatim at
    /// <c>/settings/data</c> — confirmed on a live Hindi install on 2026-08-01 — because a service
    /// sits outside the razor tree and so outside both localization counters. Resolving the whole
    /// table through a real localizer is the only check that would have caught it.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("hi")]
    public void EveryArtefactRowResolvesThroughTheResources(string culture)
    {
        using var resources = new ResourceHarness(culture);

        var snapshot = DataStorageInspector.Inspect(sandbox);
        Assert.Equal(DataStorageInspector.KnownArtefacts.Count + 1, snapshot.Artefacts.Count);

        foreach (var artefact in snapshot.Artefacts)
        {
            foreach (var key in new[] { artefact.NameKey, artefact.DescriptionKey })
            {
                // A key, not a sentence: keys have no spaces, and a resolved value that still
                // equals its key is a lookup that did not land.
                Assert.DoesNotContain(' ', key);
                Assert.NotEqual(key, resources.Require(key));
            }
        }
    }

    /// <summary>
    /// REQ-UI-051: the artefact PATHS stay culture-invariant. They name real files on disk, and a
    /// translated one would report a path that does not exist.
    /// </summary>
    [Fact]
    public void ArtefactPathsAreTheSameInEveryCulture()
    {
        string[] english;
        using (new ResourceHarness("en"))
        {
            english = DataStorageInspector.Inspect(sandbox)
                .Artefacts.Select(artefact => artefact.RelativePath).ToArray();
        }

        using (new ResourceHarness("hi"))
        {
            var hindi = DataStorageInspector.Inspect(sandbox)
                .Artefacts.Select(artefact => artefact.RelativePath).ToArray();

            Assert.Equal(english, hindi);
        }
    }
}
