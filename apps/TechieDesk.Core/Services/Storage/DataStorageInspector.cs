using TechieDeskDb;

namespace TechieDesk.Services.Storage;

/// <summary>
/// Measures what TechieDesk keeps on disk so the data/storage settings surface can report it
/// (REQ-UI-041, BRD-133).
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a pure function over a directory path rather than a DI service: it reads
/// only the file system, holds no state, and takes the directory as an argument, so a test can
/// point it at a sandbox and assert exact byte counts. The path itself comes from
/// <see cref="DataDirectory"/>, which is the single authority for it (REQ-FN-034/037).
/// </para>
/// <para>
/// The reported total is the recursive size of the WHOLE directory, not the sum of the artefacts
/// this type knows about. Anything unrecognised — SQLite <c>-wal</c>/<c>-shm</c> side files, a
/// half-finished download, a file a future requirement adds — is carried in the
/// <see cref="OtherArtefactNameKey"/> row so the artefact rows always add up to the total. A table
/// whose rows sum to less than the headline figure is exactly the report a user cannot act on.
/// </para>
/// <para>
/// <b>REQ-UI-051 / BRD-91.</b> Every name and description below is a RESOURCE KEY, never English.
/// This table was the confirmed defect: nine artefact names and nine descriptions rendered in
/// English on a Hindi install at <c>/settings/data</c>, because a service sits outside the razor
/// tree and therefore outside both localization counters. Returning keys makes "a service handed
/// English to a screen" impossible here rather than merely discouraged — the page has nothing to
/// render but a key, so forgetting to localize fails visibly instead of silently.
/// </para>
/// </remarks>
public static class DataStorageInspector
{
    /// <summary>Resource key for the row carrying everything not matched by a known artefact.</summary>
    public const string OtherArtefactNameKey = "StorageArtefactOtherName";

    /// <summary>Resource key for that row's description.</summary>
    public const string OtherArtefactDescriptionKey = "StorageArtefactOtherDescription";

    /// <summary>Sub-directory holding original uploaded source documents.</summary>
    public const string UploadsDirectoryName = "uploads";

    /// <summary>Sub-directory holding downloaded embedding-model weights.</summary>
    public const string ModelsDirectoryName = "models";

    /// <summary>
    /// Gets the artefacts the data directory is expected to hold, in the order they are displayed.
    /// </summary>
    /// <remarks>
    /// Every entry names a real path owned by a shipped requirement; the file and directory names
    /// come from <see cref="DataDirectory"/> so this list cannot drift from what the app writes.
    /// The first two members of each row are resource keys (REQ-UI-051); only the third — the path
    /// — is text, and it is invariant because it names a real file on disk.
    /// </remarks>
    public static IReadOnlyList<DataStorageArtefactDefinition> KnownArtefacts { get; } =
    [
        new("StorageArtefactModelName", "StorageArtefactModelDescription", ModelsDirectoryName),
        new("StorageArtefactVectorStoreName", "StorageArtefactVectorStoreDescription", DataDirectory.VectorDbFileName),
        new("StorageArtefactAppDatabaseName", "StorageArtefactAppDatabaseDescription", DataDirectory.AppDbFileName),
        new("StorageArtefactRagStoreName", "StorageArtefactRagStoreDescription", DataDirectory.RagStoreFileName),
        new("StorageArtefactUploadsName", "StorageArtefactUploadsDescription", UploadsDirectoryName),
        new("StorageArtefactProviderConfigName", "StorageArtefactProviderConfigDescription", DataDirectory.ConfigFileName),
        new("StorageArtefactKeyRingName", "StorageArtefactKeyRingDescription", DataDirectory.KeyRingDirectoryName),
        new("StorageArtefactLogsName", "StorageArtefactLogsDescription", DataDirectory.LogDirectoryName)
    ];

    /// <summary>
    /// Measures the bytes occupied by a file or a directory tree.
    /// </summary>
    /// <param name="path">Absolute path to a file or directory. It need not exist.</param>
    /// <returns>The bytes occupied, or zero when the path does not exist.</returns>
    /// <remarks>
    /// Files that vanish or become unreadable mid-walk are skipped rather than aborting the whole
    /// measurement: a log file rolling over while the page renders must not blank the report.
    /// </remarks>
    public static long MeasureSize(string path)
    {
        if (File.Exists(path))
        {
            return SafeFileLength(path);
        }

        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in EnumerateFilesSafely(path))
        {
            total += SafeFileLength(file);
        }

        return total;
    }

    /// <summary>
    /// Gets the most recent write time across a file or a directory tree.
    /// </summary>
    /// <param name="path">Absolute path to a file or directory. It need not exist.</param>
    /// <returns>The latest UTC write time, or null when the path does not exist.</returns>
    public static DateTimeOffset? LastWritten(string path)
    {
        if (File.Exists(path))
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        // The newest FILE, not the directory's own timestamp. A directory's mtime moves whenever a
        // child is added or removed, so a folder emptied last week would report today and read as
        // "TechieDesk wrote this a moment ago". Only when the directory holds no file at all does
        // its own timestamp become the best available answer.
        var latest = DateTime.MinValue;
        var sawFile = false;
        foreach (var file in EnumerateFilesSafely(path))
        {
            try
            {
                var written = File.GetLastWriteTimeUtc(file);
                sawFile = true;
                if (written > latest)
                {
                    latest = written;
                }
            }
            catch (IOException)
            {
                // A file that disappeared between enumeration and stat contributes nothing.
            }
        }

        return new DateTimeOffset(sawFile ? latest : Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero);
    }

    /// <summary>
    /// Reads the data directory and reports every artefact, the total, and the host volume.
    /// </summary>
    /// <param name="dataDirectory">Absolute data directory, from <see cref="DataDirectory.Resolve"/>.</param>
    /// <returns>A snapshot whose artefact sizes always sum to <c>TotalSizeBytes</c>.</returns>
    public static DataStorageSnapshot Inspect(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var root = Path.GetFullPath(dataDirectory);
        var exists = Directory.Exists(root);
        var total = MeasureSize(root);

        var artefacts = new List<DataStorageArtefact>();
        long accountedFor = 0;

        foreach (var definition in KnownArtefacts)
        {
            var fullPath = Path.Combine(root, definition.RelativePath);
            var present = File.Exists(fullPath) || Directory.Exists(fullPath);
            var size = present ? MeasureSize(fullPath) : 0;
            accountedFor += size;

            artefacts.Add(new DataStorageArtefact(
                definition.NameKey,
                definition.DescriptionKey,
                definition.RelativePath,
                fullPath,
                size,
                present ? LastWritten(fullPath) : null,
                present));
        }

        // The remainder keeps the table honest: rows always add up to the headline total.
        var remainder = Math.Max(0, total - accountedFor);
        artefacts.Add(new DataStorageArtefact(
            OtherArtefactNameKey,
            OtherArtefactDescriptionKey,
            ".",
            root,
            remainder,
            exists ? LastWritten(root) : null,
            remainder > 0));

        var (freeBytes, volumeBytes) = MeasureVolume(root);
        return new DataStorageSnapshot(root, exists, artefacts, total, freeBytes, volumeBytes);
    }

    /// <summary>
    /// Renders a byte count the way the data/storage surface displays it.
    /// </summary>
    /// <param name="bytes">A non-negative byte count.</param>
    /// <returns>A short human-readable size such as <c>1.7 GB</c> or <c>184 MB</c>.</returns>
    public static string FormatSize(long bytes)
    {
        const long Kilobyte = 1024;
        const long Megabyte = Kilobyte * 1024;
        const long Gigabyte = Megabyte * 1024;

        return bytes switch
        {
            >= Gigabyte => $"{bytes / (double)Gigabyte:F2} GB",
            >= Megabyte => $"{bytes / (double)Megabyte:F1} MB",
            >= Kilobyte => $"{bytes / (double)Kilobyte:F0} KB",
            _ => $"{bytes} B"
        };
    }

    /// <summary>
    /// Reports the free and total size of the volume holding a path.
    /// </summary>
    /// <param name="path">An absolute path on the volume of interest.</param>
    /// <returns>Free and total bytes; both zero when the volume cannot be read.</returns>
    /// <remarks>
    /// The deepest mount point that prefixes the path wins, which is what makes a data directory
    /// relocated onto an external volume report that volume rather than the boot disk.
    /// </remarks>
    private static (long FreeBytes, long TotalBytes) MeasureVolume(string path)
    {
        try
        {
            DriveInfo? best = null;
            var bestLength = -1;

            foreach (var drive in DriveInfo.GetDrives())
            {
                string root;
                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    root = drive.RootDirectory.FullName;
                }
                catch (IOException)
                {
                    continue;
                }

                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && root.Length > bestLength)
                {
                    best = drive;
                    bestLength = root.Length;
                }
            }

            return best is null ? (0, 0) : (best.AvailableFreeSpace, best.TotalSize);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Volume statistics are informational; the artefact table is the load-bearing part.
            return (0, 0);
        }
    }

    /// <summary>Enumerates every file under a directory, skipping branches that cannot be read.</summary>
    /// <param name="directory">Absolute directory path that exists.</param>
    /// <returns>Absolute file paths at any depth.</returns>
    private static IEnumerable<string> EnumerateFilesSafely(string directory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        return Directory.EnumerateFiles(directory, "*", options);
    }

    /// <summary>Reads a file's length, treating an unreadable file as contributing nothing.</summary>
    /// <param name="path">Absolute file path.</param>
    /// <returns>The file length in bytes, or zero when it could not be read.</returns>
    private static long SafeFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
