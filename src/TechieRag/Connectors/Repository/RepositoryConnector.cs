using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Connectors.Http;

namespace TechieRag.Connectors.Repository;

/// <summary>
/// Ingests the text files of a hosted repository on a chosen branch (REQ-RAG-019 / BRD-63).
/// </summary>
/// <remarks>
/// <para><b>One connector, two APIs.</b> GitHub and GitLab differ in project addressing, tree
/// paging, blob retrieval and auth header, and in nothing else that matters here — so the shared
/// 90% (branch resolution, glob filtering, binary rejection, incremental sync by blob hash) is
/// written once and the differences are four small switches. Two near-identical connectors would
/// have meant fixing every filtering bug twice.</para>
/// <para><b>Filtering happens during listing, not after fetching.</b> A path excluded by a glob is
/// never fetched, never counted, and never enters the sync state. Filtering after the fetch would
/// download the whole repository to throw most of it away — on a large monorepo that is the
/// difference between a run that finishes and one that exhausts the hourly rate limit.</para>
/// <para><b>Incremental sync is exact here.</b> Both hosts return a content hash for every blob, so
/// an unchanged file is provably unchanged — no timestamp guessing. A rebase that rewrites every
/// commit date re-ingests nothing.</para>
/// <para><b>Binary files are refused with a reason.</b> A PNG or a compiled artefact decoded as text
/// produces a page of replacement characters that embeds into noise and pollutes every later search.
/// Content with a NUL byte in it is reported as a per-item failure rather than ingested.</para>
/// </remarks>
public sealed class RepositoryConnector : IDataConnector
{
    private readonly IConnectorTransport transport;
    private readonly RepositoryConnectorOptions options;
    private readonly GlobFilter filter;
    private readonly ILogger<RepositoryConnector> logger;
    private string? resolvedBranch;

    /// <summary>Initializes a new instance of the <see cref="RepositoryConnector"/> class.</summary>
    /// <param name="transport">Network seam. Wrap it in <see cref="RateLimitedTransport"/> for real hosts.</param>
    /// <param name="options">What to ingest, and the credential to ingest it with.</param>
    /// <param name="logger">Diagnostics. Never receives the token.</param>
    /// <exception cref="ArgumentException"><see cref="RepositoryConnectorOptions.ProjectPath"/> is empty.</exception>
    public RepositoryConnector(
        IConnectorTransport transport,
        RepositoryConnectorOptions options,
        ILogger<RepositoryConnector>? logger = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? NullLogger<RepositoryConnector>.Instance;

        if (string.IsNullOrWhiteSpace(options.ProjectPath))
        {
            throw new ArgumentException("A repository connector needs a ProjectPath, e.g. 'owner/repo'.", nameof(options));
        }

        filter = new GlobFilter(options.IncludeGlobs, options.ExcludeGlobs);
        resolvedBranch = string.IsNullOrWhiteSpace(options.Branch) ? null : options.Branch;
    }

    /// <inheritdoc />
    public string SourceType => "repository";

    /// <inheritdoc />
    public string SourceName => $"{options.ProjectPath}@{resolvedBranch ?? "(default branch)"}";

    /// <inheritdoc />
    public async Task<ConnectorPage> ListAsync(
        ConnectorListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var branch = await ResolveBranchAsync(cancellationToken).ConfigureAwait(false);

        return options.Host == RepositoryHost.GitHub
            ? await ListGitHubAsync(branch, cancellationToken).ConfigureAwait(false)
            : await ListGitLabAsync(branch, request.Cursor, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ConnectorDocument> FetchAsync(
        ConnectorItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var branch = await ResolveBranchAsync(cancellationToken).ConfigureAwait(false);
        var url = options.Host == RepositoryHost.GitHub
            ? $"{options.ResolveApiBaseUrl()}/repos/{options.ProjectPath}/git/blobs/{item.Version}"
            : $"{options.ResolveApiBaseUrl()}/projects/{EncodedProject()}/repository/files/{Uri.EscapeDataString(item.Id)}?ref={Uri.EscapeDataString(branch)}";

        var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            // 401/403 mean the credential is wrong for the whole repository, so the run is over.
            // Everything else — a 404 for a file deleted between listing and fetching, a transient
            // 500 — costs this one file and nothing more.
            ThrowIfCredentialFailure(response, item.Name);
            throw new InvalidOperationException(
                $"{item.Name} could not be read: the host replied {response.StatusCode}.");
        }

        return new ConnectorDocument(item, DecodeContent(response.Body, item.Name));
    }

    private async Task<ConnectorPage> ListGitHubAsync(string branch, CancellationToken cancellationToken)
    {
        // The recursive tree is one request for the entire repository, which is why this host needs
        // no paging at all. The cost is the host's own response cap, surfaced below as `truncated`.
        var url = $"{options.ResolveApiBaseUrl()}/repos/{options.ProjectPath}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1";
        var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        EnsureListable(response, branch);

        using var json = Parse(response.Body, "tree listing");
        var items = new List<ConnectorItem>();
        var failures = new List<ConnectorItemFailure>();

        if (json.RootElement.TryGetProperty("tree", out var tree) && tree.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in tree.EnumerateArray())
            {
                if (ReadString(entry, "type") != "blob")
                {
                    continue;
                }

                var path = ReadString(entry, "path");
                if (path is null || !filter.IsMatch(path))
                {
                    continue;
                }

                items.Add(new ConnectorItem(
                    path,
                    path,
                    $"{options.ResolveWebBaseUrl()}/{options.ProjectPath}/blob/{branch}/{path}",
                    ReadString(entry, "sha"),
                    null,
                    entry.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : null,
                    new Dictionary<string, string> { ["Branch"] = branch, ["Path"] = path }));
            }
        }

        // Truncation is reported, never swallowed. A repository whose tree exceeds the host's
        // single-response limit would otherwise ingest an arbitrary prefix of itself and look
        // complete, which is the worst possible outcome for a search index.
        if (json.RootElement.TryGetProperty("truncated", out var truncated)
            && truncated.ValueKind == JsonValueKind.True)
        {
            failures.Add(new ConnectorItemFailure(
                options.ProjectPath,
                SourceName,
                "The host truncated the repository tree: this run saw only part of the repository. Narrow the include globs or ingest sub-paths separately."));
        }

        logger.LogInformation("{Source} listed {Count} matching file(s)", SourceName, items.Count);
        return new ConnectorPage(items, null, failures);
    }

    private async Task<ConnectorPage> ListGitLabAsync(
        string branch,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var page = cursor ?? "1";
        var url = $"{options.ResolveApiBaseUrl()}/projects/{EncodedProject()}/repository/tree"
                  + $"?ref={Uri.EscapeDataString(branch)}&recursive=true&per_page={options.PageSize}&page={page}";

        var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        EnsureListable(response, branch);

        using var json = Parse(response.Body, "tree listing");
        var items = new List<ConnectorItem>();

        if (json.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                if (ReadString(entry, "type") != "blob")
                {
                    continue;
                }

                var path = ReadString(entry, "path");
                if (path is null || !filter.IsMatch(path))
                {
                    continue;
                }

                // This host's tree carries no size, so an oversized file is only caught after it is
                // fetched. Nothing can be done about that from here; the runner's byte cap still
                // applies to what it does return.
                items.Add(new ConnectorItem(
                    path,
                    path,
                    $"{options.ResolveWebBaseUrl()}/{options.ProjectPath}/-/blob/{branch}/{path}",
                    ReadString(entry, "id"),
                    null,
                    null,
                    new Dictionary<string, string> { ["Branch"] = branch, ["Path"] = path }));
            }
        }

        // The host paginates its tree and states the next page in a header. Trusting the header
        // rather than counting results is what keeps paging correct when a page is filtered down to
        // zero matching files — a count-based loop would stop there and miss the rest of the repo.
        var next = response.Header("X-Next-Page");
        return new ConnectorPage(items, string.IsNullOrWhiteSpace(next) ? null : next.Trim());
    }

    private async Task<string> ResolveBranchAsync(CancellationToken cancellationToken)
    {
        if (resolvedBranch is not null)
        {
            return resolvedBranch;
        }

        var url = options.Host == RepositoryHost.GitHub
            ? $"{options.ResolveApiBaseUrl()}/repos/{options.ProjectPath}"
            : $"{options.ResolveApiBaseUrl()}/projects/{EncodedProject()}";

        var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            ThrowIfCredentialFailure(response, options.ProjectPath);
            throw new ConnectorException(
                SourceType,
                $"'{options.ProjectPath}' could not be read on {HostName()}: the host replied {response.StatusCode}.",
                response.StatusCode);
        }

        using var json = Parse(response.Body, "project metadata");
        var branch = ReadString(json.RootElement, "default_branch");

        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new ConnectorException(
                SourceType,
                $"'{options.ProjectPath}' did not report a default branch. Set Branch explicitly.");
        }

        resolvedBranch = branch;
        return branch;
    }

    private Task<ConnectorHttpResponse> SendAsync(string url, CancellationToken cancellationToken) =>
        transport.GetAsync(new ConnectorHttpRequest(url, BuildHeaders()), cancellationToken);

    private Dictionary<string, string> BuildHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "application/json",
        };

        if (options.Host == RepositoryHost.GitHub)
        {
            headers["Accept"] = "application/vnd.github+json";
            headers["X-GitHub-Api-Version"] = "2022-11-28";

            if (!string.IsNullOrWhiteSpace(options.AccessToken))
            {
                headers["Authorization"] = $"Bearer {options.AccessToken}";
            }

            return headers;
        }

        if (!string.IsNullOrWhiteSpace(options.AccessToken))
        {
            // A header, not the ?private_token= query parameter this API also accepts. Query strings
            // are logged by proxies and pasted into bug reports; headers are not.
            headers["PRIVATE-TOKEN"] = options.AccessToken;
        }

        return headers;
    }

    private void EnsureListable(ConnectorHttpResponse response, string branch)
    {
        if (response.IsSuccess)
        {
            return;
        }

        ThrowIfCredentialFailure(response, options.ProjectPath);

        if (response.StatusCode == 404)
        {
            throw new ConnectorException(
                SourceType,
                $"'{options.ProjectPath}' has no branch '{branch}' on {HostName()}, or the repository does not exist.",
                404);
        }

        throw new ConnectorException(
            SourceType,
            $"'{options.ProjectPath}' could not be listed: the host replied {response.StatusCode}.",
            response.StatusCode);
    }

    private void ThrowIfCredentialFailure(ConnectorHttpResponse response, string what)
    {
        if (response.StatusCode is not (401 or 403))
        {
            return;
        }

        // The token itself is never named, quoted, or partially printed — only the fact that the
        // host rejected whatever was sent.
        throw new ConnectorException(
            SourceType,
            options.AccessToken is null
                ? $"{HostName()} refused anonymous access to '{what}' ({response.StatusCode}). Supply an access token."
                : $"{HostName()} rejected the supplied access token for '{what}' ({response.StatusCode}). Check that it is current and has read scope.",
            response.StatusCode);
    }

    private string HostName() => options.Host == RepositoryHost.GitHub ? "GitHub" : "GitLab";

    private string EncodedProject() => Uri.EscapeDataString(options.ProjectPath);

    private JsonDocument Parse(string body, string what)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException(
                SourceType,
                $"{HostName()} returned a {what} response that is not JSON. The API base URL may be wrong.",
                ex);
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Decodes a blob response into text.</summary>
    /// <param name="body">The JSON body of a blob or file response.</param>
    /// <param name="name">The file's path, for the failure message.</param>
    /// <returns>The file's text.</returns>
    /// <exception cref="InvalidDataException">The file is binary, or its encoding is not one this connector understands.</exception>
    /// <remarks>
    /// Both hosts wrap file content in JSON as base64 rather than serving it raw from these
    /// endpoints. Base64 in JSON is used in preference to each host's raw endpoint because the raw
    /// endpoints differ in redirect behaviour and content-type handling, while this shape is
    /// identical on both and survives any byte sequence intact.
    /// </remarks>
    internal static string DecodeContent(string body, string name)
    {
        using var json = JsonDocument.Parse(body);
        var encoding = ReadString(json.RootElement, "encoding");
        var content = ReadString(json.RootElement, "content");

        if (content is null)
        {
            throw new InvalidDataException($"{name} came back without any content.");
        }

        if (!string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            // Both hosts document base64 for these endpoints. An unexpected encoding is a real
            // change in the API, and guessing would put mangled text into the index silently.
            throw new InvalidDataException(
                $"{name} came back {encoding ?? "with no"} encoding, which this connector cannot decode.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(content.Replace("\n", string.Empty).Replace("\r", string.Empty));
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"{name} came back with malformed base64 content.", ex);
        }

        if (IsBinary(bytes))
        {
            throw new InvalidDataException(
                $"{name} looks like a binary file. Exclude it with a glob if this is expected.");
        }

        var text = new UTF8Encoding(false).GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    /// <summary>Determines whether a byte sequence is binary rather than text.</summary>
    /// <param name="bytes">The decoded file content.</param>
    /// <returns>True when the content contains a NUL byte near its start.</returns>
    /// <remarks>
    /// A NUL byte in the first 8 KB is the same heuristic diff tools use, and it is right for the
    /// case that matters: no text file in a repository contains one, and every compiled artefact,
    /// image and archive does.
    /// </remarks>
    internal static bool IsBinary(byte[] bytes)
    {
        var limit = Math.Min(bytes.Length, 8192);
        for (var index = 0; index < limit; index++)
        {
            if (bytes[index] == 0)
            {
                return true;
            }
        }

        return false;
    }
}
