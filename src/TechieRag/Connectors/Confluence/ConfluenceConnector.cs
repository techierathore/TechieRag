using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Connectors.Http;
using TechieRag.Web;

namespace TechieRag.Connectors.Confluence;

/// <summary>
/// Ingests the pages of a Confluence space or page tree (REQ-RAG-020 / BRD-64).
/// </summary>
/// <remarks>
/// <para><b>Two shapes of the same walk.</b> A space listing is already recursive — the API returns
/// every page in the space at any depth — so it is a flat, cursor-paged loop. A page tree is not:
/// the API answers "children of X" one level at a time, so the connector runs a breadth-first walk,
/// paging each level and queueing the pages it finds. Breadth-first for the same reason the site
/// crawler is: with a run budget, depth-first spends all of it descending one branch.</para>
/// <para><b>Listing is not filtered by date, deliberately.</b> The API can be queried for pages
/// modified since a timestamp, and this connector does not use it. Listing costs one request per
/// twenty-five pages while fetching costs one request per page, so the saving is small — and a
/// listing that returns only changes is no longer a statement about what exists, which is what
/// sync-state pruning depends on (see <c>IDataConnector.ListsEntireSource</c>). Full listing plus
/// exact version comparison is cheaper to reason about and cannot go stale.</para>
/// <para><b>Version numbers are exact.</b> Every page carries a monotonic version, so an unchanged
/// page is provably unchanged and an incremental run fetches nothing it already has.</para>
/// <para><b>Attachments are out of scope here.</b> BRD-64 is the space and its pages. Page
/// attachments are a different ingestion path — binary documents that belong to the file processors,
/// not to a text connector — and pretending to cover them by ingesting their filenames would be
/// worse than not covering them.</para>
/// <para><b>One instance drives one run.</b> The page-tree walk keeps its pending-parents queue on
/// the instance, so a cursor is only meaningful to the connector that issued it.</para>
/// <para><b>Every URL this connector requests is pinned to the configured site.</b> The API states
/// its own paging links — <c>_links.next</c> and <c>_links.base</c> — inside the response body, and
/// this connector follows them because a Cloud site's real base is not always the URL the caller
/// typed. That makes the response an input that names the next request, and every request carries
/// the site's API token in an <c>Authorization</c> header. A body answering
/// <c>"next": "https://attacker.example/"</c> would therefore not merely misroute the walk, it would
/// hand the caller's credential to whoever wrote it. <see cref="RequireSameOrigin"/> refuses any
/// such link, so a hostile or compromised response can redirect nothing.</para>
/// </remarks>
public sealed class ConfluenceConnector : IDataConnector
{
    private readonly IConnectorTransport transport;
    private readonly ConfluenceConnectorOptions options;
    private readonly ILogger<ConfluenceConnector> logger;
    private readonly Queue<string> pendingParents = new();
    private bool startedTreeWalk;

    /// <summary>Initializes a new instance of the <see cref="ConfluenceConnector"/> class.</summary>
    /// <param name="transport">Network seam. Wrap it in <see cref="RateLimitedTransport"/> for a real site.</param>
    /// <param name="options">What to ingest, and the credential to ingest it with.</param>
    /// <param name="logger">Diagnostics. Never receives the token.</param>
    /// <exception cref="ArgumentException">Neither or both of space key and root page id were given.</exception>
    public ConfluenceConnector(
        IConnectorTransport transport,
        ConfluenceConnectorOptions options,
        ILogger<ConfluenceConnector>? logger = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? NullLogger<ConfluenceConnector>.Instance;

        var hasSpace = !string.IsNullOrWhiteSpace(options.SpaceKey);
        var hasRoot = !string.IsNullOrWhiteSpace(options.RootPageId);

        if (hasSpace == hasRoot)
        {
            throw new ArgumentException(
                "A Confluence connector needs exactly one of SpaceKey or RootPageId — a space is a different walk from a page tree.",
                nameof(options));
        }
    }

    /// <inheritdoc />
    public string SourceType => "confluence";

    /// <inheritdoc />
    public string SourceName =>
        string.IsNullOrWhiteSpace(options.SpaceKey)
            ? $"{Host()} page {options.RootPageId}"
            : $"{Host()} space {options.SpaceKey}";

    /// <inheritdoc />
    public async Task<ConnectorPage> ListAsync(
        ConnectorListRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var url = request.Cursor ?? FirstUrl();
        if (url is null)
        {
            return ConnectorPage.Empty;
        }

        // A cursor is a URL this connector previously read out of a response body. It is re-checked
        // here rather than only where it was produced, because this is the last point before the
        // credential is attached to it.
        RequireSameOrigin(url, "paging cursor");

        var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);
        EnsureListable(response);

        using var json = Parse(response.Body);
        var items = new List<ConnectorItem>();
        var isTreeWalk = !string.IsNullOrWhiteSpace(options.RootPageId);

        foreach (var result in ReadResults(json.RootElement))
        {
            var item = ReadItem(result, json.RootElement);
            if (item is null)
            {
                continue;
            }

            items.Add(item);

            // A page found in a tree walk is also a parent to visit. Queueing it here rather than
            // recursing keeps the walk breadth-first and keeps the run budget spent near the root.
            if (isTreeWalk && options.IncludeChildPages)
            {
                pendingParents.Enqueue(item.Id);
            }
        }

        logger.LogDebug("{Source} listed {Count} page(s)", SourceName, items.Count);
        return new ConnectorPage(items, NextCursor(json.RootElement, isTreeWalk));
    }

    /// <inheritdoc />
    public async Task<ConnectorDocument> FetchAsync(
        ConnectorItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var url = $"{options.ResolveBaseUrl()}/rest/api/content/{Uri.EscapeDataString(item.Id)}?expand=body.storage";
        var response = await SendAsync(url, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            // A credential rejection is about the whole site, so it ends the run. A 404 is one page
            // deleted between listing and fetching and costs only that page.
            ThrowIfCredentialFailure(response, item.Name);
            throw new InvalidOperationException(
                $"'{item.Name}' could not be read: the site replied {response.StatusCode}.");
        }

        using var json = Parse(response.Body);
        var storage = ReadStorageBody(json.RootElement);

        // Storage format is XHTML with Confluence's own macro elements mixed in. Running it through
        // the same reader the crawler uses strips markup and macro scaffolding and leaves the prose,
        // rather than embedding a page of angle brackets.
        var text = string.IsNullOrWhiteSpace(storage)
            ? string.Empty
            : WebPageReader.Read(storage, item.SourceUrl.Length == 0 ? options.ResolveBaseUrl() : item.SourceUrl).Text;

        return new ConnectorDocument(item, text);
    }

    private string? FirstUrl()
    {
        var baseUrl = options.ResolveBaseUrl();

        if (!string.IsNullOrWhiteSpace(options.SpaceKey))
        {
            return $"{baseUrl}/rest/api/content?type=page&spaceKey={Uri.EscapeDataString(options.SpaceKey)}"
                   + $"&limit={options.PageSize}&start=0&expand=version,space";
        }

        if (startedTreeWalk)
        {
            return null;
        }

        startedTreeWalk = true;

        // The root page is fetched as a listing of one so that it is ingested too. Starting at its
        // children would silently drop the page the user actually named.
        return $"{baseUrl}/rest/api/content/{Uri.EscapeDataString(options.RootPageId!)}?expand=version,space";
    }

    private string? NextCursor(JsonElement root, bool isTreeWalk)
    {
        var next = ReadNextLink(root);
        if (next is not null)
        {
            return next;
        }

        if (!isTreeWalk)
        {
            return null;
        }

        // This level is exhausted; descend to the next queued parent. Returning null here instead
        // would end the walk at whatever depth the last page happened to sit.
        while (pendingParents.Count > 0)
        {
            var parent = pendingParents.Dequeue();
            return $"{options.ResolveBaseUrl()}/rest/api/content/{Uri.EscapeDataString(parent)}/child/page"
                   + $"?limit={options.PageSize}&start=0&expand=version,space";
        }

        return null;
    }

    /// <summary>Reads the results array out of a listing, or wraps a single-page response as one.</summary>
    /// <param name="root">The parsed response body.</param>
    /// <returns>The page elements the response describes.</returns>
    /// <remarks>
    /// The API returns a collection for a listing and a bare object for a single page. Normalising
    /// here is what lets the root of a tree walk and the levels beneath it share one code path.
    /// </remarks>
    private static IEnumerable<JsonElement> ReadResults(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in results.EnumerateArray())
            {
                yield return result;
            }

            yield break;
        }

        if (root.TryGetProperty("id", out _))
        {
            yield return root;
        }
    }

    private ConnectorItem? ReadItem(JsonElement result, JsonElement root)
    {
        var id = ReadString(result, "id");
        if (id is null)
        {
            return null;
        }

        var title = ReadString(result, "title") ?? $"Page {id}";
        var metadata = new Dictionary<string, string> { ["PageId"] = id };

        if (result.TryGetProperty("space", out var space) && ReadString(space, "key") is { } key)
        {
            metadata["SpaceKey"] = key;
        }

        string? version = null;
        DateTimeOffset? modified = null;
        if (result.TryGetProperty("version", out var versionElement)
            && versionElement.ValueKind == JsonValueKind.Object)
        {
            if (versionElement.TryGetProperty("number", out var number) && number.TryGetInt64(out var value))
            {
                version = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (ReadString(versionElement, "when") is { } when
                && DateTimeOffset.TryParse(
                    when,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                modified = parsed;
            }
        }

        return new ConnectorItem(id, title, BuildWebUrl(result, root), version, modified, null, metadata);
    }

    /// <summary>Builds the human-facing URL for a page.</summary>
    /// <param name="result">The page element.</param>
    /// <param name="root">The response root, which carries the site's link base.</param>
    /// <returns>An absolute URL, or a constructed fallback when the response carries no link.</returns>
    /// <remarks>
    /// <para>The API reports page links relative to a base it states in the same response, and that
    /// base is not always the URL the connector was configured with — Cloud sites answer on a
    /// <c>/wiki</c> prefix that a caller may or may not have included. Using the response's own base
    /// is what keeps citations clickable in both cases.</para>
    /// <para>A link off the configured site is discarded in favour of a constructed one. No
    /// credential is at stake here — the connector never requests a citation URL — but this string is
    /// shown to a user as "where this answer came from", and a response that could set it to any
    /// address would turn every citation into a phishing link wearing the site's name.</para>
    /// </remarks>
    private string BuildWebUrl(JsonElement result, JsonElement root)
    {
        var linkBase = LinkBase(root);

        if (result.TryGetProperty("_links", out var links)
            && ReadString(links, "webui") is { } webui
            && !string.IsNullOrWhiteSpace(webui))
        {
            if (!webui.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return $"{linkBase.TrimEnd('/')}/{webui.TrimStart('/')}";
            }

            if (IsSameOrigin(webui))
            {
                return webui;
            }

            logger.LogWarning(
                "{Source} reported a page link outside the configured site; using a constructed link instead",
                SourceName);
        }

        var id = ReadString(result, "id");
        return id is null ? linkBase : $"{linkBase.TrimEnd('/')}/pages/viewpage.action?pageId={id}";
    }

    private string? ReadNextLink(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("_links", out var links)
            || ReadString(links, "next") is not { } next
            || string.IsNullOrWhiteSpace(next))
        {
            return null;
        }

        var absolute = next.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? next
            : $"{LinkBase(root).TrimEnd('/')}/{next.TrimStart('/')}";

        RequireSameOrigin(absolute, "paging link");
        return absolute;
    }

    /// <summary>Reads the link base the response states, ignoring one that points somewhere else.</summary>
    /// <param name="root">The response root.</param>
    /// <returns>The site's own link base, or the configured base URL.</returns>
    /// <remarks>
    /// The base is used to turn the API's relative links into absolute ones, so a response that
    /// stated a foreign base would redirect the whole walk — and the citation URLs with it — without
    /// ever naming a foreign link. Falling back to the configured base is always safe.
    /// </remarks>
    private string LinkBase(JsonElement root)
    {
        var stated = ReadLinkBase(root);
        return stated is not null && IsSameOrigin(stated) ? stated : options.ResolveBaseUrl();
    }

    private static string? ReadLinkBase(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("_links", out var links)
            ? ReadString(links, "base")
            : null;

    /// <summary>Refuses a URL that does not belong to the configured site.</summary>
    /// <param name="url">The URL about to be requested with the site's credential attached.</param>
    /// <param name="what">What produced the URL, for the failure message.</param>
    /// <exception cref="ConnectorException">The URL is on a different scheme, host or port.</exception>
    private void RequireSameOrigin(string url, string what)
    {
        if (IsSameOrigin(url))
        {
            return;
        }

        // The offending URL is named because an operator needs to know their site answered with it;
        // the credential that would have been sent to it is not, and never appears in a message.
        throw new ConnectorException(
            SourceType,
            $"The site's {what} pointed at '{url}', which is not on the configured site "
            + $"'{options.ResolveBaseUrl()}'. Refused rather than sending the API token to another host.");
    }

    /// <summary>Determines whether a URL is on the same scheme, host and port as the configured site.</summary>
    /// <param name="url">The candidate URL.</param>
    /// <returns>True when the URL belongs to the configured site.</returns>
    /// <remarks>
    /// Scheme is part of the comparison: an https site whose response links to http for the same
    /// host is asking for the token to be sent in clear text, which is a downgrade to refuse and not
    /// a detail to normalise away.
    /// </remarks>
    private bool IsSameOrigin(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var candidate)
            || !Uri.TryCreate(options.ResolveBaseUrl(), UriKind.Absolute, out var site))
        {
            return false;
        }

        return string.Equals(candidate.Scheme, site.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Host, site.Host, StringComparison.OrdinalIgnoreCase)
            && candidate.Port == site.Port;
    }

    private static string? ReadStorageBody(JsonElement root) =>
        root.TryGetProperty("body", out var body)
        && body.TryGetProperty("storage", out var storage)
            ? ReadString(storage, "value")
            : null;

    private Task<ConnectorHttpResponse> SendAsync(string url, CancellationToken cancellationToken) =>
        transport.GetAsync(new ConnectorHttpRequest(url, BuildHeaders()), cancellationToken);

    /// <summary>Builds the request headers, including authorization.</summary>
    /// <returns>Headers for one request.</returns>
    /// <remarks>
    /// Cloud pairs an account email with an API token over HTTP basic; Server and Data Center issue
    /// a personal access token used as a bearer token. Which one is meant is inferred from whether
    /// an email was supplied, because asking the caller to also name the deployment type would be a
    /// setting whose only correct value is already implied by the other two.
    /// </remarks>
    private Dictionary<string, string> BuildHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "application/json",
        };

        if (string.IsNullOrWhiteSpace(options.ApiToken))
        {
            return headers;
        }

        if (string.IsNullOrWhiteSpace(options.UserEmail))
        {
            headers["Authorization"] = $"Bearer {options.ApiToken}";
            return headers;
        }

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.UserEmail}:{options.ApiToken}"));
        headers["Authorization"] = $"Basic {basic}";
        return headers;
    }

    private void EnsureListable(ConnectorHttpResponse response)
    {
        if (response.IsSuccess)
        {
            return;
        }

        ThrowIfCredentialFailure(response, SourceName);

        if (response.StatusCode == 404)
        {
            throw new ConnectorException(
                SourceType,
                $"{SourceName} does not exist, or the account cannot see it.",
                404);
        }

        throw new ConnectorException(
            SourceType,
            $"{SourceName} could not be listed: the site replied {response.StatusCode}.",
            response.StatusCode);
    }

    private void ThrowIfCredentialFailure(ConnectorHttpResponse response, string what)
    {
        if (response.StatusCode is not (401 or 403))
        {
            return;
        }

        // The token is never named, quoted or partially printed — only the fact that it was refused.
        throw new ConnectorException(
            SourceType,
            options.ApiToken is null
                ? $"{Host()} refused anonymous access to '{what}' ({response.StatusCode}). Supply an API token."
                : $"{Host()} rejected the supplied API token for '{what}' ({response.StatusCode}). Check that it is current and that the account can read the space.",
            response.StatusCode);
    }

    private string Host() =>
        Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : "Confluence";

    private JsonDocument Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new ConnectorException(
                SourceType,
                $"{Host()} returned a response that is not JSON. The base URL may be missing the site's API prefix.",
                ex);
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
