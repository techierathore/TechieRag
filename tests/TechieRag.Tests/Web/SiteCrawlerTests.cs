using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Web;

/// <summary>
/// REQ-RAG-017 / BRD-61: the bounds on a crawl. Each of these is the difference between "ingest this
/// site" and an unbounded walk of the web starting at the seed.
/// </summary>
public sealed class SiteCrawlerTests
{
    /// <summary>MaxDepth 0 fetches only the seed, however many links it carries.</summary>
    [Fact]
    public async Task DepthZeroFetchesOnlyTheSeed()
    {
        var fetcher = new FakeFetcher()
            .WithPage("https://example.test/", "https://example.test/a", "https://example.test/b")
            .WithPage("https://example.test/a")
            .WithPage("https://example.test/b");

        var result = await Crawl(fetcher, new WebCrawlOptions { MaxDepth = 0 });

        Assert.Single(result.Pages);
        Assert.Equal("https://example.test/", result.Pages[0].FinalUrl);
    }

    /// <summary>Depth 1 follows the seed's links but not their links.</summary>
    [Fact]
    public async Task DepthOneFollowsOneHop()
    {
        var fetcher = new FakeFetcher()
            .WithPage("https://example.test/", "https://example.test/a")
            .WithPage("https://example.test/a", "https://example.test/deep")
            .WithPage("https://example.test/deep");

        var result = await Crawl(fetcher, new WebCrawlOptions { MaxDepth = 1 });

        Assert.Equal(2, result.Pages.Count);
        Assert.DoesNotContain(result.Pages, p => p.FinalUrl.EndsWith("/deep", StringComparison.Ordinal));
    }

    /// <summary>The page budget is a hard cap regardless of depth.</summary>
    [Fact]
    public async Task StopsAtMaxPages()
    {
        var fetcher = new FakeFetcher()
            .WithPage("https://example.test/", "https://example.test/1", "https://example.test/2", "https://example.test/3")
            .WithPage("https://example.test/1")
            .WithPage("https://example.test/2")
            .WithPage("https://example.test/3");

        var result = await Crawl(fetcher, new WebCrawlOptions { MaxDepth = 5, MaxPages = 2 });

        Assert.Equal(2, result.Pages.Count);
    }

    /// <summary>
    /// Off-host links are not followed by default. One link to a large external site would otherwise
    /// consume the entire page budget on content the user never asked for.
    /// </summary>
    [Fact]
    public async Task StaysOnTheSeedHostByDefault()
    {
        var fetcher = new FakeFetcher()
            .WithPage("https://example.test/", "https://elsewhere.test/page", "https://example.test/local")
            .WithPage("https://elsewhere.test/page")
            .WithPage("https://example.test/local");

        var result = await Crawl(fetcher, new WebCrawlOptions { MaxDepth = 2 });

        Assert.Equal(2, result.Pages.Count);
        Assert.DoesNotContain(result.Pages, p => p.FinalUrl.Contains("elsewhere", StringComparison.Ordinal));
    }

    /// <summary>The same document reached twice is fetched once.</summary>
    [Fact]
    public async Task VisitsEachPageOnce()
    {
        var fetcher = new FakeFetcher()
            .WithPage("https://example.test/", "https://example.test/a", "https://example.test/a/")
            .WithPage("https://example.test/a");

        var result = await Crawl(fetcher, new WebCrawlOptions { MaxDepth = 2 });

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(2, fetcher.Requests.Count);
    }

    /// <summary>
    /// A page that fails is recorded and the crawl continues. A 404 on one link is normal on a real
    /// site; aborting would make large sites uncrawlable.
    /// </summary>
    [Fact]
    public async Task ContinuesPastAFailedPage()
    {
        var fetcher = new FakeFetcher()
            .WithPage("https://example.test/", "https://example.test/gone", "https://example.test/ok")
            .WithFailure("https://example.test/gone", "404")
            .WithPage("https://example.test/ok");

        var result = await Crawl(fetcher, new WebCrawlOptions { MaxDepth = 1 });

        Assert.Equal(2, result.Pages.Count);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("https://example.test/gone", failure.Url);
        Assert.Contains("404", failure.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// SSRF: a public page linking to a private address must not turn this app into a proxy into the
    /// user's own network. The link is not followed.
    /// </summary>
    [Theory]
    [InlineData("http://localhost:8080/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.1.2.3/")]
    [InlineData("http://192.168.0.5/")]
    [InlineData("http://172.16.9.9/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    public async Task DoesNotFollowLinksIntoPrivateNetworks(string privateUrl)
    {
        var fetcher = new FakeFetcher()
            .WithPage("https://example.test/", privateUrl)
            .WithPage(privateUrl);

        var result = await Crawl(fetcher, new WebCrawlOptions { MaxDepth = 2, SameHostOnly = false });

        Assert.Single(result.Pages);
        Assert.DoesNotContain(fetcher.Requests, r => r == privateUrl);
    }

    /// <summary>A private seed is refused outright, with a reason that names the fix.</summary>
    [Fact]
    public async Task RefusesAPrivateSeed()
    {
        var fetcher = new FakeFetcher().WithPage("http://localhost/");

        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => Crawl(fetcher, new WebCrawlOptions(), "http://localhost/"));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An operator who means to crawl an intranet can, by saying so.</summary>
    [Fact]
    public async Task AllowsAPrivateSeedWhenExplicitlyPermitted()
    {
        var fetcher = new FakeFetcher().WithPage("http://192.168.1.10/docs");

        var result = await Crawl(
            fetcher,
            new WebCrawlOptions { BlockPrivateNetworkTargets = false },
            "http://192.168.1.10/docs");

        Assert.Single(result.Pages);
    }

    /// <summary>A non-http seed is rejected before any request is made.</summary>
    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.test/x")]
    [InlineData("file:///etc/passwd")]
    public async Task RejectsANonHttpSeed(string seed)
    {
        var fetcher = new FakeFetcher();

        await Assert.ThrowsAsync<WebFetchException>(() => Crawl(fetcher, new WebCrawlOptions(), seed));
        Assert.Empty(fetcher.Requests);
    }

    private static Task<CrawlResult> Crawl(
        FakeFetcher fetcher,
        WebCrawlOptions options,
        string seed = "https://example.test/")
    {
        // The politeness delay is real time; tests set it to zero and cover it separately.
        options.RequestDelay = TimeSpan.Zero;
        return new SiteCrawler(fetcher).CrawlAsync(seed, options);
    }

    private sealed class FakeFetcher : IWebContentFetcher
    {
        private readonly Dictionary<string, WebPage> pages = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> failures = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Requests { get; } = [];

        public FakeFetcher WithPage(string url, params string[] links)
        {
            pages[url] = new WebPage(url, url, url, $"Text of {url}", links);
            return this;
        }

        public FakeFetcher WithFailure(string url, string reason)
        {
            failures[url] = reason;
            return this;
        }

        public Task<WebPage> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            Requests.Add(url);

            if (failures.TryGetValue(url, out var reason))
            {
                throw new WebFetchException(url, reason);
            }

            if (pages.TryGetValue(url, out var page))
            {
                return Task.FromResult(page);
            }

            var trimmed = url.TrimEnd('/');
            if (pages.TryGetValue(trimmed, out page))
            {
                return Task.FromResult(page);
            }

            throw new WebFetchException(url, "not found in fake");
        }
    }
}
