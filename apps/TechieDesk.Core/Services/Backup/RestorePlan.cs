namespace TechieDesk.Services.Backup;

/// <summary>
/// What to do when a workspace in the archive already exists here (REQ-FN-047, BRD-145c).
/// </summary>
/// <remarks>
/// There is deliberately no "Merge" member. The benchmark's restore was an all-or-nothing instance
/// rollback with no merge path, and the lesson taken from it is not that merging should be added but
/// that the choice must be the USER'S and must be stated before anything is written. Silently
/// merging two divergent copies of a workspace is the one outcome that cannot be undone or even
/// noticed afterwards.
/// </remarks>
public enum WorkspaceConflictResolution
{
    /// <summary>Leave the existing workspace untouched and import nothing for it.</summary>
    /// <remarks>The default everywhere, because it is the only member that cannot lose data.</remarks>
    Skip,

    /// <summary>Import the archived workspace alongside the existing one under a new identifier.</summary>
    Duplicate,

    /// <summary>Delete the existing workspace's content and import the archived one in its place.</summary>
    Replace
}

/// <summary>Why a restore cannot proceed (REQ-FN-047).</summary>
public enum RestoreBlockReason
{
    /// <summary>Nothing is blocking the restore.</summary>
    None,

    /// <summary>The file is not a readable <c>.tdbak</c> archive, or has no manifest.</summary>
    NotAnArchive,

    /// <summary>The archive was written by a newer, unrecognised archive format.</summary>
    UnsupportedFormatVersion,

    /// <summary>An entry's content hash or length does not match the manifest.</summary>
    IntegrityFailed,

    /// <summary>The archive claims an entry this format does not define, or a traversing path.</summary>
    UnsafeEntry,

    /// <summary>The archived vectors come from a different embedding model than this install uses.</summary>
    EmbeddingModelMismatch
}

/// <summary>
/// A restore refusal's user-visible explanation, as a resource key plus its arguments
/// (REQ-UI-055 / BRD-91).
/// </summary>
/// <param name="MessageKey">A key present in <c>AppStrings.resx</c>.</param>
/// <param name="Arguments">The values its placeholders take, in order.</param>
public sealed record RestoreBlockDetail(string MessageKey, IReadOnlyList<string> Arguments);

/// <summary>One workspace the archive would bring in, and how it lands here.</summary>
/// <param name="WorkspaceId">Identifier as recorded in the archive.</param>
/// <param name="Name">Display name as recorded in the archive.</param>
/// <param name="AlreadyExists">True when a workspace with this identifier exists in this install.</param>
/// <param name="ExistingName">Display name of the existing workspace, when there is one.</param>
/// <param name="DocumentCount">How many documents the archive carries for this workspace.</param>
/// <param name="ThreadCount">How many threads the archive carries for this workspace.</param>
public sealed record RestoreWorkspacePlan(
    string WorkspaceId,
    string Name,
    bool AlreadyExists,
    string? ExistingName,
    int DocumentCount,
    int ThreadCount);

/// <summary>
/// The pre-flight report: exactly what a restore would change, produced before the user commits
/// and before a single byte is written (REQ-FN-047, BRD-145e).
/// </summary>
/// <remarks>
/// Producing this is a strictly read-only operation. It parses the manifest, verifies every entry's
/// hash, checks entry names for traversal, and compares embedding-model identity — so by the time
/// the user is asked to confirm, every reason the restore could be refused has already been found.
/// </remarks>
public sealed record RestorePreflight
{
    /// <summary>Gets the archive path this report describes.</summary>
    public string ArchivePath { get; init; } = string.Empty;

    /// <summary>Gets the archive's manifest, when it could be read.</summary>
    public BackupManifest? Manifest { get; init; }

    /// <summary>Gets the embedding identity this install currently uses.</summary>
    public EmbeddingIdentity? TargetEmbedding { get; init; }

    /// <summary>Gets the reason the restore is blocked, or <see cref="RestoreBlockReason.None"/>.</summary>
    public RestoreBlockReason BlockReason { get; init; } = RestoreBlockReason.None;

    /// <summary>
    /// Gets the RESOURCE KEY for the explanation of <see cref="BlockReason"/>, when blocked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REQ-UI-055 (BRD-91). This was the sentence itself, in English, built here in the service and
    /// rendered at <c>BackupRestore.razor</c> under a title that WAS localized — so a Hindi user saw
    /// a Devanagari heading over an English explanation. These are restore REFUSALS: the user is
    /// being told why their data will not come back, which is the worst possible moment for a
    /// message they cannot read.
    /// </para>
    /// <para>
    /// <see cref="BlockReason"/> stays the machine-readable answer and is what code branches on;
    /// this is only what the screen prints.
    /// </para>
    /// </remarks>
    public string? BlockDetailKey { get; init; }

    /// <summary>
    /// Gets the values <see cref="BlockDetailKey"/>'s placeholders take, in order.
    /// </summary>
    /// <remarks>
    /// Entry names, byte counts, format versions and embedding-model identities — machine values
    /// that are identical in every culture. An exception message from the zip reader is relayed
    /// verbatim for the same reason: it is the runtime's text, not ours.
    /// </remarks>
    public IReadOnlyList<string> BlockDetailArguments { get; init; } = [];

    /// <summary>Gets the per-workspace plan, including which ones already exist here.</summary>
    public IReadOnlyList<RestoreWorkspacePlan> Workspaces { get; init; } = [];

    /// <summary>Gets a value indicating whether the restore may proceed.</summary>
    public bool CanRestore => BlockReason == RestoreBlockReason.None;

    /// <summary>Gets a value indicating whether any workspace in the archive already exists here.</summary>
    /// <remarks>When true the caller must supply an explicit conflict choice before applying.</remarks>
    public bool HasConflicts => Workspaces.Any(workspace => workspace.AlreadyExists);

    /// <summary>
    /// Gets a value indicating whether re-embedding is the remedy for the block.
    /// </summary>
    /// <remarks>
    /// True only for <see cref="RestoreBlockReason.EmbeddingModelMismatch"/>. The archive's documents
    /// and chunk text are perfectly good; it is exclusively the vectors that are unusable, so the
    /// honest offer is to import the content and re-embed it here rather than to refuse outright.
    /// </remarks>
    public bool CanReEmbed => BlockReason == RestoreBlockReason.EmbeddingModelMismatch;
}

/// <summary>Choices the user makes after reading a <see cref="RestorePreflight"/>.</summary>
public sealed record RestoreChoices
{
    /// <summary>Gets how to resolve workspaces that already exist here.</summary>
    public WorkspaceConflictResolution Conflict { get; init; } = WorkspaceConflictResolution.Skip;

    /// <summary>
    /// Gets a value indicating whether to import content whose embedding model does not match,
    /// discarding the archived vectors so they can be regenerated here.
    /// </summary>
    /// <remarks>
    /// This is the "re-embed instead" offer from BRD-145a, and it is the ONLY way past a model
    /// mismatch. It never imports a foreign vector — it imports the chunk text with a null vector, so
    /// the result is visibly un-embedded rather than invisibly mis-embedded. Refusing loudly and
    /// degrading visibly are both acceptable; importing silently-wrong vectors is not.
    /// </remarks>
    public bool ReEmbedOnModelMismatch { get; init; }
}

/// <summary>What a completed restore actually did.</summary>
/// <param name="WorkspacesImported">Workspaces written, whether new, duplicated or replaced.</param>
/// <param name="WorkspacesSkipped">Workspaces left alone because they already existed.</param>
/// <param name="DocumentsImported">Catalogue documents written.</param>
/// <param name="ChunksImported">Chunks written.</param>
/// <param name="ThreadsImported">Threads written.</param>
/// <param name="MessagesImported">Messages written.</param>
/// <param name="VectorsDiscarded">Chunks imported without their archived vector, pending re-embed.</param>
public sealed record RestoreOutcome(
    int WorkspacesImported,
    int WorkspacesSkipped,
    int DocumentsImported,
    int ChunksImported,
    int ThreadsImported,
    int MessagesImported,
    int VectorsDiscarded);

/// <summary>What an export produced.</summary>
/// <param name="ArchivePath">Absolute path of the archive written.</param>
/// <param name="SizeBytes">Size of the archive on disk.</param>
/// <param name="Manifest">The manifest embedded in it.</param>
public sealed record BackupOutcome(string ArchivePath, long SizeBytes, BackupManifest Manifest);
