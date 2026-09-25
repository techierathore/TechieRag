using System.Net;
using System.Text.Json;
using TechieRag.Local.Runtime;
using TechieRag.Web;

namespace TechieRag.Local.HuggingFace;

/// <summary>
/// Reads a model's licence, commit and file list through Hugging Face's public API, without an account
/// (REQ-RAG-108 / BRD-166).
/// </summary>
/// <remarks>
/// <para><b>Two requests, no model file.</b> <c>GET /api/models/&lt;repository&gt;[/revision/&lt;version&gt;]</c>
/// gives the commit (<c>sha</c>), whether the model is gated, the model card's licence and the
/// repository's file names; <c>GET /api/models/&lt;repository&gt;/tree/&lt;commit&gt;[/&lt;folder&gt;]</c>
/// gives each file's size and fingerprint: <c>lfs.oid</c>, the SHA-256, for a large file, else
/// <c>oid</c>, the git blob SHA-1. Both are metadata; the terms are built from them before any model
/// file is requested.</para>
/// <para><b>Gated and private models are refused.</b> A gated model needs each user's Hugging Face
/// account and a token; the library takes none, so the name is refused with a message saying so.</para>
/// <para><b>Guarded.</b> Requests go through the connect-time private-address guard every outbound call
/// of the library uses (<see cref="HttpWebContentFetcher.CreateGuardedHandler"/>).</para>
/// </remarks>
internal sealed class HuggingFaceHub
{
    /// <summary>Hugging Face's address.</summary>
    internal static readonly Uri DefaultBaseAddress = new("https://huggingface.co/");

    private static readonly Lazy<HuggingFaceHub> Shared =
        new(() => new HuggingFaceHub(DefaultBaseAddress, HttpWebContentFetcher.CreateGuardedHandler()));

    private static readonly string[] LicenceFileNames = ["LICENSE", "LICENSE.md", "LICENSE.txt", "LICENCE", "LICENCE.md", "LICENCE.txt"];

    private readonly HttpClient httpClient;

    /// <summary>Creates a client for a hub.</summary>
    /// <param name="baseAddress">The hub's address, ending in a slash.</param>
    /// <param name="handler">The HTTP handler; the client does not dispose it.</param>
    public HuggingFaceHub(Uri baseAddress, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(handler);
        BaseAddress = baseAddress;
        httpClient = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(2) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TechieRag/1.0 (+https://github.com/techierathore/TechieRag)");
    }

    /// <summary>Gets the client for huggingface.co.</summary>
    public static HuggingFaceHub Default => Shared.Value;

    /// <summary>Gets the hub's address.</summary>
    public Uri BaseAddress { get; }

    /// <summary>
    /// Resolves a name to one commit and reads its licence and runtime files.
    /// </summary>
    /// <param name="source">The model's name.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>The snapshot.</returns>
    /// <exception cref="InvalidOperationException">
    /// The model is gated, private, missing, or its folder is not an ONNX Runtime GenAI model.
    /// </exception>
    /// <exception cref="HttpRequestException">Hugging Face could not be reached or failed.</exception>
    public async Task<HuggingFaceSnapshot> ResolveAsync(HuggingFaceModelSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var repository = source.Repository;
        var infoPath = source.Version is null
            ? $"api/models/{repository}"
            : $"api/models/{repository}/revision/{Uri.EscapeDataString(source.Version)}";
        using var info = await GetJsonAsync(infoPath, source, cancellationToken).ConfigureAwait(false);
        var root = info.RootElement;
        RefuseGated(root, source);

        var commit = Text(root, "sha") is { } sha && HuggingFaceModelSource.IsCommit(sha)
            ? sha
            : throw new InvalidOperationException($"Hugging Face gave no commit for '{source.Id}'.");
        var files = await ListFilesAsync(source, commit, cancellationToken).ConfigureAwait(false);
        return new HuggingFaceSnapshot(
            repository,
            source.Folder,
            commit,
            LicenceName(root),
            TermsUrl(root, repository, commit),
            new Uri(BaseAddress, $"{repository}/resolve/{commit}").ToString(),
            files);
    }

    private async Task<List<LocalModelFileSpec>> ListFilesAsync(HuggingFaceModelSource source, string commit, CancellationToken cancellationToken)
    {
        var treePath = $"api/models/{source.Repository}/tree/{commit}" + (source.Folder.Length == 0 ? string.Empty : "/" + source.Folder);
        using var tree = await GetJsonAsync(treePath, source, cancellationToken).ConfigureAwait(false);
        if (tree.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Hugging Face listed no files for '{source.Id}' at {commit}.");
        }

        var prefix = source.Folder.Length == 0 ? string.Empty : source.Folder + "/";
        var files = tree.RootElement.EnumerateArray()
            .Where(entry => Text(entry, "type") == "file")
            .Select(entry => (Entry: entry, Path: Text(entry, "path") ?? string.Empty))
            .Where(item => item.Path.StartsWith(prefix, StringComparison.Ordinal)
                           && HuggingFaceFileSelector.IsRuntimeFile(item.Path[prefix.Length..]))
            .Select(item => ToFileSpec(item.Entry, item.Path, item.Path[prefix.Length..]))
            .OrderBy(file => file.FileName, StringComparer.Ordinal)
            .ToList();

        if (!files.Any(f => f.FileName == GenAiConfigInfo.FileName))
        {
            throw new InvalidOperationException(
                $"'{source.Id}' at {commit} has no {GenAiConfigInfo.FileName}: it is not an ONNX Runtime GenAI model folder. "
                + "Name the folder inside the repository that holds one (LocalModel.FromHuggingFace(repository, folder)).");
        }

        return files;
    }

    private static LocalModelFileSpec ToFileSpec(JsonElement entry, string path, string fileName)
    {
        if (entry.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object
            && Text(lfs, "oid") is { Length: 64 } sha256 && lfs.TryGetProperty("size", out var lfsSize))
        {
            return new LocalModelFileSpec(fileName, path, lfsSize.GetInt64(), sha256.ToLowerInvariant());
        }

        var oid = Text(entry, "oid") is { Length: 40 } sha1
            ? sha1.ToLowerInvariant()
            : throw new InvalidOperationException($"Hugging Face gave no fingerprint for '{path}'.");
        return new LocalModelFileSpec(fileName, path, entry.GetProperty("size").GetInt64(), oid, LocalModelHashKind.GitBlobSha1);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, HuggingFaceModelSource source, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(new Uri(BaseAddress, path), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Hugging Face has no public model '{source.Id}'{(source.Version is null ? string.Empty : " at version '" + source.Version + "'")} "
                + $"(HTTP {(int)response.StatusCode}): it does not exist, is private, or needs a Hugging Face token, which TechieRag does not take.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static void RefuseGated(JsonElement info, HuggingFaceModelSource source)
    {
        var gated = info.TryGetProperty("gated", out var value) && value.ValueKind is not (JsonValueKind.False or JsonValueKind.Null);
        var closed = info.TryGetProperty("private", out var isPrivate) && isPrivate.ValueKind == JsonValueKind.True;
        if (gated || closed)
        {
            throw new InvalidOperationException(
                $"The Hugging Face model '{source.Repository}' is {(gated ? "gated" : "private")}: each user must sign in to Hugging Face and "
                + "accept its terms there, and TechieRag does not take Hugging Face tokens. Choose a model that is not gated, "
                + "or download its files yourself and use LocalModel.FromFolder.");
        }
    }

    private static string LicenceName(JsonElement info)
    {
        if (!info.TryGetProperty("cardData", out var card) || card.ValueKind != JsonValueKind.Object)
        {
            return "not stated on the model card";
        }

        var licence = card.TryGetProperty("license", out var value) ? value : default;
        var name = licence.ValueKind switch
        {
            JsonValueKind.String => licence.GetString(),
            JsonValueKind.Array => string.Join(", ", licence.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString())),
            _ => null
        };

        if (string.Equals(name, "other", StringComparison.OrdinalIgnoreCase) && Text(card, "license_name") is { Length: > 0 } custom)
        {
            return custom;
        }

        return string.IsNullOrWhiteSpace(name) ? "not stated on the model card" : name;
    }

    private Uri TermsUrl(JsonElement info, string repository, string commit)
    {
        var siblings = info.TryGetProperty("siblings", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(s => Text(s, "rfilename")).OfType<string>().ToHashSet(StringComparer.Ordinal)
            : [];
        if (LicenceFileNames.FirstOrDefault(siblings.Contains) is { } licenceFile)
        {
            return new Uri(BaseAddress, $"{repository}/blob/{commit}/{licenceFile}");
        }

        var link = info.TryGetProperty("cardData", out var card) && card.ValueKind == JsonValueKind.Object ? Text(card, "license_link") : null;
        if (Uri.TryCreate(link, UriKind.Absolute, out var absolute) && absolute.Scheme is "https" or "http")
        {
            return absolute;
        }

        if (link is not null && siblings.Contains(link))
        {
            return new Uri(BaseAddress, $"{repository}/blob/{commit}/{Uri.EscapeDataString(link).Replace("%2F", "/", StringComparison.Ordinal)}");
        }

        return new Uri(BaseAddress, $"{repository}/tree/{commit}");
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
