using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Dapper;
using Microsoft.Data.Sqlite;

using TechieDeskDb;
using TechieRag;

namespace TechieDesk.Services.Backup;

/// <summary>
/// Packs and unpacks the self-contained <c>.tdbak</c> archive (REQ-FN-046/047, BRD-144/145, ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this service is for.</b> It is how a team hands work to a colleague without a server: the
/// owner exports an instance or a single workspace to an inert archive file, drops it in a shared
/// OneDrive/Drive/Dropbox folder, and the colleague restores it. Everything else in this type follows
/// from that one sentence, including the things it refuses to do.
/// </para>
/// <para>
/// <b>Credential exclusion is by construction, not by filtering (BRD-144).</b> The packer opens three
/// SQLite files and reads a fixed allow-list of tables through explicit column lists:
/// <c>TrWorkspace</c>, <c>TrWorkspaceDocument</c>, <c>TrThread</c> and <c>TrMessage</c> from the RAG
/// store; <c>Documents</c> and <c>Chunks</c> from the vector store; and <c>SchemaVersions</c>
/// (script names only) from the app database. It never enumerates the data directory, so the
/// Data Protection <c>keys/</c> ring, <c>connector-secrets.json</c> and the <c>enc:v1:</c> API keys
/// inside <c>techierag-config.json</c> are unreachable. It never reads <c>LicenseCache</c>,
/// <c>Connector</c> or <c>InstanceSetting</c>, so AppManager tokens and connector credential
/// references are unreachable. There is no filter to weaken and no wildcard to widen — adding a
/// secret to an archive would require adding a new query to this file.
/// </para>
/// <para>
/// <b>Everything streams (ADR-013).</b> Rows are enumerated unbuffered from SQLite and written
/// line-by-line into a <see cref="ZipArchive"/> over a <see cref="FileStream"/>; entry hashes are
/// computed as the bytes pass. Peak memory is one row, not one instance. This is the failure the
/// benchmark actually hit — it withdrew its own export partly because large instances "crash during
/// zipping" — so buffering an entry, or calling <c>ToArray</c> on archive content, would reintroduce
/// the defect this feature was specified to avoid.
/// </para>
/// <para>
/// <b>The methods are synchronous on purpose.</b> They are continuous disk and SQLite work with no
/// await points worth yielding at, and Dapper's unbuffered enumeration — the thing that keeps memory
/// bounded — has no async counterpart. Callers move them off the UI thread with
/// <c>Task.Run</c>, which is the same shape <c>DataStorageInspector</c> already uses from
/// <c>DataStorage.razor</c>. Cancellation is honoured inside every row loop.
/// </para>
/// </remarks>
public sealed class BackupService
{
    // ---------------------------------------------------------------------------------------------
    // Restore-refusal resource keys (REQ-UI-055 / BRD-91)
    //
    // Every refusal this service can issue is a KEY, never a sentence. BackupRestore.razor resolves
    // them through IStringLocalizer<AppStrings>; the service has no localizer and never gains one.
    // The ARGUMENTS are archive data — entry names, byte counts, format versions, embedding-model
    // identities — and stay culture-invariant, because a restore reads them back off the .tdbak.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Resource key: the chosen path does not exist.</summary>
    public const string BlockFileMissingKey = "BackupBlockFileMissing";

    /// <summary>Resource key: the file could not be opened as a zip archive.</summary>
    public const string BlockNotReadableKey = "BackupBlockNotReadable";

    /// <summary>Resource key: the archive carries no manifest entry.</summary>
    public const string BlockNoManifestKey = "BackupBlockNoManifest";

    /// <summary>Resource key: the manifest could not be deserialized.</summary>
    public const string BlockManifestUnreadableKey = "BackupBlockManifestUnreadable";

    /// <summary>Resource key: the manifest deserialized to nothing.</summary>
    public const string BlockManifestEmptyKey = "BackupBlockManifestEmpty";

    /// <summary>Resource key: the archive format is newer than this build understands.</summary>
    public const string BlockNewerFormatKey = "BackupBlockNewerFormat";

    /// <summary>Resource key: the archive declares an entry this build will not extract.</summary>
    public const string BlockUnsafeEntryKey = "BackupBlockUnsafeEntryDetail";

    /// <summary>Resource key: an entry the manifest lists is absent from the archive.</summary>
    public const string BlockEntryMissingKey = "BackupBlockEntryMissing";

    /// <summary>Resource key: an entry's byte length disagrees with the manifest.</summary>
    public const string BlockEntryLengthMismatchKey = "BackupBlockEntryLengthMismatch";

    /// <summary>Resource key: an entry's checksum disagrees with the manifest.</summary>
    public const string BlockEntryChecksumMismatchKey = "BackupBlockEntryChecksumMismatch";

    /// <summary>Resource key: the manifest does not cover every content entry.</summary>
    public const string BlockManifestIncompleteKey = "BackupBlockManifestIncomplete";

    /// <summary>Resource key: the archive's vectors come from a different embedding model.</summary>
    public const string BlockEmbeddingMismatchKey = "BackupBlockEmbeddingMismatchDetail";

    /// <summary>Sub-directory of the data directory used to stage an unpacked archive.</summary>
    /// <remarks>
    /// A sub-directory rather than the data directory itself, because the scheduled
    /// <c>DatabaseMaintenanceJobHandler</c> globs <c>*.db</c> at the top level and would otherwise
    /// try to VACUUM a half-restored file. Staged content is JSONL regardless, but relying on that
    /// would be relying on a coincidence.
    /// </remarks>
    private const string StagingDirectoryName = "restore-staging";

    /// <summary>Largest number of identifiers bound into a single <c>IN</c> clause.</summary>
    /// <remarks>
    /// SQLite caps bound parameters per statement. A workspace with more documents than this is
    /// entirely plausible, and the failure would be an exception at export time on exactly the large
    /// instances this feature exists to serve, so identifier lists are always queried in batches.
    /// </remarks>
    private const int ParameterBatchSize = 400;

    private static readonly JsonSerializerOptions RowOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IConfiguration configuration;
    private readonly ILogger<BackupService> logger;
    private readonly string appVersion;

    /// <summary>Creates a backup service.</summary>
    /// <param name="configuration">Application configuration, read for the data-directory override.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="appVersion">Version of the running application, stamped into the manifest.</param>
    public BackupService(IConfiguration configuration, ILogger<BackupService> logger, string appVersion)
    {
        this.configuration = configuration;
        this.logger = logger;
        this.appVersion = appVersion;
    }

    /// <summary>Gets the resolved data directory every read and write is scoped to.</summary>
    public string DataDirectoryPath => DataDirectory.Resolve(configuration[DataDirectory.ConfigKey]);

    /// <summary>
    /// Detects whether the LIVE data directory sits inside a consumer cloud-sync folder
    /// (REQ-FN-047, ADR-013).
    /// </summary>
    /// <returns>The detected sync product, or null when the location looks safe.</returns>
    /// <remarks>
    /// This is about the data directory only. An archive written INTO a synced folder is the intended
    /// workflow and is never warned about — the prohibition is on syncing live database files, not on
    /// sharing inert ones.
    /// </remarks>
    public SyncFolderMatch? DetectDataDirectorySyncRisk() => SyncFolderDetector.Detect(DataDirectoryPath);

    /// <summary>Suggests a file name for a new archive.</summary>
    /// <param name="scope">The granularity being exported.</param>
    /// <param name="workspaceName">The workspace name, when exporting a single workspace.</param>
    /// <returns>A file name ending in <see cref="BackupArchive.FileExtension"/>.</returns>
    public static string SuggestFileName(BackupScope scope, string? workspaceName)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
        if (scope == BackupScope.Workspace && !string.IsNullOrWhiteSpace(workspaceName))
        {
            var safe = new string(workspaceName
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray()).Trim('-');
            if (safe.Length > 0)
            {
                return $"techiedesk-{safe}-{stamp}{BackupArchive.FileExtension}";
            }
        }

        return $"techiedesk-backup-{stamp}{BackupArchive.FileExtension}";
    }

    /// <summary>Lists the workspaces available to export, with their content counts.</summary>
    /// <returns>Every workspace in this install, ordered by name.</returns>
    public IReadOnlyList<BackupWorkspaceSummary> ListWorkspaces()
    {
        var ragStorePath = Path.Combine(DataDirectoryPath, DataDirectory.RagStoreFileName);
        if (!File.Exists(ragStorePath))
        {
            return [];
        }

        using var ragStore = OpenReadOnly(ragStorePath);

        // Counted through a long-typed row because SQLite's COUNT(*) is an INTEGER, which Dapper
        // will not bind to an int-typed record constructor.
        return ragStore.Query<WorkspaceSummaryRow>(
            """
            SELECT  w.WorkspaceId AS WorkspaceId,
                    w.Name AS Name,
                    (SELECT COUNT(*) FROM TrWorkspaceDocument d WHERE d.WorkspaceId = w.WorkspaceId)
                        AS DocumentCount,
                    (SELECT COUNT(*) FROM TrThread t WHERE t.WorkspaceId = w.WorkspaceId)
                        AS ThreadCount
            FROM    TrWorkspace w
            ORDER BY w.Name
            """)
            .Select(row => new BackupWorkspaceSummary(
                row.WorkspaceId, row.Name, (int)row.DocumentCount, (int)row.ThreadCount))
            .ToList();
    }

    /// <summary>A workspace summary as SQLite types it, before narrowing the counts.</summary>
    /// <param name="WorkspaceId">Identifier.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="DocumentCount">Attached documents.</param>
    /// <param name="ThreadCount">Threads belonging to it.</param>
    private sealed record WorkspaceSummaryRow(
        string WorkspaceId, string Name, long DocumentCount, long ThreadCount);

    /// <summary>Reads the embedding model identity this install currently uses.</summary>
    /// <returns>The configured model name, dimension and provider.</returns>
    /// <remarks>
    /// Parsed straight out of <c>techierag-config.json</c> rather than through
    /// <c>TechieRagConfigService</c>, deliberately. That service decrypts every provider API key into
    /// the object it returns, and this type must never hold a cleartext credential — not because it
    /// would write one out, but so that the claim "no code path here can reach a secret" stays true
    /// by inspection rather than by audit.
    /// </remarks>
    public EmbeddingIdentity ReadEmbeddingIdentity() => ReadInstallProfile().Embedding;

    /// <summary>
    /// Writes an archive containing the selected scope (REQ-FN-046).
    /// </summary>
    /// <param name="destinationPath">Absolute path of the archive to create.</param>
    /// <param name="scope">Whole instance, or the named workspaces.</param>
    /// <param name="workspaceIds">Workspaces to export when <paramref name="scope"/> is Workspace.</param>
    /// <param name="cancellationToken">Cancels the export between rows.</param>
    /// <returns>Where the archive landed, how big it is, and what it says it holds.</returns>
    /// <exception cref="InvalidOperationException">The vectors do not live in this data directory.</exception>
    public BackupOutcome Export(
        string destinationPath,
        BackupScope scope,
        IReadOnlyList<string>? workspaceIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var dataDirectory = DataDirectoryPath;
        var profile = ReadInstallProfile();

        if (profile.VectorStore != VectorStoreType.SqliteVec)
        {
            // Refusing is the honest outcome. With an external vector store the embeddings are not in
            // this data directory at all, so the archive would restore documents and chunk text with
            // every vector missing — which looks like a successful backup and is not one.
            throw new InvalidOperationException(
                $"This install keeps its embeddings in an external {profile.VectorStore} vector " +
                "store, so they cannot be packed into a portable archive. Switch the vector store " +
                "to the embedded SQLite one before exporting.");
        }

        if (!destinationPath.EndsWith(BackupArchive.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            destinationPath += BackupArchive.FileExtension;
        }

        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        var ragStorePath = Path.Combine(dataDirectory, DataDirectory.RagStoreFileName);
        var vectorDbPath = Path.Combine(dataDirectory, DataDirectory.VectorDbFileName);

        using var ragStore = File.Exists(ragStorePath) ? OpenReadOnly(ragStorePath) : null;
        using var vectorDb = File.Exists(vectorDbPath) ? OpenReadOnly(vectorDbPath) : null;

        var selectedIds = ResolveWorkspaceIds(ragStore, scope, workspaceIds);

        // Written to a sidecar first and moved into place only on success, so a cancelled or failed
        // export can never leave a plausible-looking but truncated .tdbak in a shared folder where a
        // colleague would try to restore it.
        var partialPath = destinationPath + ".partial";
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        BackupManifest manifest;
        try
        {
            using (var file = new FileStream(
                       partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       bufferSize: 64 * 1024, useAsync: false))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                manifest = WriteContent(
                    archive, ragStore, vectorDb, scope, selectedIds, profile, cancellationToken);
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(partialPath, destinationPath);
        }
        catch
        {
            TryDeleteFile(partialPath);
            throw;
        }

        var size = new FileInfo(destinationPath).Length;
        logger.LogInformation(
            "Wrote backup archive {Path} ({Bytes} bytes, scope {Scope}, {Workspaces} workspaces, " +
            "{Chunks} chunks) — REQ-FN-046",
            destinationPath, size, scope, manifest.Counts.Workspaces, manifest.Counts.Chunks);

        return new BackupOutcome(destinationPath, size, manifest);
    }

    /// <summary>
    /// Reports exactly what restoring an archive would change, without changing anything
    /// (REQ-FN-047).
    /// </summary>
    /// <param name="archivePath">Absolute path of the archive to inspect.</param>
    /// <param name="cancellationToken">Cancels the inspection.</param>
    /// <returns>A pre-flight report, including any reason the restore must be refused.</returns>
    /// <remarks>
    /// Every refusal this feature can issue is decided here, before a byte is written: the file is a
    /// real archive, its format version is one this build understands, every entry name is known and
    /// non-traversing, every entry matches the manifest's hash and length, and the archived vectors
    /// come from this install's embedding model. That ordering is what makes "a partial restore
    /// leaves the install as it was" achievable — the install is not touched until nothing is left
    /// that could refuse it.
    /// </remarks>
    public RestorePreflight Preflight(string archivePath, CancellationToken cancellationToken = default)
    {
        var report = new RestorePreflight { ArchivePath = archivePath };

        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return report with
            {
                BlockReason = RestoreBlockReason.NotAnArchive,
                BlockDetailKey = BlockFileMissingKey
            };
        }

        ZipArchive archive;
        FileStream file;
        try
        {
            file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
                                              or UnauthorizedAccessException)
        {
            return report with
            {
                BlockReason = RestoreBlockReason.NotAnArchive,
                BlockDetailKey = BlockNotReadableKey,
                BlockDetailArguments = [exception.Message]
            };
        }

        using (archive)
        {
            var manifestEntry = archive.GetEntry(BackupArchive.ManifestEntryName);
            if (manifestEntry is null)
            {
                return report with
                {
                    BlockReason = RestoreBlockReason.NotAnArchive,
                    BlockDetailKey = BlockNoManifestKey
                };
            }

            BackupManifest? manifest;
            try
            {
                using var manifestStream = manifestEntry.Open();
                manifest = JsonSerializer.Deserialize<BackupManifest>(manifestStream, ManifestOptions);
            }
            catch (JsonException exception)
            {
                return report with
                {
                    BlockReason = RestoreBlockReason.NotAnArchive,
                    BlockDetailKey = BlockManifestUnreadableKey,
                    BlockDetailArguments = [exception.Message]
                };
            }

            if (manifest is null)
            {
                return report with
                {
                    BlockReason = RestoreBlockReason.NotAnArchive,
                    BlockDetailKey = BlockManifestEmptyKey
                };
            }

            report = report with { Manifest = manifest };

            if (manifest.ArchiveFormatVersion > BackupArchive.FormatVersion)
            {
                return report with
                {
                    BlockReason = RestoreBlockReason.UnsupportedFormatVersion,
                    BlockDetailKey = BlockNewerFormatKey,
                    BlockDetailArguments =
                    [
                        manifest.ArchiveFormatVersion.ToString(CultureInfo.InvariantCulture),
                        BackupArchive.FormatVersion.ToString(CultureInfo.InvariantCulture)
                    ]
                };
            }

            // Zip-slip and unknown-entry check, on EVERY entry the archive declares — including ones
            // the manifest does not mention, which is precisely where a hostile entry would hide.
            var stagingProbe = Path.Combine(DataDirectoryPath, StagingDirectoryName, "probe");
            foreach (var entry in archive.Entries)
            {
                if (!BackupArchive.IsKnownEntryName(entry.FullName) ||
                    !BackupArchive.TryResolveSafePath(stagingProbe, entry.FullName, out _))
                {
                    logger.LogWarning(
                        "Refused backup archive {Path}: unsafe or unknown entry {Entry} — REQ-FN-047",
                        archivePath, entry.FullName);

                    return report with
                    {
                        BlockReason = RestoreBlockReason.UnsafeEntry,
                        BlockDetailKey = BlockUnsafeEntryKey,
                        BlockDetailArguments = [entry.FullName]
                    };
                }
            }

            var integrityFailure = VerifyIntegrity(archive, manifest, cancellationToken);
            if (integrityFailure is not null)
            {
                return report with
                {
                    BlockReason = RestoreBlockReason.IntegrityFailed,
                    BlockDetailKey = integrityFailure.MessageKey,
                    BlockDetailArguments = integrityFailure.Arguments
                };
            }

            report = report with
            {
                Workspaces = BuildWorkspacePlans(archive, cancellationToken)
            };

            var target = ReadInstallProfile().Embedding;
            report = report with { TargetEmbedding = target };

            if (!manifest.Embedding.Matches(target))
            {
                return report with
                {
                    BlockReason = RestoreBlockReason.EmbeddingModelMismatch,
                    BlockDetailKey = BlockEmbeddingMismatchKey,
                    BlockDetailArguments = [manifest.Embedding.Describe(), target.Describe()]
                };
            }
        }

        return report;
    }

    /// <summary>
    /// Applies an archive to this install (REQ-FN-046/047).
    /// </summary>
    /// <param name="archivePath">Absolute path of the archive to restore.</param>
    /// <param name="options">The conflict choice, and whether to re-embed on model mismatch.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    /// <returns>What was actually written.</returns>
    /// <exception cref="InvalidOperationException">The pre-flight refused the archive.</exception>
    /// <remarks>
    /// Staged, verified, then applied. The archive is unpacked into a private staging directory
    /// under the data directory — every entry path re-checked against
    /// <see cref="BackupArchive.TryResolveSafePath"/> at the moment of writing, not merely at
    /// pre-flight — and the databases are only touched once staging succeeded. Both writes run inside
    /// transactions that are committed together at the very end, so a failure anywhere leaves the
    /// install exactly as it was.
    /// </remarks>
    public RestoreOutcome Restore(
        string archivePath, RestoreChoices options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var preflight = Preflight(archivePath, cancellationToken);
        var mismatchAccepted = preflight.CanReEmbed && options.ReEmbedOnModelMismatch;

        if (!preflight.CanRestore && !mismatchAccepted)
        {
            // Culture-invariant on purpose (REQ-UI-055). This is an exception message: it goes to
            // the log and to support, and text that changes with the reader's language cannot be
            // searched for. The USER sees the localized BlockDetailKey on the pre-flight screen,
            // which is reached before this can ever throw.
            throw new InvalidOperationException(
                $"Restore refused: {preflight.BlockReason} [{preflight.BlockDetailKey}]");
        }

        var stagingRoot = Path.Combine(
            DataDirectoryPath, StagingDirectoryName, Guid.NewGuid().ToString("N"));

        try
        {
            ExtractToStaging(archivePath, stagingRoot, cancellationToken);
            var outcome = Apply(stagingRoot, preflight, options, mismatchAccepted, cancellationToken);

            logger.LogInformation(
                "Restored backup archive {Path}: {Imported} workspaces imported, {Skipped} skipped, " +
                "{Documents} documents, {Chunks} chunks ({Discarded} vectors discarded) — REQ-FN-046",
                archivePath, outcome.WorkspacesImported, outcome.WorkspacesSkipped,
                outcome.DocumentsImported, outcome.ChunksImported, outcome.VectorsDiscarded);

            return outcome;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Export internals
    // ---------------------------------------------------------------------------------------------

    /// <summary>Streams every content entry into the archive and returns the manifest written last.</summary>
    /// <param name="archive">The archive being created.</param>
    /// <param name="ragStore">Open read-only RAG store, or null when the install has none.</param>
    /// <param name="vectorDb">Open read-only vector store, or null when the install has none.</param>
    /// <param name="scope">Granularity being exported.</param>
    /// <param name="selectedIds">Workspace identifiers in scope.</param>
    /// <param name="profile">The install's embedding and vector-store identity.</param>
    /// <param name="cancellationToken">Cancels between rows.</param>
    /// <returns>The manifest, already written into the archive.</returns>
    private BackupManifest WriteContent(
        ZipArchive archive,
        SqliteConnection? ragStore,
        SqliteConnection? vectorDb,
        BackupScope scope,
        IReadOnlyList<string> selectedIds,
        InstallProfile profile,
        CancellationToken cancellationToken)
    {
        var integrity = new List<BackupEntryIntegrity>();
        var workspaceNames = new List<string>();

        var workspaces = WriteJsonLines(
            archive, BackupArchive.WorkspacesEntryName,
            ReadWorkspaces(ragStore, scope, selectedIds),
            row => workspaceNames.Add(row.Name), integrity, cancellationToken);

        var workspaceDocumentIds = new List<string>();
        var workspaceDocuments = WriteJsonLines(
            archive, BackupArchive.WorkspaceDocumentsEntryName,
            ReadWorkspaceDocuments(ragStore, scope, selectedIds),
            row => workspaceDocumentIds.Add(row.DocumentId), integrity, cancellationToken);

        var threadIds = new List<string>();
        var threads = WriteJsonLines(
            archive, BackupArchive.ThreadsEntryName,
            ReadThreads(ragStore, scope, selectedIds),
            row => threadIds.Add(row.ThreadId), integrity, cancellationToken);

        var messages = WriteJsonLines(
            archive, BackupArchive.MessagesEntryName,
            ReadMessages(ragStore, scope, threadIds),
            null, integrity, cancellationToken);

        var documentIds = scope == BackupScope.Instance
            ? null
            : workspaceDocumentIds.Distinct(StringComparer.Ordinal).ToList();

        var documents = WriteJsonLines(
            archive, BackupArchive.DocumentsEntryName,
            ReadDocuments(vectorDb, documentIds),
            null, integrity, cancellationToken);

        var vectorCount = 0;
        var chunks = WriteJsonLines(
            archive, BackupArchive.ChunksEntryName,
            ReadChunks(vectorDb, documentIds),
            row =>
            {
                if (row.Vector is { Length: > 0 })
                {
                    vectorCount++;
                }
            },
            integrity, cancellationToken);

        var manifest = new BackupManifest
        {
            ArchiveFormatVersion = BackupArchive.FormatVersion,
            AppVersion = appVersion,
            SchemaVersion = ReadSchemaVersion(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Scope = scope,
            WorkspaceIds = selectedIds,
            WorkspaceNames = workspaceNames,
            Embedding = profile.Embedding,
            Counts = new BackupCounts(
                workspaces, workspaceDocuments, threads, messages, documents, chunks, vectorCount),
            Entries = integrity
        };

        // Written last: the per-entry hashes only exist once each entry has streamed to disk, and a
        // ZIP central directory is order-independent so reading it first still costs nothing.
        var manifestEntry = archive.CreateEntry(
            BackupArchive.ManifestEntryName, CompressionLevel.Optimal);
        using var manifestStream = manifestEntry.Open();
        JsonSerializer.Serialize(manifestStream, manifest, ManifestOptions);

        return manifest;
    }

    /// <summary>Streams rows into one archive entry as newline-delimited JSON.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="archive">The archive being created.</param>
    /// <param name="entryName">Entry to create.</param>
    /// <param name="rows">Unbuffered row sequence.</param>
    /// <param name="observe">Optional per-row observer, used to collect counts and identifiers.</param>
    /// <param name="integrity">Collector the entry's hash and length are appended to.</param>
    /// <param name="cancellationToken">Cancels between rows.</param>
    /// <returns>The number of rows written.</returns>
    private static int WriteJsonLines<T>(
        ZipArchive archive,
        string entryName,
        IEnumerable<T> rows,
        Action<T>? observe,
        List<BackupEntryIntegrity> integrity,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        var count = 0;

        using var entryStream = entry.Open();
        using var hashing = new HashingWriteStream(entryStream);

        using (var writer = new StreamWriter(hashing, new UTF8Encoding(false), 64 * 1024, leaveOpen: true))
        {
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteLine(JsonSerializer.Serialize(row, RowOptions));
                observe?.Invoke(row);
                count++;
            }

            writer.Flush();
        }

        integrity.Add(new BackupEntryIntegrity(entryName, hashing.BytesWritten, hashing.FinishHex()));
        return count;
    }

    /// <summary>Resolves which workspace identifiers an export covers.</summary>
    /// <param name="ragStore">Open RAG store, or null.</param>
    /// <param name="scope">Requested granularity.</param>
    /// <param name="requested">Identifiers the caller asked for, for workspace scope.</param>
    /// <returns>The identifiers to export.</returns>
    private static IReadOnlyList<string> ResolveWorkspaceIds(
        SqliteConnection? ragStore, BackupScope scope, IReadOnlyList<string>? requested)
    {
        if (scope == BackupScope.Workspace)
        {
            var ids = (requested ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            if (ids.Count == 0)
            {
                throw new ArgumentException(
                    "Exporting a single workspace needs at least one workspace selected.",
                    nameof(requested));
            }

            return ids;
        }

        return ragStore is null
            ? []
            : ragStore.Query<string>("SELECT WorkspaceId FROM TrWorkspace ORDER BY WorkspaceId").ToList();
    }

    /// <summary>Reads the in-scope workspace rows, unbuffered.</summary>
    /// <param name="ragStore">Open RAG store, or null.</param>
    /// <param name="scope">Requested granularity.</param>
    /// <param name="ids">Workspace identifiers in scope.</param>
    /// <returns>A lazy row sequence.</returns>
    private static IEnumerable<BackupWorkspaceRow> ReadWorkspaces(
        SqliteConnection? ragStore, BackupScope scope, IReadOnlyList<string> ids)
    {
        if (ragStore is null)
        {
            yield break;
        }

        const string Columns =
            "WorkspaceId, Name, SystemPrompt, LlmModel, SimilarityThreshold, TopK, " +
            "RerankEnabled, ChatMode, CreatedAt, UpdatedAt";

        if (scope == BackupScope.Instance)
        {
            foreach (var row in ragStore.Query<BackupWorkspaceRow>(
                         $"SELECT {Columns} FROM TrWorkspace ORDER BY WorkspaceId", buffered: false))
            {
                yield return row;
            }

            yield break;
        }

        foreach (var batch in Batch(ids))
        {
            foreach (var row in ragStore.Query<BackupWorkspaceRow>(
                         $"SELECT {Columns} FROM TrWorkspace WHERE WorkspaceId IN @ids ORDER BY WorkspaceId",
                         new { ids = batch }, buffered: false))
            {
                yield return row;
            }
        }
    }

    /// <summary>Reads the in-scope workspace/document links, unbuffered.</summary>
    /// <param name="ragStore">Open RAG store, or null.</param>
    /// <param name="scope">Requested granularity.</param>
    /// <param name="ids">Workspace identifiers in scope.</param>
    /// <returns>A lazy row sequence.</returns>
    private static IEnumerable<BackupWorkspaceDocumentRow> ReadWorkspaceDocuments(
        SqliteConnection? ragStore, BackupScope scope, IReadOnlyList<string> ids)
    {
        if (ragStore is null)
        {
            yield break;
        }

        const string Columns = "WorkspaceId, DocumentId, ContentHash, IsPinned, AddedAt";

        if (scope == BackupScope.Instance)
        {
            foreach (var row in ragStore.Query<BackupWorkspaceDocumentRow>(
                         $"SELECT {Columns} FROM TrWorkspaceDocument ORDER BY WorkspaceId, DocumentId",
                         buffered: false))
            {
                yield return row;
            }

            yield break;
        }

        foreach (var batch in Batch(ids))
        {
            foreach (var row in ragStore.Query<BackupWorkspaceDocumentRow>(
                         $"SELECT {Columns} FROM TrWorkspaceDocument WHERE WorkspaceId IN @ids " +
                         "ORDER BY WorkspaceId, DocumentId",
                         new { ids = batch }, buffered: false))
            {
                yield return row;
            }
        }
    }

    /// <summary>Reads the in-scope threads, unbuffered.</summary>
    /// <param name="ragStore">Open RAG store, or null.</param>
    /// <param name="scope">Requested granularity.</param>
    /// <param name="ids">Workspace identifiers in scope.</param>
    /// <returns>A lazy row sequence.</returns>
    private static IEnumerable<BackupThreadRow> ReadThreads(
        SqliteConnection? ragStore, BackupScope scope, IReadOnlyList<string> ids)
    {
        if (ragStore is null)
        {
            yield break;
        }

        const string Columns = "ThreadId, UserId, WorkspaceId, Title, CreatedAt, UpdatedAt";

        if (scope == BackupScope.Instance)
        {
            foreach (var row in ragStore.Query<BackupThreadRow>(
                         $"SELECT {Columns} FROM TrThread ORDER BY ThreadId", buffered: false))
            {
                yield return row;
            }

            yield break;
        }

        foreach (var batch in Batch(ids))
        {
            foreach (var row in ragStore.Query<BackupThreadRow>(
                         $"SELECT {Columns} FROM TrThread WHERE WorkspaceId IN @ids ORDER BY ThreadId",
                         new { ids = batch }, buffered: false))
            {
                yield return row;
            }
        }
    }

    /// <summary>Reads the messages belonging to the in-scope threads, unbuffered.</summary>
    /// <param name="ragStore">Open RAG store, or null.</param>
    /// <param name="scope">Requested granularity.</param>
    /// <param name="threadIds">Thread identifiers already written, for workspace scope.</param>
    /// <returns>A lazy row sequence.</returns>
    private static IEnumerable<BackupMessageRow> ReadMessages(
        SqliteConnection? ragStore, BackupScope scope, IReadOnlyList<string> threadIds)
    {
        if (ragStore is null)
        {
            yield break;
        }

        const string Columns = "MessageId, ThreadId, Role, Content, SourcesJson, CreatedAt";

        if (scope == BackupScope.Instance)
        {
            foreach (var row in ragStore.Query<BackupMessageRow>(
                         $"SELECT {Columns} FROM TrMessage ORDER BY ThreadId, CreatedAt", buffered: false))
            {
                yield return row;
            }

            yield break;
        }

        foreach (var batch in Batch(threadIds))
        {
            foreach (var row in ragStore.Query<BackupMessageRow>(
                         $"SELECT {Columns} FROM TrMessage WHERE ThreadId IN @ids ORDER BY ThreadId, CreatedAt",
                         new { ids = batch }, buffered: false))
            {
                yield return row;
            }
        }
    }

    /// <summary>Reads the in-scope catalogue documents, unbuffered.</summary>
    /// <param name="vectorDb">Open vector store, or null.</param>
    /// <param name="documentIds">Document identifiers to restrict to, or null for all of them.</param>
    /// <returns>A lazy row sequence.</returns>
    private static IEnumerable<BackupDocumentRow> ReadDocuments(
        SqliteConnection? vectorDb, IReadOnlyList<string>? documentIds)
    {
        if (vectorDb is null)
        {
            yield break;
        }

        const string Columns = "Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata";

        if (documentIds is null)
        {
            foreach (var row in vectorDb.Query<BackupDocumentRow>(
                         $"SELECT {Columns} FROM Documents ORDER BY Id", buffered: false))
            {
                yield return row;
            }

            yield break;
        }

        foreach (var batch in Batch(documentIds))
        {
            foreach (var row in vectorDb.Query<BackupDocumentRow>(
                         $"SELECT {Columns} FROM Documents WHERE Id IN @ids ORDER BY Id",
                         new { ids = batch }, buffered: false))
            {
                yield return row;
            }
        }
    }

    /// <summary>Reads the in-scope chunks and their embedding vectors, unbuffered.</summary>
    /// <param name="vectorDb">Open vector store, or null.</param>
    /// <param name="documentIds">Document identifiers to restrict to, or null for all of them.</param>
    /// <returns>A lazy row sequence; peak memory is one chunk's vector.</returns>
    private static IEnumerable<BackupChunkRow> ReadChunks(
        SqliteConnection? vectorDb, IReadOnlyList<string>? documentIds)
    {
        if (vectorDb is null)
        {
            yield break;
        }

        const string Columns =
            "Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt";

        if (documentIds is null)
        {
            foreach (var row in vectorDb.Query<BackupChunkRow>(
                         $"SELECT {Columns} FROM Chunks ORDER BY DocumentId, ChunkIndex",
                         buffered: false))
            {
                yield return row;
            }

            yield break;
        }

        foreach (var batch in Batch(documentIds))
        {
            foreach (var row in vectorDb.Query<BackupChunkRow>(
                         $"SELECT {Columns} FROM Chunks WHERE DocumentId IN @ids " +
                         "ORDER BY DocumentId, ChunkIndex",
                         new { ids = batch }, buffered: false))
            {
                yield return row;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Restore internals
    // ---------------------------------------------------------------------------------------------

    /// <summary>Verifies every manifest entry's hash and length against the archive.</summary>
    /// <param name="archive">The open archive.</param>
    /// <param name="manifest">The manifest read from it.</param>
    /// <param name="cancellationToken">Cancels the verification.</param>
    /// <returns>A resource key plus arguments, or null when everything matched.</returns>
    /// <remarks>
    /// REQ-UI-055: the entry NAMES threaded through the arguments are archive data. They are read
    /// back out of the <c>.tdbak</c> by <see cref="BackupArchive.IsKnownEntryName"/> and must stay
    /// byte-identical in every culture; only the sentence around them is translated.
    /// </remarks>
    private static RestoreBlockDetail? VerifyIntegrity(
        ZipArchive archive, BackupManifest manifest, CancellationToken cancellationToken)
    {
        foreach (var expected in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = archive.GetEntry(expected.Name);
            if (entry is null)
            {
                return new RestoreBlockDetail(BlockEntryMissingKey, [expected.Name]);
            }

            using var stream = entry.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long length = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
                length += read;
            }

            if (length != expected.Length)
            {
                return new RestoreBlockDetail(
                    BlockEntryLengthMismatchKey,
                    [
                        expected.Name,
                        length.ToString(CultureInfo.InvariantCulture),
                        expected.Length.ToString(CultureInfo.InvariantCulture)
                    ]);
            }

            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.Equals(actual, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new RestoreBlockDetail(BlockEntryChecksumMismatchKey, [expected.Name]);
            }
        }

        // The manifest must account for every content entry; an archive that simply omits one from
        // its integrity list would otherwise slip an unverified stream past the check above.
        foreach (var required in BackupArchive.ContentEntryNames)
        {
            if (!manifest.Entries.Any(entry =>
                    string.Equals(entry.Name, required, StringComparison.Ordinal)))
            {
                return new RestoreBlockDetail(BlockManifestIncompleteKey, [required]);
            }
        }

        return null;
    }

    /// <summary>Builds the per-workspace pre-flight plan by reading the archive and this install.</summary>
    /// <param name="archive">The open archive.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>One plan entry per workspace in the archive.</returns>
    private IReadOnlyList<RestoreWorkspacePlan> BuildWorkspacePlans(
        ZipArchive archive, CancellationToken cancellationToken)
    {
        var workspaces = ReadEntryLines<BackupWorkspaceRow>(
            archive, BackupArchive.WorkspacesEntryName, cancellationToken).ToList();

        var documentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var link in ReadEntryLines<BackupWorkspaceDocumentRow>(
                     archive, BackupArchive.WorkspaceDocumentsEntryName, cancellationToken))
        {
            documentCounts[link.WorkspaceId] = documentCounts.GetValueOrDefault(link.WorkspaceId) + 1;
        }

        var threadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var thread in ReadEntryLines<BackupThreadRow>(
                     archive, BackupArchive.ThreadsEntryName, cancellationToken))
        {
            if (thread.WorkspaceId is { Length: > 0 })
            {
                threadCounts[thread.WorkspaceId] = threadCounts.GetValueOrDefault(thread.WorkspaceId) + 1;
            }
        }

        var existing = ReadExistingWorkspaceNames();

        return workspaces
            .Select(workspace => new RestoreWorkspacePlan(
                workspace.WorkspaceId,
                workspace.Name,
                existing.ContainsKey(workspace.WorkspaceId),
                existing.GetValueOrDefault(workspace.WorkspaceId),
                documentCounts.GetValueOrDefault(workspace.WorkspaceId),
                threadCounts.GetValueOrDefault(workspace.WorkspaceId)))
            .ToList();
    }

    /// <summary>Reads the workspace identifiers and names already present in this install.</summary>
    /// <returns>A map of workspace identifier to display name; empty when there is no store yet.</returns>
    private Dictionary<string, string> ReadExistingWorkspaceNames()
    {
        var ragStorePath = Path.Combine(DataDirectoryPath, DataDirectory.RagStoreFileName);
        if (!File.Exists(ragStorePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var ragStore = OpenReadOnly(ragStorePath);
        try
        {
            return ragStore
                .Query<(string WorkspaceId, string Name)>("SELECT WorkspaceId, Name FROM TrWorkspace")
                .ToDictionary(row => row.WorkspaceId, row => row.Name, StringComparer.Ordinal);
        }
        catch (SqliteException)
        {
            // A data directory whose RAG store exists but has never been initialised has no
            // TrWorkspace table yet. Nothing exists, so nothing can conflict.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Extracts every archive entry into a staging directory, re-checking each path.</summary>
    /// <param name="archivePath">The archive to unpack.</param>
    /// <param name="stagingRoot">Directory to unpack into; created here.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <remarks>
    /// The path check is repeated here even though pre-flight already made it. Validating at
    /// inspection time and writing from a different code path is exactly how a
    /// time-of-check/time-of-use hole appears, so the guard sits immediately before the only
    /// <see cref="FileStream"/> that writes archive-named content.
    /// </remarks>
    private void ExtractToStaging(
        string archivePath, string stagingRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingRoot);

        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!BackupArchive.IsKnownEntryName(entry.FullName) ||
                !BackupArchive.TryResolveSafePath(stagingRoot, entry.FullName, out var destination) ||
                destination is null)
            {
                throw new InvalidOperationException(
                    $"The archive contains an entry this build will not extract ('{entry.FullName}').");
            }

            using var source = entry.Open();
            using var target = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
            source.CopyTo(target);
        }
    }

    /// <summary>Applies staged content to the two content databases inside transactions.</summary>
    /// <param name="stagingRoot">Directory holding the verified, unpacked entries.</param>
    /// <param name="preflight">The report the user confirmed.</param>
    /// <param name="options">Conflict choice and re-embed decision.</param>
    /// <param name="discardVectors">True when archived vectors must be dropped for re-embedding.</param>
    /// <param name="cancellationToken">Cancels between rows.</param>
    /// <returns>What was written.</returns>
    private RestoreOutcome Apply(
        string stagingRoot,
        RestorePreflight preflight,
        RestoreChoices options,
        bool discardVectors,
        CancellationToken cancellationToken)
    {
        var dataDirectory = DataDirectory.ResolveAndCreate(configuration[DataDirectory.ConfigKey]);
        var ragStorePath = Path.Combine(dataDirectory, DataDirectory.RagStoreFileName);
        var vectorDbPath = Path.Combine(dataDirectory, DataDirectory.VectorDbFileName);

        using var ragStore = OpenReadWrite(ragStorePath);
        using var vectorDb = OpenReadWrite(vectorDbPath);

        foreach (var statement in BackupSchema.RagStoreStatements)
        {
            ragStore.Execute(statement);
        }

        foreach (var statement in BackupSchema.VectorStoreStatements)
        {
            vectorDb.Execute(statement);
        }

        using var ragTransaction = ragStore.BeginTransaction();
        using var vectorTransaction = vectorDb.BeginTransaction();

        var workspaceIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var workspaceNameOverride = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var plan in preflight.Workspaces)
        {
            if (!plan.AlreadyExists)
            {
                workspaceIdMap[plan.WorkspaceId] = plan.WorkspaceId;
                continue;
            }

            switch (options.Conflict)
            {
                case WorkspaceConflictResolution.Skip:
                    skipped.Add(plan.WorkspaceId);
                    break;

                case WorkspaceConflictResolution.Duplicate:
                    var newId = Guid.NewGuid().ToString();
                    workspaceIdMap[plan.WorkspaceId] = newId;
                    workspaceNameOverride[plan.WorkspaceId] = $"{plan.Name} (restored)";
                    break;

                case WorkspaceConflictResolution.Replace:
                    workspaceIdMap[plan.WorkspaceId] = plan.WorkspaceId;
                    DeleteWorkspaceContent(ragStore, ragTransaction, plan.WorkspaceId);
                    break;
            }
        }

        var workspacesImported = 0;
        foreach (var workspace in ReadStagedLines<BackupWorkspaceRow>(
                     stagingRoot, BackupArchive.WorkspacesEntryName, cancellationToken))
        {
            if (!workspaceIdMap.TryGetValue(workspace.WorkspaceId, out var targetId))
            {
                continue;
            }

            ragStore.Execute(
                """
                INSERT OR REPLACE INTO TrWorkspace
                    (WorkspaceId, Name, SystemPrompt, LlmModel, SimilarityThreshold, TopK,
                     RerankEnabled, ChatMode, CreatedAt, UpdatedAt)
                VALUES
                    (@WorkspaceId, @Name, @SystemPrompt, @LlmModel, @SimilarityThreshold, @TopK,
                     @RerankEnabled, @ChatMode, @CreatedAt, @UpdatedAt)
                """,
                new
                {
                    WorkspaceId = targetId,
                    Name = workspaceNameOverride.GetValueOrDefault(workspace.WorkspaceId, workspace.Name),
                    workspace.SystemPrompt,
                    workspace.LlmModel,
                    workspace.SimilarityThreshold,
                    workspace.TopK,
                    workspace.RerankEnabled,
                    workspace.ChatMode,
                    workspace.CreatedAt,
                    workspace.UpdatedAt
                },
                ragTransaction);

            workspacesImported++;
        }

        var importedDocumentIds = new HashSet<string>(StringComparer.Ordinal);
        var linkedDocumentIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var link in ReadStagedLines<BackupWorkspaceDocumentRow>(
                     stagingRoot, BackupArchive.WorkspaceDocumentsEntryName, cancellationToken))
        {
            linkedDocumentIds.Add(link.DocumentId);

            if (!workspaceIdMap.TryGetValue(link.WorkspaceId, out var targetId))
            {
                continue;
            }

            ragStore.Execute(
                """
                INSERT OR REPLACE INTO TrWorkspaceDocument
                    (WorkspaceId, DocumentId, ContentHash, IsPinned, AddedAt)
                VALUES
                    (@WorkspaceId, @DocumentId, @ContentHash, @IsPinned, @AddedAt)
                """,
                new
                {
                    WorkspaceId = targetId,
                    link.DocumentId,
                    link.ContentHash,
                    link.IsPinned,
                    link.AddedAt
                },
                ragTransaction);

            importedDocumentIds.Add(link.DocumentId);
        }

        var threadIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var threadsImported = 0;

        foreach (var thread in ReadStagedLines<BackupThreadRow>(
                     stagingRoot, BackupArchive.ThreadsEntryName, cancellationToken))
        {
            string targetWorkspaceId;
            if (thread.WorkspaceId is null or "")
            {
                // A thread with no workspace belongs to the instance rather than to any workspace, so
                // no conflict choice applies to it and it is carried across as-is.
                targetWorkspaceId = string.Empty;
            }
            else if (!workspaceIdMap.TryGetValue(thread.WorkspaceId, out var mapped))
            {
                continue;
            }
            else
            {
                targetWorkspaceId = mapped;
            }

            // A duplicated workspace needs new thread identifiers: the originals still belong to the
            // workspace already here, and reusing them would silently move that workspace's history.
            var targetThreadId = thread.WorkspaceId is { Length: > 0 } &&
                                 !string.Equals(targetWorkspaceId, thread.WorkspaceId, StringComparison.Ordinal)
                ? Guid.NewGuid().ToString()
                : thread.ThreadId;

            threadIdMap[thread.ThreadId] = targetThreadId;

            ragStore.Execute(
                """
                INSERT OR REPLACE INTO TrThread
                    (ThreadId, UserId, WorkspaceId, Title, CreatedAt, UpdatedAt)
                VALUES
                    (@ThreadId, @UserId, @WorkspaceId, @Title, @CreatedAt, @UpdatedAt)
                """,
                new
                {
                    ThreadId = targetThreadId,
                    thread.UserId,
                    WorkspaceId = targetWorkspaceId.Length == 0 ? null : targetWorkspaceId,
                    thread.Title,
                    thread.CreatedAt,
                    thread.UpdatedAt
                },
                ragTransaction);

            threadsImported++;
        }

        var messagesImported = 0;
        foreach (var message in ReadStagedLines<BackupMessageRow>(
                     stagingRoot, BackupArchive.MessagesEntryName, cancellationToken))
        {
            if (!threadIdMap.TryGetValue(message.ThreadId, out var targetThreadId))
            {
                continue;
            }

            ragStore.Execute(
                """
                INSERT OR REPLACE INTO TrMessage
                    (MessageId, ThreadId, Role, Content, SourcesJson, CreatedAt)
                VALUES
                    (@MessageId, @ThreadId, @Role, @Content, @SourcesJson, @CreatedAt)
                """,
                new
                {
                    MessageId = string.Equals(targetThreadId, message.ThreadId, StringComparison.Ordinal)
                        ? message.MessageId
                        : Guid.NewGuid().ToString(),
                    ThreadId = targetThreadId,
                    message.Role,
                    message.Content,
                    message.SourcesJson,
                    message.CreatedAt
                },
                ragTransaction);

            messagesImported++;
        }

        // Documents and chunks are a shared catalogue, so they are written with OR IGNORE: a restore
        // adds what is missing and never rewrites what is already here. Anything reachable from an
        // imported workspace comes in, plus documents the archive carries that no workspace links to
        // at all — those are instance-level library content that would otherwise be silently lost.
        var documentsImported = 0;
        foreach (var document in ReadStagedLines<BackupDocumentRow>(
                     stagingRoot, BackupArchive.DocumentsEntryName, cancellationToken))
        {
            if (!importedDocumentIds.Contains(document.Id) && linkedDocumentIds.Contains(document.Id))
            {
                continue;
            }

            vectorDb.Execute(
                """
                INSERT OR IGNORE INTO Documents
                    (Id, Name, SourcePath, ChunkCount, IngestedAt, Metadata)
                VALUES
                    (@Id, @Name, @SourcePath, @ChunkCount, @IngestedAt, @Metadata)
                """,
                document,
                vectorTransaction);

            importedDocumentIds.Add(document.Id);
            documentsImported++;
        }

        var chunksImported = 0;
        var vectorsDiscarded = 0;
        foreach (var chunk in ReadStagedLines<BackupChunkRow>(
                     stagingRoot, BackupArchive.ChunksEntryName, cancellationToken))
        {
            if (!importedDocumentIds.Contains(chunk.DocumentId))
            {
                continue;
            }

            var vector = discardVectors ? null : chunk.Vector;
            if (discardVectors && chunk.Vector is { Length: > 0 })
            {
                vectorsDiscarded++;
            }

            vectorDb.Execute(
                """
                INSERT OR IGNORE INTO Chunks
                    (Id, DocumentId, Text, Vector, PageNumber, ChunkIndex, Metadata, CreatedAt)
                VALUES
                    (@Id, @DocumentId, @Text, @Vector, @PageNumber, @ChunkIndex, @Metadata, @CreatedAt)
                """,
                new
                {
                    chunk.Id,
                    chunk.DocumentId,
                    chunk.Text,
                    Vector = vector,
                    chunk.PageNumber,
                    chunk.ChunkIndex,
                    chunk.Metadata,
                    chunk.CreatedAt
                },
                vectorTransaction);

            chunksImported++;
        }

        // Content before links. If the second commit failed we would rather be left with catalogue
        // documents no workspace points at — visible in the library, harmless — than with workspace
        // links pointing at documents that were never written.
        vectorTransaction.Commit();
        ragTransaction.Commit();

        return new RestoreOutcome(
            workspacesImported, skipped.Count, documentsImported, chunksImported,
            threadsImported, messagesImported, vectorsDiscarded);
    }

    /// <summary>Removes an existing workspace's rows ahead of a Replace restore.</summary>
    /// <param name="ragStore">The open RAG store connection.</param>
    /// <param name="transaction">The enclosing transaction.</param>
    /// <param name="workspaceId">The workspace to clear.</param>
    /// <remarks>
    /// Only the workspace, its document links and its conversations are removed. The catalogue
    /// documents themselves are left alone because they are shared: deleting them would silently
    /// strip content from other workspaces that also reference them.
    /// </remarks>
    private static void DeleteWorkspaceContent(
        SqliteConnection ragStore, System.Data.IDbTransaction transaction, string workspaceId)
    {
        ragStore.Execute(
            "DELETE FROM TrMessage WHERE ThreadId IN " +
            "(SELECT ThreadId FROM TrThread WHERE WorkspaceId = @workspaceId)",
            new { workspaceId }, transaction);

        ragStore.Execute(
            "DELETE FROM TrThread WHERE WorkspaceId = @workspaceId", new { workspaceId }, transaction);

        ragStore.Execute(
            "DELETE FROM TrWorkspaceDocument WHERE WorkspaceId = @workspaceId",
            new { workspaceId }, transaction);

        ragStore.Execute(
            "DELETE FROM TrWorkspace WHERE WorkspaceId = @workspaceId", new { workspaceId }, transaction);
    }

    // ---------------------------------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>Reads one JSONL entry out of an open archive, lazily.</summary>
    /// <typeparam name="T">Row type.</typeparam>
    /// <param name="archive">The open archive.</param>
    /// <param name="entryName">Entry to read.</param>
    /// <param name="cancellationToken">Cancels between lines.</param>
    /// <returns>A lazy row sequence; empty when the entry is absent.</returns>
    private static IEnumerable<T> ReadEntryLines<T>(
        ZipArchive archive, string entryName, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            yield break;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length == 0)
            {
                continue;
            }

            var row = JsonSerializer.Deserialize<T>(line, RowOptions);
            if (row is not null)
            {
                yield return row;
            }
        }
    }

    /// <summary>Reads one staged JSONL file, lazily.</summary>
    /// <typeparam name="T">Row type.</typeparam>
    /// <param name="stagingRoot">Directory the archive was unpacked into.</param>
    /// <param name="entryName">File to read.</param>
    /// <param name="cancellationToken">Cancels between lines.</param>
    /// <returns>A lazy row sequence; empty when the file is absent.</returns>
    private static IEnumerable<T> ReadStagedLines<T>(
        string stagingRoot, string entryName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(stagingRoot, entryName);
        if (!File.Exists(path))
        {
            yield break;
        }

        using var reader = new StreamReader(path, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length == 0)
            {
                continue;
            }

            var row = JsonSerializer.Deserialize<T>(line, RowOptions);
            if (row is not null)
            {
                yield return row;
            }
        }
    }

    /// <summary>Splits an identifier list into batches small enough to bind into one statement.</summary>
    /// <param name="ids">The identifiers.</param>
    /// <returns>Batches of at most <see cref="ParameterBatchSize"/> identifiers.</returns>
    private static IEnumerable<IReadOnlyList<string>> Batch(IReadOnlyList<string> ids)
    {
        for (var index = 0; index < ids.Count; index += ParameterBatchSize)
        {
            yield return ids.Skip(index).Take(ParameterBatchSize).ToList();
        }
    }

    /// <summary>Opens a SQLite file for reading only.</summary>
    /// <param name="path">Absolute path of an existing database file.</param>
    /// <returns>An open read-only connection.</returns>
    /// <remarks>
    /// Read-only at the driver level, so an export physically cannot modify the live install even if
    /// a future edit to this file got a statement wrong.
    /// </remarks>
    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());

        connection.Open();
        return connection;
    }

    /// <summary>Opens a SQLite file for writing, creating it when absent.</summary>
    /// <param name="path">Absolute path of the database file.</param>
    /// <returns>An open read-write connection.</returns>
    private static SqliteConnection OpenReadWrite(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        connection.Open();
        return connection;
    }

    /// <summary>Reads the last applied migration script name from the app database.</summary>
    /// <returns>The script name, or an empty string when it cannot be determined.</returns>
    /// <remarks>
    /// This is the ONLY thing the packer reads from <c>techiedesk.db</c>, and it reads exactly one
    /// column of the DbUp journal. The credential-bearing tables in that file — <c>LicenseCache</c>,
    /// <c>Connector</c>, <c>InstanceSetting</c> — have no query anywhere in this type.
    /// </remarks>
    private string ReadSchemaVersion()
    {
        var appDbPath = Path.Combine(DataDirectoryPath, DataDirectory.AppDbFileName);
        if (!File.Exists(appDbPath))
        {
            return string.Empty;
        }

        try
        {
            using var connection = OpenReadOnly(appDbPath);
            return connection.QueryFirstOrDefault<string>(
                "SELECT ScriptName FROM SchemaVersions ORDER BY SchemaVersionID DESC LIMIT 1")
                ?? string.Empty;
        }
        catch (SqliteException exception)
        {
            logger.LogDebug(exception, "Could not read the schema version for the backup manifest");
            return string.Empty;
        }
    }

    /// <summary>The embedding and vector-store identity of this install.</summary>
    /// <param name="Embedding">The configured embedding model.</param>
    /// <param name="VectorStore">Where vectors are kept.</param>
    private readonly record struct InstallProfile(EmbeddingIdentity Embedding, VectorStoreType VectorStore);

    /// <summary>Reads the install's embedding and vector-store identity from the saved config file.</summary>
    /// <returns>The identity, falling back to the shipped defaults when nothing is saved yet.</returns>
    private InstallProfile ReadInstallProfile()
    {
        var defaults = new InstallProfile(
            new EmbeddingIdentity("bge-m3", 1024, nameof(EmbeddingSource.Embedded)),
            VectorStoreType.SqliteVec);

        var configPath = Path.Combine(DataDirectoryPath, DataDirectory.ConfigFileName);
        if (!File.Exists(configPath))
        {
            return defaults;
        }

        try
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            var model = defaults.Embedding.Model;
            var dimensions = defaults.Embedding.Dimensions;
            var source = defaults.Embedding.Source;

            if (TryGetProperty(root, "embedding", out var embedding))
            {
                if (TryGetProperty(embedding, "model", out var modelElement) &&
                    modelElement.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(modelElement.GetString()))
                {
                    model = modelElement.GetString()!;
                }

                if (TryGetProperty(embedding, "dimensions", out var dimensionElement) &&
                    dimensionElement.TryGetInt32(out var parsedDimensions) && parsedDimensions > 0)
                {
                    dimensions = parsedDimensions;
                }

                if (TryGetProperty(embedding, "source", out var sourceElement))
                {
                    source = sourceElement.ValueKind == JsonValueKind.Number &&
                             sourceElement.TryGetInt32(out var sourceValue) &&
                             Enum.IsDefined((EmbeddingSource)sourceValue)
                        ? ((EmbeddingSource)sourceValue).ToString()
                        : sourceElement.ToString();
                }
            }

            var vectorStore = defaults.VectorStore;
            if (TryGetProperty(root, "vectorStore", out var vectorElement) &&
                TryGetProperty(vectorElement, "type", out var typeElement) &&
                typeElement.ValueKind == JsonValueKind.Number &&
                typeElement.TryGetInt32(out var typeValue) &&
                Enum.IsDefined((VectorStoreType)typeValue))
            {
                vectorStore = (VectorStoreType)typeValue;
            }

            return new InstallProfile(new EmbeddingIdentity(model, dimensions, source), vectorStore);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogWarning(
                exception,
                "Could not read the embedding identity from the saved configuration; assuming defaults");
            return defaults;
        }
    }

    /// <summary>Looks a JSON property up without depending on its casing.</summary>
    /// <param name="element">The object to search.</param>
    /// <param name="name">Property name.</param>
    /// <param name="value">The property when found.</param>
    /// <returns>True when the property exists.</returns>
    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>Deletes a file, ignoring failure.</summary>
    /// <param name="path">The file to remove.</param>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover .partial is untidy, not harmful, and must never mask the real failure.
        }
    }

    /// <summary>Deletes a directory tree, ignoring failure.</summary>
    /// <param name="path">The directory to remove.</param>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Same rationale as TryDeleteFile: staging is disposable and cleanup must not throw.
        }
    }
}
