namespace TechieDesk.Services.Agents;

/// <summary>
/// The only patch of filesystem the <c>file-operations</c> skill can touch (REQ-RAG-022 safety
/// boundary).
/// </summary>
/// <remarks>
/// <para><b>The boundary this enforces.</b> One directory, chosen by the operator, and nothing
/// above it. Relative paths only; no absolute path, no <c>..</c> segment, no symbolic link whose
/// target sits outside the root. A small allow-list of text-shaped extensions, a size cap, and no
/// delete, move or rename at all.</para>
/// <para><b>Why an allow-list and no delete.</b> An agent is an untrusted author of paths: the
/// model composes them from text it read, which a document can influence. The damage a mistake can
/// do therefore has to be bounded by construction rather than by the model behaving. Reading
/// arbitrary binaries would let an agent exfiltrate a key store through a chat reply, and a delete
/// verb would let a prompt-injected instruction destroy the user's data. Neither is worth the
/// convenience.</para>
/// <para><b>Resolution happens twice.</b> Once on the composed path, to catch traversal, and once
/// on the link target of anything that already exists, to catch a symlink planted inside the root
/// that points out of it. Checking only the first would make the sandbox a suggestion.</para>
/// </remarks>
public sealed class FileOperationsSandbox
{
    /// <summary>The default size cap for a single read or write, in bytes.</summary>
    public const int DefaultMaxFileBytes = 262144;

    /// <summary>The file kinds an agent may read and write when the caller does not choose.</summary>
    public static readonly IReadOnlyList<string> DefaultAllowedExtensions =
        [".txt", ".md", ".csv", ".json", ".log", ".yaml", ".yml", ".xml", ".svg"];

    private readonly HashSet<string> allowedExtensions;

    /// <summary>Initializes a new instance of the <see cref="FileOperationsSandbox"/> class.</summary>
    /// <param name="rootDirectory">The one directory the agent may work inside.</param>
    /// <param name="maxFileBytes">The size cap for a single read or write.</param>
    /// <param name="extensions">
    /// The permitted file extensions, or null for <see cref="DefaultAllowedExtensions"/>.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rootDirectory"/> is blank.</exception>
    public FileOperationsSandbox(
        string rootDirectory,
        int maxFileBytes = DefaultMaxFileBytes,
        IEnumerable<string>? extensions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        MaxFileBytes = maxFileBytes > 0 ? maxFileBytes : DefaultMaxFileBytes;
        allowedExtensions = new HashSet<string>(
            extensions ?? DefaultAllowedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets the directory every path is resolved against and confined to.</summary>
    public string RootDirectory { get; }

    /// <summary>Gets the size cap applied to a single read or write.</summary>
    public int MaxFileBytes { get; }

    /// <summary>Gets the file extensions this sandbox permits.</summary>
    public IReadOnlyCollection<string> AllowedExtensions => allowedExtensions;

    /// <summary>Gets whether the root directory exists on this install.</summary>
    public bool IsAvailable => Directory.Exists(RootDirectory);

    /// <summary>
    /// Resolves a path the model supplied to a real location inside the sandbox.
    /// </summary>
    /// <param name="relativePath">The path from the tool call.</param>
    /// <param name="mustBeAllowedFile">
    /// True to also require an allow-listed file extension; false for a directory listing.
    /// </param>
    /// <param name="fullPath">The resolved absolute path, when accepted.</param>
    /// <returns>Null when accepted, otherwise the refusal to report back to the model.</returns>
    public string? Resolve(string? relativePath, bool mustBeAllowedFile, out string fullPath)
    {
        fullPath = RootDirectory;

        var requested = (relativePath ?? string.Empty).Trim();
        if (mustBeAllowedFile && requested.Length == 0)
        {
            return "Refused: no path was supplied.";
        }

        var shapeRefusal = RefuseShape(requested);
        if (shapeRefusal is not null)
        {
            return shapeRefusal;
        }

        var combined = Path.GetFullPath(Path.Combine(RootDirectory, requested));
        if (!IsInsideRoot(combined))
        {
            return $"Refused: '{requested}' resolves outside the workspace file area.";
        }

        var linkRefusal = RefuseEscapingLink(combined, requested);
        if (linkRefusal is not null)
        {
            return linkRefusal;
        }

        if (mustBeAllowedFile && !allowedExtensions.Contains(Path.GetExtension(combined)))
        {
            return $"Refused: '{Path.GetExtension(combined)}' files are not readable or writable by "
                + $"agents. Allowed: {string.Join(", ", allowedExtensions.Order(StringComparer.Ordinal))}.";
        }

        fullPath = combined;
        return null;
    }

    /// <summary>Renders a resolved path back the way the model asked for it.</summary>
    /// <param name="fullPath">A path already accepted by <see cref="Resolve"/>.</param>
    /// <returns>
    /// The path relative to the sandbox root, using forward slashes. The root itself renders as
    /// <c>/</c> rather than <c>.</c>, because a model shown "." tends to send it back as a literal
    /// path segment on the next call.
    /// </returns>
    public string ToDisplayPath(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        var relative = Path.GetRelativePath(RootDirectory, fullPath).Replace('\\', '/');
        return relative == "." ? "/" : relative;
    }

    /// <summary>Refuses path shapes that have no legitimate use inside a sandbox.</summary>
    /// <param name="requested">The raw path from the tool call.</param>
    /// <returns>The refusal, or null.</returns>
    private static string? RefuseShape(string requested)
    {
        if (requested.Length == 0)
        {
            return null;
        }

        if (Path.IsPathRooted(requested) || requested.StartsWith('~'))
        {
            return $"Refused: '{requested}' is an absolute path. Use a path relative to the "
                + "workspace file area.";
        }

        var segments = requested.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment == "..")
            ? $"Refused: '{requested}' walks out of the workspace file area."
            : null;
    }

    /// <summary>Rejects an existing entry whose link target leaves the sandbox.</summary>
    /// <param name="fullPath">The resolved candidate path.</param>
    /// <param name="requested">The raw path, for the message.</param>
    /// <returns>The refusal, or null.</returns>
    private string? RefuseEscapingLink(string fullPath, string requested)
    {
        FileSystemInfo? entry = File.Exists(fullPath) ? new FileInfo(fullPath)
            : Directory.Exists(fullPath) ? new DirectoryInfo(fullPath)
            : null;

        var target = entry?.ResolveLinkTarget(returnFinalTarget: true);
        return target is not null && !IsInsideRoot(Path.GetFullPath(target.FullName))
            ? $"Refused: '{requested}' is a link that points outside the workspace file area."
            : null;
    }

    /// <summary>Gets whether an absolute path sits at or under the sandbox root.</summary>
    /// <param name="candidate">The absolute path to test.</param>
    /// <returns>True when the path is inside the root.</returns>
    private bool IsInsideRoot(string candidate)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(candidate, RootDirectory, comparison)
            || candidate.StartsWith(RootDirectory + Path.DirectorySeparatorChar, comparison);
    }
}
