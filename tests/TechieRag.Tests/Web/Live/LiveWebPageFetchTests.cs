using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Web.Live;

/// <summary>
/// REQ-RAG-016 / BRD-60, and REQ-RAG-031 / BRD-112: <see cref="HttpWebContentFetcher"/> and
/// <see cref="WebPageReader"/> driven against the real internet.
/// </summary>
/// <remarks>
/// <para>The hermetic suite proves the extraction RULES. It cannot prove that they survive contact
/// with real markup, because a fake fetcher hands back whatever HTML the test author imagined. These
/// tests fetch pages nobody in this repository wrote: minified, script-heavy, entity-laden,
/// gzip-encoded, redirect-bearing documents produced by real publishing systems.</para>
/// <para>Assertions are deliberately loose on WORDING and strict on STRUCTURE. Asserting an exact
/// sentence from a Wikipedia article makes the test a tripwire for someone else's copy-editing;
/// asserting that the article body survived while the footer did not is the property that actually
/// matters to retrieval quality.</para>
/// </remarks>
[Trait("Category", LiveNetworkFactAttribute.CategoryName)]
public sealed class LiveWebPageFetchTests : IDisposable
{
    private readonly HttpClient httpClient = HttpWebContentFetcher.CreateDefaultClient();

    /// <summary>
    /// A tiny real page is fetched, titled and reduced to its prose, and its single real link is
    /// discovered as an absolute URL.
    /// </summary>
    [LiveNetworkFact]
    public async Task RealPageIsFetchedAndReducedToItsProse()
    {
        var page = await Fetcher().FetchAsync(LiveTargets.TinyPageWithOneOffHostLink);

        Assert.Contains("Example Domain", page.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("This domain is for use in", page.Text, StringComparison.OrdinalIgnoreCase);

        // The whole point of extraction: what comes out is prose, not markup.
        Assert.DoesNotContain("<div", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("font-family", page.Text, StringComparison.OrdinalIgnoreCase);

        var link = Assert.Single(page.Links);
        Assert.StartsWith("https://", link, StringComparison.Ordinal);
        Assert.Contains("iana.org", link, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A large real article yields substantial text, and the navigation and footer that appear on
    /// every page of the site do not come with it.
    /// </summary>
    /// <remarks>
    /// This is the assertion that decides whether crawling a site is useful or actively harmful. If
    /// chrome survives extraction, every page of the site embeds the same menu and footer, that text
    /// dominates the index, and queries start retrieving the navigation bar instead of the content.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RealArticleKeepsItsBodyAndDropsItsSiteChrome()
    {
        var page = await Fetcher().FetchAsync(LiveTargets.ArticleWithSiteChrome);

        Assert.Contains("Retrieval-augmented generation", page.Title, StringComparison.OrdinalIgnoreCase);

        // Non-trivial: an article that extracted to a few hundred characters has lost its body.
        Assert.True(
            page.Text.Length > 5000,
            $"Expected a substantial article body, extracted only {page.Text.Length} characters.");
        Assert.Contains("retrieval", page.Text, StringComparison.OrdinalIgnoreCase);

        // Footer chrome, present in the source and absent from the extraction.
        Assert.DoesNotContain(LiveTargets.ChromeOnlyFooterMarker, page.Text, StringComparison.OrdinalIgnoreCase);

        // Script bodies are the other thing that must never reach an embedding. These markers are
        // MediaWiki's own inline bootstrap, present on every rendered article.
        Assert.DoesNotContain("mw.config", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("RLQ.push", page.Text, StringComparison.Ordinal);

        Assert.NotEmpty(page.Links);
        Assert.All(page.Links, link => Assert.True(
            Uri.TryCreate(link, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
            $"'{link}' is not an absolute http(s) URL."));
    }

    /// <summary>
    /// A real 404 is reported with the status code rather than being ingested as an error page.
    /// </summary>
    [LiveNetworkFact]
    public async Task RealNotFoundResponseIsReportedWithItsStatusCode()
    {
        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => Fetcher().FetchAsync(LiveTargets.MissingPage));

        Assert.Contains("404", error.Message, StringComparison.Ordinal);
        Assert.Equal(LiveTargets.MissingPage, error.Url);
    }

    /// <summary>
    /// A real non-HTML resource is refused with a reason that tells the operator what to do instead.
    /// </summary>
    [LiveNetworkFact]
    public async Task RealNonHtmlResourceIsRefusedWithAnActionableReason()
    {
        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => Fetcher().FetchAsync(LiveTargets.NonHtmlResource));

        Assert.Contains("json", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a readable web page", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The URL recorded against the document is the one that actually served it, after redirects.
    /// </summary>
    /// <remarks>
    /// Citations point at <c>FinalUrl</c>. Recording the requested URL instead would cite a redirect
    /// that no longer serves the text the answer was drawn from.
    /// </remarks>
    [LiveNetworkFact]
    public async Task FinalUrlRecordsWhereTheContentActuallyCameFrom()
    {
        var requested = LiveTargets.RedirectTo(LiveTargets.TinyPageWithOneOffHostLink);

        var page = await Fetcher().FetchAsync(requested);

        Assert.Equal(requested, page.RequestedUrl);
        Assert.NotEqual(requested, page.FinalUrl);
        Assert.Contains("example.com", page.FinalUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("This domain is for use in", page.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Releases the shared client.</summary>
    public void Dispose() => httpClient.Dispose();

    private HttpWebContentFetcher Fetcher() => new(httpClient);
}
