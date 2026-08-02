namespace TechieDesk.Services.Backup;

/// <summary>
/// Constants and path rules for the self-contained <c>.tdbak</c> archive (REQ-FN-046, BRD-144).
/// </summary>
/// <remarks>
/// <para>
/// The archive is an ordinary ZIP container holding a small, FIXED set of newline-delimited JSON
/// streams plus a <see cref="ManifestEntryName"/>. Both halves of that shape are deliberate.
/// </para>
/// <para>
/// <b>Fixed entry set — this is how credentials are excluded BY CONSTRUCTION (BRD-144).</b> Nothing
/// in the packer enumerates the data directory. It opens exactly two SQLite files, reads exactly the
/// six tables named below through explicit column lists, and writes exactly the entries named below.
/// The credential-bearing artefacts — <c>connector-secrets.json</c>, the Data Protection
/// <c>keys/</c> ring, the <c>LicenseCache</c> table holding AppManager tokens, and the
/// <c>apiKey</c> fields of <c>techierag-config.json</c> — are not excluded by a filter that a later
/// edit could weaken. There is simply no code path that can reach them. An archive is expected to
/// land in a third-party sync folder, so "cannot" matters more than "does not".
/// </para>
/// <para>
/// <b>JSONL, not a database copy.</b> Copying the live SQLite files would be far less code, but it
/// would carry whatever else those files hold — including rows a future migration adds — and it
/// would defeat per-workspace granularity. A row-per-line stream also lets pack and unpack run in
/// bounded memory (ADR-013), which is the failure the benchmark hit: it withdrew its own export
/// partly because large instances "crash during zipping".
/// </para>
/// </remarks>
public static class BackupArchive
{
    /// <summary>File extension of a TechieDesk backup archive.</summary>
    public const string FileExtension = ".tdbak";

    /// <summary>
    /// Version of the archive layout itself, independent of the app and database schema versions.
    /// </summary>
    /// <remarks>
    /// Versioned from the first release on purpose. Once a user owns a <c>.tdbak</c> file the format
    /// is effectively frozen, so the field that lets a later build recognise an older layout has to
    /// exist before the first archive is written, not after.
    /// </remarks>
    public const int FormatVersion = 1;

    /// <summary>Entry holding the <see cref="BackupManifest"/>; always written last, read first.</summary>
    /// <remarks>
    /// Written last because the manifest carries a content hash per entry and those hashes are only
    /// known once each entry has streamed to disk. A ZIP central directory is order-independent, so
    /// writing it last costs nothing on the read side.
    /// </remarks>
    public const string ManifestEntryName = "manifest.json";

    /// <summary>Entry holding one JSON <c>TrWorkspace</c> row per line.</summary>
    public const string WorkspacesEntryName = "workspaces.jsonl";

    /// <summary>Entry holding one JSON <c>TrWorkspaceDocument</c> row per line.</summary>
    public const string WorkspaceDocumentsEntryName = "workspace-documents.jsonl";

    /// <summary>Entry holding one JSON <c>TrThread</c> row per line.</summary>
    public const string ThreadsEntryName = "threads.jsonl";

    /// <summary>Entry holding one JSON <c>TrMessage</c> row per line.</summary>
    public const string MessagesEntryName = "messages.jsonl";

    /// <summary>Entry holding one JSON <c>Documents</c> row per line.</summary>
    public const string DocumentsEntryName = "documents.jsonl";

    /// <summary>Entry holding one JSON <c>Chunks</c> row per line, embedding vector included.</summary>
    public const string ChunksEntryName = "chunks.jsonl";

    /// <summary>Gets the content entry names, in the order the packer writes them.</summary>
    /// <remarks>
    /// This list IS the allow-list. <see cref="IsKnownEntryName"/> rejects anything outside it, so an
    /// archive cannot smuggle a seventh file past the unpacker even if it names it harmlessly.
    /// </remarks>
    public static IReadOnlyList<string> ContentEntryNames { get; } =
    [
        WorkspacesEntryName,
        WorkspaceDocumentsEntryName,
        ThreadsEntryName,
        MessagesEntryName,
        DocumentsEntryName,
        ChunksEntryName
    ];

    /// <summary>Determines whether an entry name is one this format defines.</summary>
    /// <param name="entryName">The raw entry name as the archive claims it.</param>
    /// <returns>True when the name is the manifest or one of the content entries.</returns>
    public static bool IsKnownEntryName(string? entryName) =>
        entryName is not null &&
        (string.Equals(entryName, ManifestEntryName, StringComparison.Ordinal) ||
         ContentEntryNames.Contains(entryName, StringComparer.Ordinal));

    /// <summary>
    /// Resolves an archive-supplied entry name to an absolute path guaranteed to sit inside a root
    /// directory, or refuses it (REQ-FN-047, zip-slip).
    /// </summary>
    /// <param name="rootDirectory">Absolute directory the entry must land inside.</param>
    /// <param name="entryName">The entry name exactly as the archive claims it, untrusted.</param>
    /// <param name="resolvedPath">The safe absolute path when this returns true; otherwise null.</param>
    /// <returns>True when the entry name is safe to write; false when it must be refused.</returns>
    /// <remarks>
    /// <para>
    /// Every clause here corresponds to a way the classic zip-slip traversal is spelled, and the
    /// checks are deliberately redundant. The name is rejected when it is blank, carries a NUL, is
    /// absolute in either POSIX or Windows form, is a UNC path, contains a drive-letter prefix, or
    /// contains a <c>..</c> segment under EITHER separator — a Windows-authored archive spells
    /// traversal <c>..\</c>, which is an ordinary filename character on macOS and so would survive a
    /// naive check on this platform.
    /// </para>
    /// <para>
    /// The final containment test is the one that actually has to hold, and it is done on the fully
    /// resolved path rather than on the string: the textual checks can be reasoned around, but a
    /// resolved path that does not start with the resolved root plus a separator cannot be. The
    /// separator boundary matters — without it a root of <c>/data</c> would accept <c>/data-evil</c>.
    /// </para>
    /// </remarks>
    public static bool TryResolveSafePath(
        string rootDirectory, string? entryName, out string? resolvedPath)
    {
        resolvedPath = null;

        if (string.IsNullOrWhiteSpace(entryName) || entryName.Contains('\0'))
        {
            return false;
        }

        if (Path.IsPathRooted(entryName) || entryName.StartsWith('/') || entryName.StartsWith('\\'))
        {
            return false;
        }

        // A drive-qualified name ("C:evil") is rooted on Windows but merely odd on macOS; refuse it
        // on every host so an archive behaves identically wherever it is restored.
        if (entryName.Length >= 2 && entryName[1] == ':')
        {
            return false;
        }

        var segments = entryName.Split('/', '\\');
        if (segments.Any(segment => segment is ".."))
        {
            return false;
        }

        var root = Path.GetFullPath(rootDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));

        var boundary = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        // macOS and Windows file systems are case-insensitive in practice; Unix is not. Comparing
        // ordinally on Unix and case-insensitively elsewhere matches how the host would open it.
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!candidate.StartsWith(boundary, comparison))
        {
            return false;
        }

        resolvedPath = candidate;
        return true;
    }
}
