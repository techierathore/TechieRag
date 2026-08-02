using TechieRag.Web;

namespace TechieDesk.Services.Web;

/// <summary>
/// Wraps a page fetcher so every attempt is reported to the screen (REQ-RAG-017).
/// </summary>
/// <remarks>
/// <para>This is the whole reason a crawl can show live progress without the library growing a
/// progress API. <c>IngestSiteAsync</c> returns once, at the end; the only thing that happens
/// per page is a call to <see cref="IWebContentFetcher.FetchAsync"/>, so decorating the fetcher is
/// the seam where "page 7 of 25" becomes observable.</para>
/// <para>Failures are reported AND rethrown. Swallowing one here would look like progress
/// reporting and would in fact delete the page from <see cref="CrawlResult.Failures"/> — the crawl
/// would then report every page as ingested while pages were silently missing, which is the exact
/// dishonesty the Skipped list exists to prevent.</para>
/// </remarks>
public sealed class ProgressReportingWebContentFetcher : IWebContentFetcher
{
    private readonly IWebContentFetcher inner;
    private readonly IProgress<WebIngestionProgress>? progress;
    private readonly int total;
    private int attempts;

    /// <summary>Initializes a new instance of the <see cref="ProgressReportingWebContentFetcher"/> class.</summary>
    /// <param name="inner">The fetcher that does the real work.</param>
    /// <param name="progress">Where reports go; null disables reporting.</param>
    /// <param name="total">The number of fetches this run may make at most.</param>
    public ProgressReportingWebContentFetcher(
        IWebContentFetcher inner,
        IProgress<WebIngestionProgress>? progress,
        int total)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.progress = progress;
        this.total = total;
    }

    /// <summary>Gets the number of fetches attempted, successful or not.</summary>
    public int AttemptCount => Volatile.Read(ref attempts);

    /// <inheritdoc />
    public async Task<WebPage> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        progress?.Report(new WebIngestionProgress(
            WebIngestionStage.Fetching, url, AttemptCount, total, $"Fetching {Shorten(url)}"));

        try
        {
            var page = await inner.FetchAsync(url, cancellationToken).ConfigureAwait(false);
            var done = Interlocked.Increment(ref attempts);
            progress?.Report(new WebIngestionProgress(
                WebIngestionStage.Fetched,
                page.FinalUrl,
                done,
                total,
                $"Read '{page.Title}' ({done} of at most {total})"));
            return page;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the operator's decision, not a page failure, and must not be counted
            // as one or the final tally would blame the site for the user's Stop button.
            throw;
        }
        catch (Exception ex)
        {
            var done = Interlocked.Increment(ref attempts);
            progress?.Report(new WebIngestionProgress(
                WebIngestionStage.Failed, url, done, total, $"{Shorten(url)} — {ex.Message}"));
            throw;
        }
    }

    /// <summary>Shortens a URL to something that fits one line of status text.</summary>
    /// <param name="url">The URL to shorten.</param>
    /// <returns>The URL, elided in the middle when it is long.</returns>
    private static string Shorten(string url) =>
        url.Length <= 72 ? url : $"{url[..40]}…{url[^28..]}";
}
