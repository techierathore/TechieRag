using System.Diagnostics;
using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Web.Live;

/// <summary>
/// REQ-RAG-017 / BRD-61: the crawl bounds, applied to real link graphs on live hosts.
/// </summary>
/// <remarks>
/// <para>The hermetic crawler tests hand the walker a link graph the test author drew. That proves
/// the algorithm and proves nothing about the inputs: a real page's anchors are relative, protocol
/// relative, fragment-bearing, duplicated, and mixed with <c>mailto:</c> and <c>javascript:</c>. The
/// bounds are only meaningful if they hold against those.</para>
/// <para><b>The budget is the safety property.</b> An unbounded crawl is the one ingestion path that
/// turns a single click into unbounded outbound traffic and unbounded embedding cost, so "it stopped
/// at exactly N pages on a site with hundreds of real links" is the assertion worth having.</para>
/// </remarks>
[Trait("Category", LiveNetworkFactAttribute.CategoryName)]
public sealed class LiveSiteCrawlTests : IDisposable
{
    /// <summary>Politeness delay used by every crawl here. Left ON deliberately.</summary>
    private static readonly TimeSpan Politeness = TimeSpan.FromMilliseconds(500);

    private readonly HttpClient httpClient = HttpWebContentFetcher.CreateDefaultClient();

    /// <summary>
    /// A crawl of a real site with hundreds of reachable links stops at exactly the page budget.
    /// </summary>
    [LiveNetworkFact]
    public async Task RealCrawlStopsAtThePageBudget()
    {
        var result = await Crawl(LiveTargets.CrawlableSandbox, new WebCrawlOptions
        {
            MaxDepth = 3,
            MaxPages = 4,
            RequestDelay = Politeness,
        });

        Assert.Equal(4, result.Pages.Count);

        // The seed really did offer far more than the budget — otherwise the budget proved nothing.
        Assert.True(
            result.Pages[0].Links.Count > 10,
            $"The seed offered only {result.Pages[0].Links.Count} links, so the budget was not exercised.");
    }

    /// <summary>
    /// A crawl of a real page whose only link is off-host returns just that page.
    /// </summary>
    /// <remarks>
    /// <c>example.com</c> is the ideal witness: its link graph is one off-host anchor and nothing
    /// else, so "stayed on host" and "found nothing to follow" are distinguishable — the seed's own
    /// link list is asserted to contain the off-host target that was declined.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RealCrawlStaysOnTheSeedHost()
    {
        var result = await Crawl(LiveTargets.TinyPageWithOneOffHostLink, new WebCrawlOptions
        {
            MaxDepth = 3,
            MaxPages = 25,
            RequestDelay = Politeness,
        });

        var seed = Assert.Single(result.Pages);
        Assert.Contains(seed.Links, link => !link.Contains("example.com", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every page a real crawl returns is on the seed host, including pages reached at depth.
    /// </summary>
    [LiveNetworkFact]
    public async Task EveryPageOfARealCrawlIsOnTheSeedHost()
    {
        var result = await Crawl(LiveTargets.CrawlableSandbox, new WebCrawlOptions
        {
            MaxDepth = 2,
            MaxPages = 5,
            RequestDelay = Politeness,
        });

        Assert.NotEmpty(result.Pages);
        Assert.All(result.Pages, page => Assert.Equal(
            LiveTargets.CrawlableSandboxHost,
            new Uri(page.FinalUrl).Host,
            ignoreCase: true));
    }

    /// <summary>
    /// The politeness delay is really waited between real requests.
    /// </summary>
    /// <remarks>
    /// Wall-clock, not a mocked clock. The hermetic suite already covers the delay against a
    /// <see cref="TimeProvider"/>, which proves the call is made; only real elapsed time proves the
    /// crawler is not hammering the host. Compared against a lower bound with slack, since real
    /// fetches add time and never remove it.
    /// </remarks>
    [LiveNetworkFact]
    public async Task PolitenessDelayIsReallyWaitedBetweenRealRequests()
    {
        var delay = TimeSpan.FromSeconds(1);
        var stopwatch = Stopwatch.StartNew();

        var result = await Crawl(LiveTargets.CrawlableSandbox, new WebCrawlOptions
        {
            MaxDepth = 1,
            MaxPages = 3,
            RequestDelay = delay,
        });

        stopwatch.Stop();

        Assert.Equal(3, result.Pages.Count);

        // Three fetches means two gaps. The delay runs before every request but the first.
        Assert.True(
            stopwatch.Elapsed >= delay + delay,
            $"Three fetches with a {delay.TotalSeconds}s delay took only {stopwatch.Elapsed.TotalSeconds:F1}s, "
            + "so the politeness delay was not applied.");
    }

    /// <summary>
    /// Pages returned by a real crawl carry their prose and not the navigation and footer that
    /// appear identically on every page of the site.
    /// </summary>
    /// <remarks>
    /// Chrome removal matters most in a crawl. One page keeping its footer is noise; twenty-five
    /// pages each keeping the same footer means the most-repeated text in the index is a copyright
    /// line, and it will out-rank the content for any vaguely matching query.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RealCrawledPagesCarryProseWithoutSiteChrome()
    {
        var result = await Crawl(LiveTargets.CrawlableSandbox, new WebCrawlOptions
        {
            MaxDepth = 1,
            MaxPages = 3,
            RequestDelay = Politeness,
        });

        Assert.NotEmpty(result.Pages);
        Assert.All(result.Pages, page =>
        {
            Assert.False(string.IsNullOrWhiteSpace(page.Text), $"{page.FinalUrl} extracted to nothing.");
            Assert.DoesNotContain(LiveTargets.SandboxFooterMarker, page.Text, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>Releases the shared client.</summary>
    public void Dispose() => httpClient.Dispose();

    private Task<CrawlResult> Crawl(string seed, WebCrawlOptions options) =>
        new SiteCrawler(new HttpWebContentFetcher(httpClient)).CrawlAsync(seed, options);
}
