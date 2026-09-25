using System.Globalization;
using System.Security.Cryptography;
using TechieRag.Embedded;
using TechieRag.Local.HuggingFace;
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
/// <para><b>Hugging Face names</b> (REQ-RAG-108) are resolved first (<see cref="PrepareAsync"/>): from the
/// pin and manifest on disk when present, else through <see cref="HuggingFaceHub"/>; the commit is pinned
/// at once, before the terms are shown.</para>
/// </remarks>
internal sealed class LocalModelStore
{
    /// <summary>The file in a model folder recording which files passed their SHA-256 check.</summary>
    internal const string VerifiedMarkerFileName = ".techierag-verified";

    private readonly ModelDownloadService downloads;
    private readonly HuggingFaceHub? hub;
    private readonly SemaphoreSlim resolveGate = new(1, 1);

    /// <summary>Creates a store over a download service.</summary>
    /// <param name="downloads">The download service.</param>
    /// <param name="hub">The Hugging Face API client; null uses huggingface.co.</param>
    public LocalModelStore(ModelDownloadService downloads, HuggingFaceHub? hub = null)
    {
        ArgumentNullException.ThrowIfNull(downloads);
        this.downloads = downloads;
        this.hub = hub;
    }

    /// <summary>Gets the store over the process-wide download service.</summary>
    public static LocalModelStore Shared { get; } = new(ModelDownloadService.Instance);

    /// <summary>Gets the mirror in effect: <c>TECHIERAG_MODEL_BASE_URL</c>, or null.</summary>
    public static string? Mirror =>
        Environment.GetEnvironmentVariable(EmbeddedEmbeddingProvider.ModelBaseUrlEnvironmentVariable) is { Length: > 0 } value
            ? value
            : null;

    /// <summary>
    /// Resolves a Hugging Face model to one commit, its licence and its files, once; does nothing for
    /// any other model. No model file is requested.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>A task that completes when <see cref="LocalModel.Resolution"/> is set.</returns>
    /// <exception cref="InvalidOperationException">The model is gated, private, missing, or not in ONNX Runtime GenAI's format.</exception>
    /// <exception cref="HttpRequestException">Hugging Face could not be reached.</exception>
    public async Task PrepareAsync(LocalModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var source = model.HuggingFace;
        if (source is null || model.Resolution is not null)
        {
            return;
        }

        await resolveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (model.Resolution is not null || HuggingFaceResolution.TryRestore(model))
            {
                return;
            }

            var snapshot = await (hub ?? HuggingFaceHub.Default).ResolveAsync(source, cancellationToken).ConfigureAwait(false);
            var resolution = HuggingFaceResolution.For(source, snapshot);
            snapshot.WriteManifest(resolution.Directory);
            HuggingFaceResolution.WritePin(source, snapshot.Commit);
            model.ApplyResolution(resolution);
        }
        finally
        {
            resolveGate.Release();
        }
    }

    /// <summary>
    /// Gets a model's terms with the bytes a download of the file set a runtime loads would still
    /// transfer (files on disk and <c>.part</c> remainders subtracted, as <see cref="EnsureAsync"/>
    /// reports them); a Hugging Face model is resolved first (<see cref="PrepareAsync"/>).
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="format">The runtime's format.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>The terms.</returns>
    public async Task<LocalModelTerms> GetTermsAsync(LocalModel model, LocalModelFormat format, CancellationToken cancellationToken)
    {
        await PrepareAsync(model, cancellationToken).ConfigureAwait(false);
        var variant = model.FindVariant(format);
        return model.GetTerms() with { DownloadBytes = variant?.GetPendingBytes(model.GetDirectory(variant)) ?? 0 };
    }

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
    /// <exception cref="LocalModelIntegrityException">A file failed its hash check; it was deleted.</exception>
    /// <exception cref="InvalidOperationException">
    /// A download is needed, the file set has no default address and no mirror is set; thrown before
    /// the terms are asked and before any request. Or a Hugging Face model's <c>genai_config.json</c>
    /// names a file its folder does not hold.
    /// </exception>
    public async Task EnsureAsync(
        LocalModel model,
        LocalModelVariant variant,
        string directory,
        LocalLlmOptions options,
        string? mirror,
        CancellationToken cancellationToken)
    {
        if (!variant.IsOnDisk(directory))
        {
            var files = variant.GetDownloadFiles(mirror);
            var terms = model.GetTerms() with { DownloadBytes = ModelDownloadService.GetPendingBytes(directory, files) };
            await RequireTermsAsync(terms, options, cancellationToken).ConfigureAwait(false);
            await downloads.DownloadAsync(model.Id, directory, files, cancellationToken).ConfigureAwait(false);
        }

        await VerifyAsync(directory, variant, cancellationToken).ConfigureAwait(false);
        if (model.HuggingFace is not null)
        {
            model.ApplyGenAiConfig(ReadConfig(directory, variant, model.Id));
        }
    }

    /// <summary>
    /// Reads a downloaded model's configuration and checks that every file it names was downloaded.
    /// </summary>
    /// <param name="directory">The model folder.</param>
    /// <param name="variant">The file set.</param>
    /// <param name="modelId">The model, named in the message.</param>
    /// <returns>The configuration's values.</returns>
    /// <exception cref="InvalidOperationException">A named file is not in the file set, or a value is missing.</exception>
    internal static GenAiConfigInfo ReadConfig(string directory, LocalModelVariant variant, string modelId)
    {
        var config = GenAiConfigInfo.Read(directory);
        var missing = config.FileNames.Where(name => variant.Files.All(f => f.FileName != name)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The model '{modelId}' names {string.Join(", ", missing)} in {GenAiConfigInfo.FileName}, which its folder does not hold; "
                + "ONNX Runtime GenAI cannot load it.");
        }

        return config;
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
    /// Checks every file's hash, skipping those the folder's mark already vouches for.
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
            if (marked.TryGetValue(file.FileName, out var mark) && mark.Sha256 == file.Hash && mark.Length == length)
            {
                continue;
            }

            var actual = file.HashKind == LocalModelHashKind.GitBlobSha1
                ? await HashGitBlobAsync(path, cancellationToken).ConfigureAwait(false)
                : await HashAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, file.Hash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                File.Delete(markerPath);
                throw file.HashKind == LocalModelHashKind.GitBlobSha1
                    ? new LocalModelIntegrityException(file.FileName, file.Hash, actual, "git blob SHA-1")
                    : new LocalModelIntegrityException(file.FileName, file.Hash, actual);
            }

            marked[file.FileName] = (file.Hash, length);
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

    /// <summary>
    /// Computes a file's git blob hash, the fingerprint Hugging Face publishes for a small file: the
    /// lower-case hex SHA-1 of <c>"blob " + length + "\0"</c> followed by the bytes.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels hashing.</param>
    /// <returns>The hash.</returns>
    internal static async Task<string> HashGitBlobAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        sha1.AppendData(System.Text.Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"blob {stream.Length}\0")));
        var buffer = new byte[1 << 20];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            sha1.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(sha1.GetHashAndReset());
    }

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
