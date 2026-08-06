using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Web;

/// <summary>
/// Walks a site breadth-first within the configured bounds (REQ-RAG-017 / BRD-61).
/// </summary>
/// <remarks>
/// <para><b>Breadth-first, deliberately.</b> Depth-first with a page budget spends the whole budget
/// descending one branch, so a crawl of depth 3 and 25 pages would return 25 pages from a single
/// corner of the site. Breadth-first spends the budget on the pages nearest the seed, which is what
/// someone asking to "ingest this site" means.</para>
/// <para><b>One page failing does not fail the crawl.</b> A 404 or a timeout on one link is normal
/// on a real site; aborting would make crawling large sites impossible. Failures are collected and
/// reported alongside the pages.</para>
/// </remarks>
public sealed class SiteCrawler
{
    private readonly IWebContentFetcher fetcher;
    private readonly ILogger<SiteCrawler> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new instance of the <see cref="SiteCrawler"/> class.</summary>
    /// <param name="fetcher">Page fetcher.</param>
    /// <param name="logger">Diagnostics.</param>
    /// <param name="timeProvider">Clock, so the politeness delay is testable without real waiting.</param>
    public SiteCrawler(
        IWebContentFetcher fetcher,
        ILogger<SiteCrawler>? logger = null,
        TimeProvider? timeProvider = null)
    {
        this.fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        this.logger = logger ?? NullLogger<SiteCrawler>.Instance;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Crawls from a seed URL.</summary>
    /// <param name="seedUrl">Absolute http/https URL to start from.</param>
    /// <param name="options">Crawl bounds; defaults are conservative.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pages fetched and the failures encountered.</returns>
    public async Task<CrawlResult> CrawlAsync(
        string seedUrl,
        WebCrawlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(seedUrl);
        options ??= new WebCrawlOptions();

        if (!Uri.TryCreate(seedUrl, UriKind.Absolute, out var seed)
            || (seed.Scheme != Uri.UriSchemeHttp && seed.Scheme != Uri.UriSchemeHttps))
        {
            throw new WebFetchException(seedUrl, $"'{seedUrl}' is not an absolute http or https URL.");
        }

        if (options.BlockPrivateNetworkTargets && WebCrawlOptions.IsPrivateNetworkHost(seed.Host))
        {
            throw new WebFetchException(
                seedUrl,
                $"'{seed.Host}' is a private-network address. Allow private targets explicitly if you meant to crawl an intranet.");
        }

        var pages = new List<WebPage>();
        var failures = new List<CrawlFailure>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Normalize(seed.ToString()) };
        var queue = new Queue<(string Url, int Depth)>();
        queue.Enqueue((seed.ToString(), 0));

        var isFirst = true;
        while (queue.Count > 0 && pages.Count < options.MaxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (url, depth) = queue.Dequeue();

            // Delay BETWEEN requests, not before the first: a single-page ingest should not sit
            // waiting on a politeness delay it owes nobody.
            if (!isFirst && options.RequestDelay > TimeSpan.Zero)
            {
                await Task.Delay(options.RequestDelay, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            isFirst = false;

            WebPage page;
            try
            {
                page = await fetcher.FetchAsync(url, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Crawl skipped {Url}", url);
                failures.Add(new CrawlFailure(url, ex.Message));
                continue;
            }

            pages.Add(page);

            if (depth >= options.MaxDepth)
            {
                continue;
            }

            foreach (var link in page.Links)
            {
                // Enqueueing past the budget would let a single link-heavy page fill the queue with
                // thousands of entries that can never be fetched.
                if (visited.Count >= options.MaxPages * 4)
                {
                    break;
                }

                if (!Uri.TryCreate(link, UriKind.Absolute, out var candidate))
                {
                    continue;
                }

                if (options.SameHostOnly
                    && !string.Equals(candidate.Host, seed.Host, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (options.BlockPrivateNetworkTargets
                    && WebCrawlOptions.IsPrivateNetworkHost(candidate.Host))
                {
                    continue;
                }

                if (visited.Add(Normalize(link)))
                {
                    queue.Enqueue((link, depth + 1));
                }
            }
        }

        logger.LogInformation(
            "Crawl of {Seed} fetched {Pages} page(s), {Failures} failure(s)",
            seedUrl,
            pages.Count,
            failures.Count);

        return new CrawlResult(pages, failures);
    }

    // A trailing slash does not make a different document, and treating it as one is the classic way
    // a crawler ingests every page twice.
    private static string Normalize(string url) =>
        url.EndsWith('/') ? url[..^1] : url;
}

/// <summary>The outcome of a crawl (REQ-RAG-017).</summary>
/// <param name="Pages">Pages successfully fetched, seed first.</param>
/// <param name="Failures">Pages that could not be fetched. Reported, never silently dropped.</param>
public sealed record CrawlResult(IReadOnlyList<WebPage> Pages, IReadOnlyList<CrawlFailure> Failures);

/// <summary>A page the crawl could not fetch (REQ-RAG-017).</summary>
/// <param name="Url">The URL that failed.</param>
/// <param name="Reason">Why, in operator-facing terms.</param>
public sealed record CrawlFailure(string Url, string Reason);
