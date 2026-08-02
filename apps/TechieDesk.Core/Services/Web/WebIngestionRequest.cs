using TechieRag.Web;

namespace TechieDesk.Services.Web;

/// <summary>
/// The kind of web source an "Add from web" request names (REQ-RAG-016/017/018).
/// </summary>
public enum WebIngestionSource
{
    /// <summary>One page, fetched and ingested as a single document (REQ-RAG-016 / BRD-60).</summary>
    Page,

    /// <summary>A site, crawled within explicit depth and page bounds (REQ-RAG-017 / BRD-61).</summary>
    Site,

    /// <summary>A YouTube video's transcript (REQ-RAG-018 / BRD-62).</summary>
    Video,
}

/// <summary>
/// One "Add from web" request exactly as the screen collected it (REQ-RAG-016/017/018).
/// </summary>
/// <remarks>
/// A request object rather than a long parameter list because the screen and the service must agree
/// on the SAME rules: <see cref="Validate"/> is what the form shows inline before anything is
/// fetched, and it is also what the service re-checks before it opens a socket. One definition, two
/// call sites, no chance of the button being enabled for a request the service would refuse.
/// </remarks>
public sealed class WebIngestionRequest
{
    /// <summary>The deepest crawl this surface will accept.</summary>
    /// <remarks>
    /// Not a library limit — the library takes any depth. It is a limit on what one click may set in
    /// motion: link counts grow exponentially with depth, so depth 6 on a normal site is a request
    /// the operator did not knowingly make.
    /// </remarks>
    public const int MaxAllowedDepth = 5;

    /// <summary>The largest page budget this surface will accept.</summary>
    public const int MaxAllowedPages = 200;

    /// <summary>Gets the kind of source being ingested.</summary>
    public required WebIngestionSource Source { get; init; }

    /// <summary>Gets the workspace the resulting documents are added to.</summary>
    public required string WorkspaceId { get; init; }

    /// <summary>Gets the URL that was typed, untrimmed.</summary>
    public required string Url { get; init; }

    /// <summary>Gets a value indicating whether ingested documents are pinned into workspace context.</summary>
    public bool Pinned { get; init; }

    /// <summary>Gets how many links deep a crawl follows. 0 fetches only the seed page.</summary>
    public int MaxDepth { get; init; } = 1;

    /// <summary>Gets the hard cap on pages fetched by a crawl, including the seed.</summary>
    public int MaxPages { get; init; } = 25;

    /// <summary>Gets a value indicating whether a crawl stays on the seed URL's host.</summary>
    public bool SameHostOnly { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether private, loopback and link-local targets are permitted.
    /// </summary>
    /// <remarks>
    /// Phrased as ALLOW rather than BLOCK on purpose. <see cref="WebCrawlOptions.BlockPrivateNetworkTargets"/>
    /// defaults to <c>true</c>, and the default of a C# <c>bool</c> is <c>false</c>; a property named
    /// <c>BlockPrivateNetworkTargets</c> here would therefore be OFF for every request that forgot to
    /// set it, quietly turning the SSRF guard off by omission. Inverted, the forgetful default is the
    /// safe one and switching it on is something a caller has to write down.
    /// </remarks>
    public bool AllowPrivateNetworkTargets { get; init; }

    /// <summary>Gets the BCP-47 language prefix preferred for a video transcript.</summary>
    public string PreferredLanguage { get; init; } = "en";

    /// <summary>Gets the URL with surrounding whitespace removed.</summary>
    public string TrimmedUrl => Url?.Trim() ?? string.Empty;

    /// <summary>
    /// Checks the request against everything that can be known without touching the network.
    /// </summary>
    /// <returns>An operator-facing reason the request cannot run, or null when it can.</returns>
    public string? Validate()
    {
        var url = TrimmedUrl;
        if (string.IsNullOrEmpty(url))
        {
            return "Enter a URL first.";
        }

        if (string.IsNullOrWhiteSpace(WorkspaceId))
        {
            return "No workspace is selected, so there is nowhere to put the result.";
        }

        if (Source == WebIngestionSource.Video)
        {
            // A YouTube URL is not judged by scheme: bare ids and youtu.be short links are both
            // legitimate things to paste, and YouTubeUrl is the one place that knows the shapes.
            return YouTubeUrl.IsYouTube(url)
                ? null
                : $"'{url}' is not a YouTube video URL. Paste a watch, youtu.be, shorts or embed link.";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return $"'{url}' is not an absolute http or https URL. Include the scheme, e.g. https://example.com.";
        }

        // The library refuses a private seed too, but it does so after the request is already in
        // flight and with no idea that a toggle exists. Saying it here names the toggle.
        if (!AllowPrivateNetworkTargets && WebCrawlOptions.IsPrivateNetworkHost(uri.Host))
        {
            return $"'{uri.Host}' is a private-network address. Turn on 'Allow intranet and private addresses' "
                   + "if you really mean to read from your own network.";
        }

        if (Source != WebIngestionSource.Site)
        {
            return null;
        }

        if (MaxDepth < 0 || MaxDepth > MaxAllowedDepth)
        {
            return $"Crawl depth must be between 0 and {MaxAllowedDepth}.";
        }

        return MaxPages is < 1 or > MaxAllowedPages
            ? $"The page limit must be between 1 and {MaxAllowedPages}."
            : null;
    }

    /// <summary>Builds the library crawl bounds this request describes.</summary>
    /// <returns>Crawl options carrying the depth, budget, host policy and SSRF guard.</returns>
    public WebCrawlOptions ToCrawlOptions() => new()
    {
        MaxDepth = MaxDepth,
        MaxPages = MaxPages,
        SameHostOnly = SameHostOnly,
        BlockPrivateNetworkTargets = !AllowPrivateNetworkTargets,
    };

    /// <summary>Gets the number of fetches this request can make, for progress arithmetic.</summary>
    /// <returns>The page budget for a crawl; 1 for anything else.</returns>
    public int ExpectedFetchCount() => Source == WebIngestionSource.Site ? Math.Max(1, MaxPages) : 1;
}
