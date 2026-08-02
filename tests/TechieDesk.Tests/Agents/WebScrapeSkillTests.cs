using TechieDesk.Services.Agents;
using TechieRag.Web;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 — the <c>web-scrape</c> skill. It reuses the library fetcher the crawler already
/// uses, so these tests cover the agent-facing edges: what URLs it will refuse to compose a request
/// for at all, how a failed fetch is reported, and that no fetcher means an honest unavailability.
/// </summary>
public class WebScrapeSkillTests
{
    /// <summary>The skill binds to the catalogue name the toggles and the resolver use.</summary>
    [Fact]
    public void BindsToTheCatalogueName()
    {
        Assert.Equal(SkillCatalog.WebScrape, WebScrapeSkill.Create(null).SkillName);
    }

    /// <summary>With no fetcher configured the skill reports itself unavailable.</summary>
    [Fact]
    public async Task WithNoFetcherItReportsUnavailable()
    {
        var result = await WebScrapeSkill.Create(null)
            .Invoke("""{"url":"https://example.com"}""", CancellationToken.None);

        Assert.True(SkillUnavailable.IsUnavailable(result));
    }

    /// <summary>A fetched page comes back as its title, its final URL and its readable text.</summary>
    [Fact]
    public async Task AFetchedPageIsReturnedAsReadableText()
    {
        var fetcher = new FakeWebContentFetcher(new WebPage(
            "https://example.com", "https://example.com/final", "Example", "The body text.", []));

        var result = await WebScrapeSkill.Create(fetcher)
            .Invoke("""{"url":"https://example.com"}""", CancellationToken.None);

        Assert.Equal("https://example.com", fetcher.LastUrl);
        Assert.Contains("Example", result, StringComparison.Ordinal);
        Assert.Contains("https://example.com/final", result, StringComparison.Ordinal);
        Assert.Contains("The body text.", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// A scheme other than http or https is refused before a request is composed. These are the
    /// shapes a prompt-injected instruction reaches for, and refusing them here keeps the reason
    /// legible in the execution trace.
    /// </summary>
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    [InlineData("javascript:alert(1)")]
    public async Task ANonWebSchemeIsRefusedWithoutFetching(string url)
    {
        var fetcher = new FakeWebContentFetcher(Page());

        var result = await WebScrapeSkill.Create(fetcher)
            .Invoke($$"""{"url":"{{url}}"}""", CancellationToken.None);

        Assert.Null(fetcher.LastUrl);
        Assert.StartsWith("Refused:", result, StringComparison.Ordinal);
    }

    /// <summary>A schemeless or relative URL is refused rather than guessed at.</summary>
    /// <remarks>
    /// A leading-slash path parses as an absolute <c>file:</c> URI on Unix, so it is caught by the
    /// scheme check rather than the shape check. Both refusals are correct; what matters is that
    /// neither reaches the fetcher.
    /// </remarks>
    [Theory]
    [InlineData("example.com/docs")]
    [InlineData("/docs/index.html")]
    public async Task ASchemelessUrlIsRefused(string url)
    {
        var fetcher = new FakeWebContentFetcher(Page());

        var result = await WebScrapeSkill.Create(fetcher)
            .Invoke($$"""{"url":"{{url}}"}""", CancellationToken.None);

        Assert.Null(fetcher.LastUrl);
        Assert.StartsWith("Refused:", result, StringComparison.Ordinal);
    }

    /// <summary>A missing URL is a reportable bad call, not an exception that ends the turn.</summary>
    [Fact]
    public async Task AMissingUrlIsReportedNotThrown()
    {
        var result = await WebScrapeSkill.Create(new FakeWebContentFetcher(Page()))
            .Invoke("{}", CancellationToken.None);

        Assert.Contains("No URL supplied", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// A page that will not load reports the failure in its own words rather than as unavailability
    /// — the skill is available, this page is not, and the model can try another URL.
    /// </summary>
    [Fact]
    public async Task AFailedFetchIsReportedNotUnavailable()
    {
        var fetcher = new FakeWebContentFetcher(Page())
        {
            Failure = new WebFetchException("https://example.com", "404 Not Found")
        };

        var result = await WebScrapeSkill.Create(fetcher)
            .Invoke("""{"url":"https://example.com"}""", CancellationToken.None);

        Assert.False(SkillUnavailable.IsUnavailable(result));
        Assert.Contains("404 Not Found", result, StringComparison.Ordinal);
    }

    /// <summary>A long page is truncated to the requested budget and says that it was.</summary>
    [Fact]
    public async Task ALongPageIsTruncatedHonestly()
    {
        var fetcher = new FakeWebContentFetcher(new WebPage(
            "https://example.com", "https://example.com", "Long", new string('x', 9000), []));

        var result = await WebScrapeSkill.Create(fetcher)
            .Invoke("""{"url":"https://example.com","maxCharacters":500}""", CancellationToken.None);

        Assert.Contains("[truncated at 500 characters]", result, StringComparison.Ordinal);
    }

    /// <summary>Builds a page the fetcher can return when the test does not care about it.</summary>
    /// <returns>A minimal page.</returns>
    private static WebPage Page() =>
        new("https://example.com", "https://example.com", "Example", "body", []);

    /// <summary>A fetcher under test control, so no test ever reaches the network.</summary>
    /// <param name="page">The page to return.</param>
    private sealed class FakeWebContentFetcher(WebPage page) : IWebContentFetcher
    {
        /// <summary>Gets or sets a failure to raise instead of returning the page.</summary>
        public WebFetchException? Failure { get; set; }

        /// <summary>Gets the URL the skill asked for, or null when it never asked.</summary>
        public string? LastUrl { get; private set; }

        /// <inheritdoc />
        public Task<WebPage> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            LastUrl = url;
            return Failure is not null ? Task.FromException<WebPage>(Failure) : Task.FromResult(page);
        }
    }
}
