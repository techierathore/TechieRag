using TechieRag.Abstractions;

namespace TechieRag.Web;

/// <summary>
/// Ingests web content into a TechieRag instance (REQ-RAG-016/017/018, BRD-60/61/62).
/// </summary>
/// <remarks>
/// Extension methods over <see cref="ITechieRag"/> rather than new members on it, deliberately: the
/// core interface is implemented by consumers and shipped in a published package, so adding members
/// is a breaking change for every implementer. Web ingestion is also strictly a composition of
/// fetch + <c>IngestTextAsync</c> — it introduces no new storage or embedding behaviour — so it has
/// no claim on the core contract.
/// </remarks>
public static class WebIngestionExtensions
{
    /// <summary>Fetches one page and ingests its readable text (REQ-RAG-016 / BRD-60).</summary>
    /// <param name="rag">The RAG instance.</param>
    /// <param name="url">Absolute http/https URL.</param>
    /// <param name="fetcher">Page fetcher.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ingested document's id.</returns>
    /// <exception cref="WebFetchException">The page could not be fetched, or held no readable text.</exception>
    public static async Task<string> IngestUrlAsync(
        this ITechieRag rag,
        string url,
        IWebContentFetcher fetcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rag);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentException.ThrowIfNullOrEmpty(url);

        var page = await fetcher.FetchAsync(url, cancellationToken).ConfigureAwait(false);
        return await IngestPageAsync(rag, page, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Crawls a site and ingests every page reached (REQ-RAG-017 / BRD-61).</summary>
    /// <param name="rag">The RAG instance.</param>
    /// <param name="seedUrl">Absolute http/https URL to start from.</param>
    /// <param name="fetcher">Page fetcher.</param>
    /// <param name="options">Crawl bounds; defaults are conservative.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was ingested, what was skipped, and why.</returns>
    public static async Task<WebIngestionResult> IngestSiteAsync(
        this ITechieRag rag,
        string seedUrl,
        IWebContentFetcher fetcher,
        WebCrawlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rag);
        ArgumentNullException.ThrowIfNull(fetcher);

        var crawl = await new SiteCrawler(fetcher)
            .CrawlAsync(seedUrl, options, cancellationToken)
            .ConfigureAwait(false);

        var ingested = new List<string>();
        var skipped = new List<CrawlFailure>(crawl.Failures);

        foreach (var page in crawl.Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A page whose text is empty after chrome removal is a navigation shell or an image
            // gallery. Ingesting it would add an empty document that can never be retrieved, and
            // would make the ingested count a lie about what is searchable.
            if (string.IsNullOrWhiteSpace(page.Text))
            {
                skipped.Add(new CrawlFailure(page.FinalUrl, "The page had no readable text."));
                continue;
            }

            ingested.Add(await IngestPageAsync(rag, page, cancellationToken).ConfigureAwait(false));
        }

        return new WebIngestionResult(ingested, skipped);
    }

    /// <summary>Ingests a YouTube video's transcript (REQ-RAG-018 / BRD-62).</summary>
    /// <param name="rag">The RAG instance.</param>
    /// <param name="urlOrVideoId">A YouTube URL in any recognised shape, or a bare video id.</param>
    /// <param name="reader">Transcript reader.</param>
    /// <param name="preferredLanguage">BCP-47 prefix to prefer, e.g. "en".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ingested document's id.</returns>
    public static async Task<string> IngestYouTubeAsync(
        this ITechieRag rag,
        string urlOrVideoId,
        YouTubeTranscriptReader reader,
        string? preferredLanguage = "en",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rag);
        ArgumentNullException.ThrowIfNull(reader);

        var transcript = await reader
            .ReadAsync(urlOrVideoId, preferredLanguage, cancellationToken)
            .ConfigureAwait(false);

        return await rag.IngestTextAsync(
            transcript.Text,
            transcript.Title,
            new Dictionary<string, object>
            {
                ["SourceUrl"] = transcript.Url,
                ["SourcePath"] = transcript.Url,
                ["SourceType"] = "youtube",
                ["VideoId"] = transcript.VideoId,
                ["IngestedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the source URL of a document that was ingested from the web (REQ-RAG-016/017/018).
    /// </summary>
    /// <param name="document">A document from <see cref="ITechieRag.ListDocumentsAsync"/>.</param>
    /// <returns>Where the document was read from, or an empty string when it did not come from the web.</returns>
    /// <remarks>
    /// <para><b>Why this is not just a dictionary lookup.</b> Web ingestion records the URL under
    /// <c>SourceUrl</c> in the document's metadata, and for a caller holding the object it just
    /// ingested that is where it is. But the metadata does not necessarily survive a round trip
    /// through a vector store: <c>SqliteVecStore</c> — the desktop application's default — writes the
    /// document row's <c>Metadata</c> column as a literal <c>{}</c> and keeps per-document metadata
    /// only on the chunks. Listing the catalogue therefore returns documents whose <c>Metadata</c> is
    /// empty, and a caller that only reads <c>SourceUrl</c> shows a blank source for every web
    /// document it ever ingested.</para>
    /// <para><c>SourcePath</c> IS lifted onto the document row, so web ingestion writes the URL to
    /// both and this method reads whichever survived. The duplication is deliberate and is the
    /// difference between the Documents screen showing a link and showing an empty cell.</para>
    /// </remarks>
    public static string WebSourceUrl(this Models.Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Metadata.TryGetValue("SourceUrl", out var value)
            && value?.ToString() is { Length: > 0 } fromMetadata)
        {
            return fromMetadata;
        }

        // "text-input" is the placeholder every text ingestion starts with, and an absolute URL is
        // the only thing worth reporting as a source. Anything else is a file path from a different
        // ingestion route and is not this method's business.
        return Uri.TryCreate(document.SourcePath, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? document.SourcePath
            : string.Empty;
    }

    private static Task<string> IngestPageAsync(
        ITechieRag rag,
        WebPage page,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(page.Text))
        {
            throw new WebFetchException(
                page.RequestedUrl,
                $"{page.RequestedUrl} contains no readable text. It may be an image, a video, or rendered entirely by script.");
        }

        return rag.IngestTextAsync(
            page.Text,
            page.Title,
            new Dictionary<string, object>
            {
                // The FINAL url is recorded, not the requested one: a citation must point at the
                // document that was actually read, not at a redirect that no longer serves it.
                ["SourceUrl"] = page.FinalUrl,
                // SourcePath carries the same value on purpose. It is the only field the vector
                // stores lift out of chunk metadata onto the document row, so it is the only way a
                // caller reading the catalogue can learn where a document came from — see
                // WebSourceUrl for the whole reason this duplication exists.
                ["SourcePath"] = page.FinalUrl,
                ["RequestedUrl"] = page.RequestedUrl,
                ["SourceType"] = "web",
                ["IngestedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            },
            cancellationToken);
    }
}

/// <summary>What a site ingestion did (REQ-RAG-017).</summary>
/// <param name="DocumentIds">Ids of the documents ingested.</param>
/// <param name="Skipped">Pages not ingested, each with a reason. Never silently dropped.</param>
public sealed record WebIngestionResult(
    IReadOnlyList<string> DocumentIds,
    IReadOnlyList<CrawlFailure> Skipped);
