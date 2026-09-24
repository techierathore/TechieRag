using System.Globalization;
using System.Security.Cryptography;
using TechieRag.Embedded;
using TechieRag.Local.Runtime;

namespace TechieRag.Local;

/// <summary>
/// Puts a model's files in place once: terms first, then a resumable download through the shared
/// <see cref="ModelDownloadService"/>, then a SHA-256 check of every file (REQ-RAG-061, REQ-RAG-062).
/// </summary>
/// <remarks>
/// <para><b>Reused, not forked.</b> The download itself — size reported before the first byte,
/// <c>.part</c> files resumed with a range request, progress events — is the one service every
/// TechieRag model uses, so a host subscribes once.</para>
/// <para><b>Verified once.</b> A folder whose files all passed is marked with
/// <see cref="VerifiedMarkerFileName"/> (hash and length per file); a later start re-hashes only a
/// file whose length or pinned hash no longer matches the mark.</para>
/// </remarks>
internal sealed class LocalModelStore
{
    /// <summary>The file in a model folder recording which files passed their SHA-256 check.</summary>
    internal const string VerifiedMarkerFileName = ".techierag-verified";

    private readonly ModelDownloadService downloads;

    /// <summary>Creates a store over a download service.</summary>
    /// <param name="downloads">The download service.</param>
    public LocalModelStore(ModelDownloadService downloads)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        this.downloads = downloads;
    }

    /// <summary>Gets the store over the process-wide download service.</summary>
    public static LocalModelStore Shared { get; } = new(ModelDownloadService.Instance);

    /// <summary>Gets the mirror in effect: <c>TECHIERAG_MODEL_BASE_URL</c>, or null.</summary>
    public static string? Mirror =>
        Environment.GetEnvironmentVariable(EmbeddedEmbeddingProvider.ModelBaseUrlEnvironmentVariable) is { Length: > 0 } value
            ? value
            : null;

    /// <summary>
    /// Makes sure every file of a variant is in the folder and verified.
    /// </summary>
    /// <param name="model">The model (its terms are asked for).</param>
    /// <param name="variant">The file set.</param>
    /// <param name="directory">The folder.</param>
    /// <param name="options">The provider options (terms acceptance).</param>
    /// <param name="mirror">The mirror, or null for the pinned source.</param>
    /// <param name="cancellationToken">Cancels; a partial file is kept for resuming.</param>
    /// <returns>A task that completes when the folder is complete and verified.</returns>
    /// <exception cref="LocalModelTermsNotAcceptedException">A download is needed and the terms were not accepted.</exception>
    /// <exception cref="LocalModelIntegrityException">A file failed its SHA-256 check; it was deleted.</exception>
    public async Task EnsureAsync(
        LocalModel model,
        LocalModelVariant variant,
        string directory,
        LocalLlmOptions options,
        string? mirror,
        CancellationToken cancellationToken)
    {
        var files = variant.GetDownloadFiles(mirror);
        if (!ModelDownloadService.IsComplete(directory, files) || !AllExactLength(directory, variant))
        {
            var terms = model.GetTerms() with { DownloadBytes = ModelDownloadService.GetPendingBytes(directory, files) };
            await RequireTermsAsync(terms, options, cancellationToken).ConfigureAwait(false);
            await downloads.DownloadAsync(model.Id, directory, files, cancellationToken).ConfigureAwait(false);
        }

        await VerifyAsync(directory, variant, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses unless the host has signalled acceptance of the terms.
    /// </summary>
    /// <param name="terms">The terms.</param>
    /// <param name="options">The provider options.</param>
    /// <param name="cancellationToken">Cancels the host's dialog.</param>
    /// <returns>A task that completes when the terms are accepted.</returns>
    /// <exception cref="LocalModelTermsNotAcceptedException">They were not accepted.</exception>
    internal static async Task RequireTermsAsync(LocalModelTerms terms, LocalLlmOptions options, CancellationToken cancellationToken)
    {
        if (options.TermsAccepted)
        {
            return;
        }

        if (options.ConfirmTermsAsync is { } confirm && await confirm(terms, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        throw new LocalModelTermsNotAcceptedException(terms);
    }

    /// <summary>
    /// Checks every file's SHA-256, skipping those the folder's mark already vouches for.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <param name="variant">The file set with pinned hashes.</param>
    /// <param name="cancellationToken">Cancels hashing.</param>
    /// <returns>A task that completes when every file passed.</returns>
    /// <exception cref="LocalModelIntegrityException">A file failed; it and the mark were deleted.</exception>
    internal static async Task VerifyAsync(string directory, LocalModelVariant variant, CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(directory, VerifiedMarkerFileName);
        var marked = ReadMarker(markerPath);
        foreach (var file in variant.Files)
        {
            var path = Path.Combine(directory, file.FileName);
            var length = new FileInfo(path).Length;
            if (marked.TryGetValue(file.FileName, out var mark) && mark.Sha256 == file.Sha256 && mark.Length == length)
            {
                continue;
            }

            var actual = await HashAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                File.Delete(markerPath);
                throw new LocalModelIntegrityException(file.FileName, file.Sha256, actual);
            }

            marked[file.FileName] = (file.Sha256, length);
        }

        await File.WriteAllLinesAsync(
            markerPath,
            marked.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Value.Sha256} {m.Value.Length} {m.Key}")),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Computes a file's lower-case hex SHA-256.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels hashing.</param>
    /// <returns>The hash.</returns>
    internal static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Whether every file is present at exactly its pinned length (the shared service accepts 95 percent).</summary>
    private static bool AllExactLength(string directory, LocalModelVariant variant) =>
        variant.Files.All(f =>
        {
            var path = Path.Combine(directory, f.FileName);
            return File.Exists(path) && new FileInfo(path).Length == f.Bytes;
        });

    private static Dictionary<string, (string Sha256, long Length)> ReadMarker(string markerPath)
    {
        var marked = new Dictionary<string, (string Sha256, long Length)>(StringComparer.Ordinal);
        if (!File.Exists(markerPath))
        {
            return marked;
        }

        foreach (var line in File.ReadAllLines(markerPath))
        {
            var parts = line.Split(' ', 3);
            if (parts.Length == 3 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
            {
                marked[parts[2]] = (parts[0], length);
            }
        }

        return marked;
    }
}
