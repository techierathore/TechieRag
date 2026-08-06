namespace TechieDesk.Services.Backup;

/// <summary>Granularity a backup archive was taken at (REQ-FN-046, BRD-144).</summary>
/// <remarks>
/// Per-workspace granularity is a requirement rather than a convenience. The AnythingLLM benchmark
/// shipped instance-only export, which is what made it unusable at any real size and contributed to
/// its withdrawal in 2024 — an operator who wants to hand ONE workspace to a colleague should not
/// have to ship, or expose, the whole install.
/// </remarks>
public enum BackupScope
{
    /// <summary>Every workspace, document, chunk, thread and message in the install.</summary>
    Instance,

    /// <summary>Only the named workspaces and the content reachable from them.</summary>
    Workspace
}

/// <summary>
/// Identity of the embedding model an archive's vectors were produced by (REQ-FN-047, BRD-145).
/// </summary>
/// <param name="Model">Model name, for example <c>bge-m3</c>.</param>
/// <param name="Dimensions">Vector width the model emits.</param>
/// <param name="Source">Provider the model was served by, for example <c>Embedded</c>.</param>
/// <remarks>
/// <para>
/// This record exists because of the nastiest failure mode in the whole feature. Vectors produced by
/// a DIFFERENT embedding model are the same shape as native ones — same dimension, same numeric
/// range — so nothing throws, nothing warns, and every subsequent retrieval is quietly wrong.
/// Comparing dimension alone does NOT catch it: <c>bge-m3</c> and any other 1024-wide model agree on
/// width and disagree on meaning. Only the model identity separates them, which is why the name is
/// carried and compared.
/// </para>
/// <para>
/// <see cref="Source"/> is recorded for diagnostics but deliberately NOT compared: the same model
/// served over Ollama and over the embedded runtime produces interchangeable vectors, so refusing
/// that combination would block a legitimate restore for no benefit.
/// </para>
/// </remarks>
public sealed record EmbeddingIdentity(string Model, int Dimensions, string Source)
{
    /// <summary>Determines whether this identity is interchangeable with another for retrieval.</summary>
    /// <param name="other">The identity to compare against, typically the target install's.</param>
    /// <returns>True when vectors from one can be searched alongside vectors from the other.</returns>
    /// <remarks>
    /// Model comparison is case-insensitive because providers disagree on casing for the same
    /// weights (<c>BGE-M3</c> from one, <c>bge-m3</c> from another). Dimension is compared as well,
    /// so a model that changed width between releases is still caught.
    /// </remarks>
    public bool Matches(EmbeddingIdentity other) =>
        Dimensions == other.Dimensions &&
        string.Equals(Model?.Trim(), other.Model?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Renders the identity for display in a pre-flight report.</summary>
    /// <returns>A short human-readable description.</returns>
    public string Describe() => $"{Model} ({Dimensions}-dimensional, via {Source})";
}

/// <summary>Row counts an archive carries, per content stream.</summary>
/// <param name="Workspaces">Number of <c>TrWorkspace</c> rows.</param>
/// <param name="WorkspaceDocuments">Number of <c>TrWorkspaceDocument</c> links.</param>
/// <param name="Threads">Number of <c>TrThread</c> rows.</param>
/// <param name="Messages">Number of <c>TrMessage</c> rows.</param>
/// <param name="Documents">Number of catalogue <c>Documents</c> rows.</param>
/// <param name="Chunks">Number of <c>Chunks</c> rows.</param>
/// <param name="ChunksWithVector">How many of <paramref name="Chunks"/> carry an embedding.</param>
public sealed record BackupCounts(
    int Workspaces,
    int WorkspaceDocuments,
    int Threads,
    int Messages,
    int Documents,
    int Chunks,
    int ChunksWithVector);

/// <summary>Integrity record for one entry inside the archive.</summary>
/// <param name="Name">The entry name.</param>
/// <param name="Length">Uncompressed byte length as written.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the uncompressed bytes.</param>
/// <remarks>
/// Verified in full BEFORE a restore applies anything (REQ-FN-047). A truncated or edited archive
/// therefore fails while the install is still untouched, rather than half-way through writing.
/// </remarks>
public sealed record BackupEntryIntegrity(string Name, long Length, string Sha256);

/// <summary>
/// Versioned header describing what an archive holds and what produced it (REQ-FN-046, BRD-144).
/// </summary>
/// <remarks>
/// Every field is versioning of some kind, and all of it is written from day one on purpose: once
/// users hold <c>.tdbak</c> files the format can no longer be changed compatibly, so the fields a
/// later build needs in order to recognise, refuse or migrate an older archive have to be present in
/// the first one ever written.
/// </remarks>
public sealed record BackupManifest
{
    /// <summary>Gets the version of the archive layout, see <see cref="BackupArchive.FormatVersion"/>.</summary>
    public int ArchiveFormatVersion { get; init; } = BackupArchive.FormatVersion;

    /// <summary>Gets the application version that produced the archive, as the host reported it.</summary>
    public string AppVersion { get; init; } = string.Empty;

    /// <summary>Gets the app-database schema version, i.e. the last migration script applied.</summary>
    /// <remarks>
    /// Carried for diagnostics and for a future compatibility gate. The archive holds no app-database
    /// rows, so this never gates a restore today — but an archive that cannot say which schema
    /// produced it is one a later build cannot reason about at all.
    /// </remarks>
    public string SchemaVersion { get; init; } = string.Empty;

    /// <summary>Gets the instant the archive was written.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets the granularity the archive was taken at.</summary>
    public BackupScope Scope { get; init; }

    /// <summary>Gets the workspace identifiers the archive carries.</summary>
    public IReadOnlyList<string> WorkspaceIds { get; init; } = [];

    /// <summary>Gets the display names of those workspaces, for a pre-flight report.</summary>
    /// <remarks>
    /// Denormalised so the restore screen can name what it is about to change without first parsing
    /// and hashing the workspace stream — the pre-flight has to be cheap enough to run on selection.
    /// </remarks>
    public IReadOnlyList<string> WorkspaceNames { get; init; } = [];

    /// <summary>Gets the embedding model the archived vectors were produced by.</summary>
    public EmbeddingIdentity Embedding { get; init; } = new(string.Empty, 0, string.Empty);

    /// <summary>Gets the row counts per content stream.</summary>
    public BackupCounts Counts { get; init; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Gets the per-entry integrity records covering every content entry.</summary>
    public IReadOnlyList<BackupEntryIntegrity> Entries { get; init; } = [];
}
