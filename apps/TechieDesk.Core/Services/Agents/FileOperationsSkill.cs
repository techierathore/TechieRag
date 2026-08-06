using System.Globalization;
using System.Text;

namespace TechieDesk.Services.Agents;

/// <summary>
/// The <c>file-operations</c> catalogue skill as a library tool (BRD-84 / REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>The safety boundary, stated plainly.</b> Three verbs — <c>list</c>, <c>read</c>,
/// <c>write</c> — inside ONE operator-chosen directory, on text-shaped files only, under a size
/// cap. No delete, no move, no rename, no path that resolves outside the root, no symbolic link
/// that leaves it. An agent never gets the user's filesystem; it gets a workspace scratch area.
/// <see cref="FileOperationsSandbox"/> holds the enforcement and is tested on its own.</para>
/// <para><b>Why the verbs are a fixed list rather than a shell.</b> The model composes paths from
/// text it read, and a document can influence that text. Bounding the damage by construction is the
/// only defence that does not depend on the model behaving, so the destructive verbs simply do not
/// exist to be called.</para>
/// <para><b>Overwrites are reported, not hidden.</b> A write that replaced an existing file says
/// so, because the difference matters to whoever reads the execution trace afterwards.</para>
/// </remarks>
public static class FileOperationsSkill
{
    /// <summary>The JSON Schema for the file-operations tool's parameters.</summary>
    public const string Schema =
        """{"type":"object","properties":{"operation":{"type":"string","enum":["list","read","write"],"description":"What to do"},"path":{"type":"string","description":"Path relative to the workspace file area. Absolute paths and .. are refused."},"content":{"type":"string","description":"Text to write, for the write operation"}},"required":["operation"]}""";

    /// <summary>The description the model is shown.</summary>
    public const string Description =
        "Lists, reads and writes text files inside this workspace's file area only. Paths are "
        + "relative to that area; anything outside it, and any delete or rename, is refused.";

    /// <summary>The most entries a single listing returns.</summary>
    public const int MaxListedEntries = 200;

    /// <summary>
    /// Binds the file-operations skill to a sandbox.
    /// </summary>
    /// <param name="sandbox">
    /// The directory the agent may work inside, or null when this workspace has no file area.
    /// </param>
    /// <returns>The skill implementation.</returns>
    public static SkillImplementation Create(FileOperationsSandbox? sandbox) =>
        new(SkillCatalog.FileOperations, Description, Schema,
            (argumentsJson, cancellationToken) => RunAsync(sandbox, argumentsJson, cancellationToken));

    /// <summary>Runs one file call.</summary>
    /// <param name="sandbox">The sandbox, or null.</param>
    /// <param name="argumentsJson">The tool-call arguments.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result, a refusal, or an unavailability report.</returns>
    private static async Task<SkillOutcome> RunAsync(
        FileOperationsSandbox? sandbox, string argumentsJson, CancellationToken cancellationToken)
    {
        if (sandbox is null)
        {
            return SkillUnavailable.Coded("SkillUnavailableFilesNoArea");
        }

        if (!sandbox.IsAvailable)
        {
            return SkillUnavailable.Coded("SkillUnavailableFilesMissingArea", sandbox.RootDirectory);
        }

        var operation = SkillArguments.ReadString(argumentsJson, "operation").Trim().ToLowerInvariant();
        var path = SkillArguments.ReadString(argumentsJson, "path");

        try
        {
            return operation switch
            {
                "list" => List(sandbox, path),
                "read" => await ReadAsync(sandbox, path, cancellationToken).ConfigureAwait(false),
                "write" => await WriteAsync(sandbox, path, argumentsJson, cancellationToken).ConfigureAwait(false),
                "" => "No operation supplied. Use list, read or write.",
                _ => $"Refused: '{operation}' is not an allowed operation. Use list, read or write — "
                    + "agents cannot delete, move or rename."
            };
        }
        catch (IOException ex)
        {
            return $"The file operation failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"The file operation was denied by the operating system: {ex.Message}";
        }
    }

    /// <summary>Lists one directory inside the sandbox.</summary>
    /// <param name="sandbox">The sandbox.</param>
    /// <param name="path">The directory, relative to the root; empty means the root itself.</param>
    /// <returns>The listing, or a refusal.</returns>
    private static string List(FileOperationsSandbox sandbox, string path)
    {
        var refusal = sandbox.Resolve(path, mustBeAllowedFile: false, out var fullPath);
        if (refusal is not null)
        {
            return refusal;
        }

        if (!Directory.Exists(fullPath))
        {
            return $"No directory '{sandbox.ToDisplayPath(fullPath)}' in the workspace file area.";
        }

        var entries = new DirectoryInfo(fullPath)
            .EnumerateFileSystemInfos()
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListedEntries)
            .Select(Describe)
            .ToList();

        return entries.Count == 0
            ? $"'{sandbox.ToDisplayPath(fullPath)}' is empty."
            : string.Join("\n", entries.Prepend(
                $"{entries.Count} entr(y/ies) in '{sandbox.ToDisplayPath(fullPath)}':"));
    }

    /// <summary>Reads one file inside the sandbox.</summary>
    /// <param name="sandbox">The sandbox.</param>
    /// <param name="path">The file, relative to the root.</param>
    /// <param name="cancellationToken">Token to cancel the read.</param>
    /// <returns>The file text, or a refusal.</returns>
    private static async Task<string> ReadAsync(
        FileOperationsSandbox sandbox, string path, CancellationToken cancellationToken)
    {
        var refusal = sandbox.Resolve(path, mustBeAllowedFile: true, out var fullPath);
        if (refusal is not null)
        {
            return refusal;
        }

        if (!File.Exists(fullPath))
        {
            return $"No file '{sandbox.ToDisplayPath(fullPath)}' in the workspace file area.";
        }

        var length = new FileInfo(fullPath).Length;
        if (length > sandbox.MaxFileBytes)
        {
            return $"Refused: '{sandbox.ToDisplayPath(fullPath)}' is {length} bytes, over the "
                + $"{sandbox.MaxFileBytes}-byte limit for agent reads.";
        }

        var text = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return $"{sandbox.ToDisplayPath(fullPath)} ({length} bytes):\n{text}";
    }

    /// <summary>Writes one file inside the sandbox.</summary>
    /// <param name="sandbox">The sandbox.</param>
    /// <param name="path">The file, relative to the root.</param>
    /// <param name="argumentsJson">The tool-call arguments, carrying the content.</param>
    /// <param name="cancellationToken">Token to cancel the write.</param>
    /// <returns>What was written, or a refusal.</returns>
    private static async Task<string> WriteAsync(
        FileOperationsSandbox sandbox,
        string path,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        var refusal = sandbox.Resolve(path, mustBeAllowedFile: true, out var fullPath);
        if (refusal is not null)
        {
            return refusal;
        }

        var content = SkillArguments.ReadString(argumentsJson, "content");
        var size = Encoding.UTF8.GetByteCount(content);
        if (size > sandbox.MaxFileBytes)
        {
            return $"Refused: {size} bytes is over the {sandbox.MaxFileBytes}-byte limit for agent "
                + "writes.";
        }

        var existed = File.Exists(fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} '{1}' ({2} bytes).",
            existed ? "Overwrote" : "Wrote",
            sandbox.ToDisplayPath(fullPath),
            size);
    }

    /// <summary>Describes one listed entry.</summary>
    /// <param name="entry">The file or directory found.</param>
    /// <returns>The listing line.</returns>
    private static string Describe(FileSystemInfo entry) => entry is FileInfo file
        ? string.Format(CultureInfo.InvariantCulture, "  {0} ({1} bytes)", file.Name, file.Length)
        : $"  {entry.Name}/";
}
