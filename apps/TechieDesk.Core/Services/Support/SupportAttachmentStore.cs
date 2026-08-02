using TechieDeskDb;

namespace TechieDesk.Services.Support;

/// <summary>
/// File-system implementation of <see cref="ISupportAttachmentStore"/> (REQ-UI-047).
/// </summary>
/// <remarks>
/// <para>
/// The root comes from <see cref="DataDirectory"/> and nowhere else. That is REQ-FN-037's
/// single-authority rule, and it is not decoration here: an attachment resolved against
/// <see cref="AppContext.BaseDirectory"/> or the content root would be written inside the signed,
/// read-only <c>.app</c> bundle, which fails on any real install exactly as the logger and the
/// update downloader once did.
/// </para>
/// <para>
/// The type and size rules are re-enforced here rather than trusted from the caller. The size is
/// checked <b>while the bytes stream in</b> and the partial file is deleted the moment the cap is
/// passed, so a client that under-declares a 4 GB file cannot fill the user's disk before anyone
/// notices.
/// </para>
/// </remarks>
public sealed class SupportAttachmentStore : ISupportAttachmentStore
{
    private const int CopyBufferSize = 81920;

    private readonly IConfiguration configuration;
    private readonly ILogger<SupportAttachmentStore> logger;

    /// <summary>Initializes a new instance of the <see cref="SupportAttachmentStore"/> class.</summary>
    /// <param name="configuration">Application configuration, read for the data-directory override.</param>
    /// <param name="logger">Logger.</param>
    public SupportAttachmentStore(IConfiguration configuration, ILogger<SupportAttachmentStore> logger)
    {
        this.configuration = configuration;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string RootDirectory => Path.Combine(
        DataDirectory.Resolve(configuration[DataDirectory.ConfigKey]),
        DataDirectory.SupportAttachmentsDirectoryName);

    /// <summary>Infers a MIME type from an allowed extension.</summary>
    /// <param name="fileName">The sanitised file name.</param>
    /// <returns>The MIME type, or <c>application/octet-stream</c> when unrecognised.</returns>
    public static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".pdf" => "application/pdf",
        ".log" => "text/plain",
        _ => "application/octet-stream"
    };

    /// <inheritdoc />
    public string BeginDraft() => Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    public async Task<SupportAttachment> SaveAsync(
        string draftKey,
        string fileName,
        string? contentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var safeName = SupportAttachmentPolicy.SafeFileName(fileName);

        // Type first, and on the SANITISED name: rejecting "evil.png/../shell.sh" on the offered
        // name would pass a check the file that lands on disk never had to satisfy.
        var extension = Path.GetExtension(safeName);
        if (string.IsNullOrEmpty(extension) || !SupportAttachmentPolicy.AllowedExtensions.Contains(extension))
        {
            throw new SupportAttachmentRejectedException(
                $"\"{fileName}\" can't be attached — support accepts {SupportAttachmentPolicy.LimitsSummary}.");
        }

        var directory = DraftDirectory(draftKey);
        Directory.CreateDirectory(directory);

        var target = UniquePath(directory, safeName);
        EnsureInsideRoot(target);

        long written = 0;
        try
        {
            await using (var file = new FileStream(
                target, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true))
            {
                var buffer = new byte[CopyBufferSize];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    written += read;
                    if (written > SupportAttachmentPolicy.MaxFileSizeBytes)
                    {
                        throw new SupportAttachmentRejectedException(
                            $"\"{fileName}\" is larger than the "
                            + $"{SupportAttachmentPolicy.FormatSize(SupportAttachmentPolicy.MaxFileSizeBytes)} attachment limit.");
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            DeleteQuietly(target);
            throw;
        }

        if (written == 0)
        {
            DeleteQuietly(target);
            throw new SupportAttachmentRejectedException($"\"{fileName}\" can't be attached — the file is empty.");
        }

        var resolvedType = string.IsNullOrWhiteSpace(contentType) ? ContentTypeFor(safeName) : contentType;
        logger.LogInformation(
            "Staged support attachment {FileName} ({Bytes} bytes) for draft {DraftKey}",
            safeName, written, draftKey);

        return new SupportAttachment(safeName, written, resolvedType, target);
    }

    /// <inheritdoc />
    public void Remove(SupportAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        EnsureInsideRoot(attachment.FullPath);
        DeleteQuietly(attachment.FullPath);
    }

    /// <inheritdoc />
    public void DiscardDraft(string draftKey)
    {
        var directory = DraftDirectory(draftKey);
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not discard support attachment draft {DraftKey}", draftKey);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Could not discard support attachment draft {DraftKey}", draftKey);
        }
    }

    /// <summary>Resolves the folder holding one draft's attachments.</summary>
    /// <param name="draftKey">The draft key, sanitised the same way a file name is.</param>
    /// <returns>An absolute directory path under <see cref="RootDirectory"/>.</returns>
    private string DraftDirectory(string draftKey) =>
        Path.Combine(RootDirectory, SupportAttachmentPolicy.SafeFileName(draftKey));

    /// <summary>Picks a path that does not already exist, keeping the original name when free.</summary>
    /// <param name="directory">The draft directory.</param>
    /// <param name="safeName">The sanitised file name.</param>
    /// <returns>An absolute path that no file currently occupies.</returns>
    private static string UniquePath(string directory, string safeName)
    {
        var candidate = Path.Combine(directory, safeName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var index = 2; ; index++)
        {
            candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Refuses any path that does not resolve inside <see cref="RootDirectory"/>.</summary>
    /// <param name="path">The absolute path about to be written or deleted.</param>
    /// <exception cref="SupportAttachmentRejectedException">When the path escapes the root.</exception>
    private void EnsureInsideRoot(string path)
    {
        var root = Path.GetFullPath(RootDirectory);
        var boundary = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (!Path.GetFullPath(path).StartsWith(boundary, comparison))
        {
            throw new SupportAttachmentRejectedException(
                "That attachment can't be staged — its name resolves outside the support attachments folder.");
        }
    }

    /// <summary>Deletes a file, tolerating its absence and a locked handle.</summary>
    /// <param name="path">The absolute file path.</param>
    private void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not delete staged support attachment {Path}", path);
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning(exception, "Could not delete staged support attachment {Path}", path);
        }
    }
}
