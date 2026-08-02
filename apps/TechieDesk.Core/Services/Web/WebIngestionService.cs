using TechieDesk.Services.Localization;
using TechieRag;
using TechieRag.Web;

namespace TechieDesk.Services.Web;

/// <summary>
/// Ingests web content into a workspace (REQ-RAG-016/017/018, BRD-60/61/62).
/// </summary>
public interface IWebIngestionService
{
    /// <summary>Runs one "Add from web" request to completion.</summary>
    /// <param name="request">What to ingest and under what bounds.</param>
    /// <param name="progress">Live progress for the screen; null when nobody is watching.</param>
    /// <param name="cancellationToken">Cancellation token; cancelling keeps what was already ingested.</param>
    /// <returns>What was ingested, and everything that was not, with reasons.</returns>
    /// <exception cref="WebFetchException">The request itself is not usable — see <see cref="WebIngestionRequest.Validate"/>.</exception>
    Task<WebIngestionOutcome> IngestAsync(
        WebIngestionRequest request,
        IProgress<WebIngestionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Composes the library's web ingestion with workspace membership (REQ-RAG-016/017/018).
/// </summary>
/// <remarks>
/// Every fetching, crawling, parsing and transcript rule lives in <c>TechieRag.Web</c> and none of
/// it is repeated here. What this class adds is the two things the library deliberately does not
/// know about: which workspace the result belongs to, and a screen that needs to be told what is
/// happening while it happens.
/// </remarks>
public sealed class WebIngestionService : IWebIngestionService
{
    private readonly ITechieRag rag;
    private readonly IWebContentFetcherFactory fetcherFactory;
    private readonly IWorkspaceDocumentLinker linker;
    private readonly YouTubeTranscriptReader transcriptReader;
    private readonly LocalizeText localize;
    private readonly ILogger<WebIngestionService> logger;

    /// <summary>Initializes a new instance of the <see cref="WebIngestionService"/> class.</summary>
    /// <param name="rag">The RAG instance the library extensions ingest into.</param>
    /// <param name="fetcherFactory">Creates a fetcher carrying this run's private-network policy.</param>
    /// <param name="linker">Adds the ingested documents to the workspace.</param>
    /// <param name="transcriptReader">Reads YouTube caption tracks (REQ-RAG-018).</param>
    /// <param name="localize">Resolves the progress lines and skip reasons this service composes (REQ-UI-055).</param>
    /// <param name="logger">Diagnostics.</param>
    public WebIngestionService(
        ITechieRag rag,
        IWebContentFetcherFactory fetcherFactory,
        IWorkspaceDocumentLinker linker,
        YouTubeTranscriptReader transcriptReader,
        LocalizeText localize,
        ILogger<WebIngestionService> logger)
    {
        this.rag = rag ?? throw new ArgumentNullException(nameof(rag));
        this.fetcherFactory = fetcherFactory ?? throw new ArgumentNullException(nameof(fetcherFactory));
        this.linker = linker ?? throw new ArgumentNullException(nameof(linker));
        this.transcriptReader = transcriptReader ?? throw new ArgumentNullException(nameof(transcriptReader));
        this.localize = localize ?? throw new ArgumentNullException(nameof(localize));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<WebIngestionOutcome> IngestAsync(
        WebIngestionRequest request,
        IProgress<WebIngestionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The same check the form ran. Re-running it here is not belt-and-braces: the service is the
        // only thing standing between a programmatic caller and an unbounded crawl.
        var invalid = request.Validate(localize);
        if (invalid is not null)
        {
            throw new WebFetchException(request.TrimmedUrl, invalid);
        }

        var total = request.ExpectedFetchCount();
        progress?.Report(new WebIngestionProgress(
            WebIngestionStage.Starting, request.TrimmedUrl, 0, total, localize("WebProgressStarting")));

        var outcome = request.Source switch
        {
            WebIngestionSource.Page => await IngestPageAsync(request, progress, total, cancellationToken)
                .ConfigureAwait(false),
            WebIngestionSource.Site => await IngestSiteAsync(request, progress, total, cancellationToken)
                .ConfigureAwait(false),
            WebIngestionSource.Video => await IngestVideoAsync(request, progress, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new WebFetchException(
                request.TrimmedUrl, localize("WebValidationNotAWebSource", request.Source)),
        };

        progress?.Report(new WebIngestionProgress(
            WebIngestionStage.Done, request.TrimmedUrl, total, total, outcome.SummaryText(localize)));

        logger.LogInformation(
            "Web ingestion of {Url} ({Source}) into {WorkspaceId}: {Ingested} ingested, {Skipped} skipped",
            request.TrimmedUrl,
            request.Source,
            request.WorkspaceId,
            outcome.Ingested.Count,
            outcome.Skipped.Count);

        return outcome;
    }

    /// <summary>Ingests one page (REQ-RAG-016).</summary>
    private async Task<WebIngestionOutcome> IngestPageAsync(
        WebIngestionRequest request,
        IProgress<WebIngestionProgress>? progress,
        int total,
        CancellationToken cancellationToken)
    {
        var fetcher = CreateFetcher(request, progress, total);
        string documentId;
        try
        {
            documentId = await rag
                .IngestUrlAsync(request.TrimmedUrl, fetcher, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WebFetchException ex)
        {
            // One page failing IS the whole request failing, but it is reported through the same
            // Skipped channel as a crawl's failures so the screen has one way to say "not ingested,
            // and here is why" instead of two.
            return new WebIngestionOutcome([], [new CrawlFailure(request.TrimmedUrl, ex.Message)]);
        }

        return await LinkAndDescribeAsync(request, [documentId], [], progress, total, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Crawls a site and ingests what it reaches (REQ-RAG-017).</summary>
    private async Task<WebIngestionOutcome> IngestSiteAsync(
        WebIngestionRequest request,
        IProgress<WebIngestionProgress>? progress,
        int total,
        CancellationToken cancellationToken)
    {
        var options = request.ToCrawlOptions();
        var fetcher = CreateFetcher(request, progress, total);

        WebIngestionResult result;
        try
        {
            result = await rag
                .IngestSiteAsync(request.TrimmedUrl, fetcher, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WebFetchException ex)
        {
            // Thrown for the SEED only — a bad scheme or a private-network seed. Per-page failures
            // never reach here; the crawler collects those into result.Failures.
            return new WebIngestionOutcome([], [new CrawlFailure(request.TrimmedUrl, ex.Message)]);
        }

        // result.Skipped is passed through untouched. Re-phrasing or filtering it is how a crawl
        // ends up reported as a clean success while pages are missing from the workspace.
        return await LinkAndDescribeAsync(
                request, result.DocumentIds, result.Skipped, progress, total, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Ingests a YouTube transcript (REQ-RAG-018).</summary>
    private async Task<WebIngestionOutcome> IngestVideoAsync(
        WebIngestionRequest request,
        IProgress<WebIngestionProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new WebIngestionProgress(
            WebIngestionStage.Fetching, request.TrimmedUrl, 0, 1, localize("WebProgressReadingCaptions")));

        string documentId;
        try
        {
            documentId = await rag
                .IngestYouTubeAsync(
                    request.TrimmedUrl, transcriptReader, request.PreferredLanguage, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WebFetchException ex)
        {
            return new WebIngestionOutcome([], [new CrawlFailure(request.TrimmedUrl, ex.Message)]);
        }

        return await LinkAndDescribeAsync(request, [documentId], [], progress, 1, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Creates the run's fetcher, wrapped so every attempt reaches the screen.</summary>
    private IWebContentFetcher CreateFetcher(
        WebIngestionRequest request, IProgress<WebIngestionProgress>? progress, int total) =>
        new ProgressReportingWebContentFetcher(
            fetcherFactory.Create(!request.AllowPrivateNetworkTargets), progress, total, localize);

    /// <summary>
    /// Adds every ingested document to the workspace and resolves its display name.
    /// </summary>
    private async Task<WebIngestionOutcome> LinkAndDescribeAsync(
        WebIngestionRequest request,
        IReadOnlyList<string> documentIds,
        IReadOnlyList<CrawlFailure> skipped,
        IProgress<WebIngestionProgress>? progress,
        int total,
        CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return new WebIngestionOutcome([], skipped);
        }

        progress?.Report(new WebIngestionProgress(
            WebIngestionStage.Embedding,
            request.TrimmedUrl,
            total,
            total,
            localize("WebProgressAddingDocuments", documentIds.Count)));

        var catalogue = await ReadCatalogueAsync(cancellationToken).ConfigureAwait(false);
        var ingested = new List<WebIngestedDocument>(documentIds.Count);
        var failures = new List<CrawlFailure>(skipped);

        foreach (var documentId in documentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            catalogue.TryGetValue(documentId, out var described);
            var name = described?.Name ?? documentId;
            var sourceUrl = described?.SourceUrl ?? request.TrimmedUrl;

            try
            {
                if (await linker
                        .LinkAsync(request.WorkspaceId, documentId, request.Pinned, cancellationToken)
                        .ConfigureAwait(false))
                {
                    ingested.Add(new WebIngestedDocument(documentId, name, sourceUrl));
                    continue;
                }

                // Embedded but not reachable from the workspace. Counting it as ingested would put a
                // number on screen that the Documents table then contradicts.
                failures.Add(new CrawlFailure(sourceUrl, localize("WebSkipNoWorkspacePersistence")));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to add {DocumentId} to workspace {WorkspaceId}", documentId, request.WorkspaceId);
                failures.Add(new CrawlFailure(sourceUrl, localize("WebSkipLinkFailed", ex.Message)));
            }
        }

        return new WebIngestionOutcome(ingested, failures);
    }

    /// <summary>
    /// Reads the document catalogue once so ingested ids can be shown as names and source URLs.
    /// </summary>
    /// <remarks>Best effort: an unreadable catalogue costs display quality, never the ingestion.</remarks>
    private async Task<Dictionary<string, DescribedDocument>> ReadCatalogueAsync(
        CancellationToken cancellationToken)
    {
        var catalogue = new Dictionary<string, DescribedDocument>(StringComparer.Ordinal);
        try
        {
            foreach (var document in await rag.ListDocumentsAsync(cancellationToken).ConfigureAwait(false))
            {
                // WebSourceUrl, not Metadata["SourceUrl"]. The app's default vector store does not
                // round-trip document metadata, so reading the dictionary directly returned an empty
                // string for every web document and the results list showed a blank source column.
                catalogue[document.Id] = new DescribedDocument(document.Name, document.WebSourceUrl());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The document catalogue could not be read; ingested items will show their ids");
        }

        return catalogue;
    }

    /// <summary>A catalogue entry reduced to the two fields the result list shows.</summary>
    private sealed record DescribedDocument(string Name, string SourceUrl);
}

/// <summary>A document produced by a web ingestion (REQ-RAG-016/017/018).</summary>
/// <param name="DocumentId">The catalogue id.</param>
/// <param name="Name">The document title, or the id when the page carried none.</param>
/// <param name="SourceUrl">Where it was read from, after redirects.</param>
public sealed record WebIngestedDocument(string DocumentId, string Name, string SourceUrl);

/// <summary>
/// What one "Add from web" run did, in full (REQ-RAG-017).
/// </summary>
/// <param name="Ingested">Documents now present in the workspace.</param>
/// <param name="Skipped">Everything that did not make it, each with a reason.</param>
public sealed record WebIngestionOutcome(
    IReadOnlyList<WebIngestedDocument> Ingested,
    IReadOnlyList<CrawlFailure> Skipped)
{
    /// <summary>Gets an outcome in which nothing happened.</summary>
    public static WebIngestionOutcome Empty { get; } = new([], []);

    /// <summary>Gets a value indicating whether anything at all was ingested.</summary>
    public bool Succeeded => Ingested.Count > 0;

    /// <summary>Gets a value indicating whether some content was ingested and some was not.</summary>
    public bool IsPartial => Ingested.Count > 0 && Skipped.Count > 0;

    /// <summary>
    /// Builds a one-line summary, in the reader's language, that never overstates what happened.
    /// </summary>
    /// <param name="localize">Resolves the resource keys the summary is assembled from.</param>
    /// <returns>The line the "Add from web" card and its toast show against the run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="localize"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>A run with skips is never described as a success. "Ingested 20 pages" when 5 were
    /// dropped is technically true and practically a lie, because the operator's next act is to go
    /// looking for content that was never added.</para>
    /// <para><b>REQ-UI-055 / BRD-91.</b> This was a property returning four English literals, read
    /// straight into an alert and three toasts. It is a method taking a
    /// <see cref="LocalizeText"/> for the reason <c>ConnectorRunReport.SummaryText</c> already
    /// records: the sentence is genuinely COMPOSED here from a head and one or two pluralized
    /// counts, and moving that composition into the page would put the honesty rule where the next
    /// screen can get it wrong.</para>
    /// <para>Pluralization goes through separate keys rather than an <c>s</c> appended in code.
    /// Hindi does not form a plural that way, and "4 दस्तावेज़s" is the tell-tale of a counter that
    /// was never really translated.</para>
    /// </remarks>
    public string SummaryText(LocalizeText localize)
    {
        ArgumentNullException.ThrowIfNull(localize);

        return (Ingested.Count, Skipped.Count) switch
        {
            (0, 0) => localize("WebSummaryNothing"),
            (0, var skipped) => localize("WebSummaryNothingSkipped", Items(localize, skipped)),
            (var ingested, 0) => localize("WebSummaryIngested", Documents(localize, ingested)),
            var (ingested, skipped) => localize(
                "WebSummaryIngestedWithSkips",
                Documents(localize, ingested),
                Items(localize, skipped)),
        };
    }

    /// <summary>Renders a count of ingested documents in the reader's language.</summary>
    /// <param name="localize">Resolves the singular or plural key.</param>
    /// <param name="count">How many.</param>
    /// <returns>"1 document" or "4 documents", translated.</returns>
    private static string Documents(LocalizeText localize, int count) =>
        localize(count == 1 ? "WebCountDocument" : "WebCountDocuments", count);

    /// <summary>Renders a count of source items in the reader's language.</summary>
    /// <param name="localize">Resolves the singular or plural key.</param>
    /// <param name="count">How many.</param>
    /// <returns>"1 item" or "4 items", translated.</returns>
    private static string Items(LocalizeText localize, int count) =>
        localize(count == 1 ? "WebCountItem" : "WebCountItems", count);
}
