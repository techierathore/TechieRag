using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Web;

/// <summary>
/// REQ-RAG-031: turning a fetched page into the text that gets embedded and the links a crawl may
/// follow. What this drops matters as much as what it keeps — chrome that survives extraction is
/// embedded on every page of a site and then dominates retrieval.
/// </summary>
public sealed class WebPageReaderTests
{
    private const string Sample = """
        <html>
          <head><title>  Pricing   Guide </title><style>.a{color:red}</style></head>
          <body>
            <nav><a href="/home">Home</a><a href="/about">About</a></nav>
            <h1>Plans</h1>
            <p>The Pro plan costs $20 per month.</p>
            <script>console.log('tracking')</script>
            <footer><a href="/legal">Legal</a></footer>
          </body>
        </html>
        """;

    /// <summary>The title is read and its whitespace collapsed.</summary>
    [Fact]
    public void ReadsAndNormalizesTheTitle()
    {
        var page = WebPageReader.Read(Sample, "https://example.test/pricing");

        Assert.Equal("Pricing Guide", page.Title);
    }

    /// <summary>Script and style content never reaches the embedder.</summary>
    [Fact]
    public void DropsScriptAndStyle()
    {
        var page = WebPageReader.Read(Sample, "https://example.test/pricing");

        Assert.DoesNotContain("tracking", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("color:red", page.Text, StringComparison.Ordinal);
        Assert.Contains("The Pro plan costs $20 per month.", page.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Site chrome is dropped from the TEXT. Every page of a site repeats its nav and footer, so
    /// keeping them would embed the same menu into every document and make "Home About Legal" the
    /// best match for a large share of queries.
    /// </summary>
    [Fact]
    public void DropsNavigationChromeFromText()
    {
        var page = WebPageReader.Read(Sample, "https://example.test/pricing");

        Assert.DoesNotContain("About", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Legal", page.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// …but chrome links are still FOLLOWED. Navigation is where a site's structure lives, so
    /// discarding those links would leave a crawl unable to find anything past the seed.
    /// </summary>
    [Fact]
    public void StillCollectsLinksFromChrome()
    {
        var page = WebPageReader.Read(Sample, "https://example.test/pricing");

        Assert.Contains("https://example.test/home", page.Links);
        Assert.Contains("https://example.test/legal", page.Links);
    }

    /// <summary>Relative hrefs resolve against the page's final URL.</summary>
    [Fact]
    public void ResolvesRelativeLinks()
    {
        const string html = """<a href="../sibling">x</a><a href="deep/page">y</a>""";

        var page = WebPageReader.Read(html, "https://example.test/docs/guide/");

        Assert.Contains("https://example.test/docs/sibling", page.Links);
        Assert.Contains("https://example.test/docs/guide/deep/page", page.Links);
    }

    /// <summary>
    /// Fragments are stripped and duplicates collapse. Without this, /page and /page#a are two crawl
    /// budget entries for one document, and a page linking itself twenty times eats the budget.
    /// </summary>
    [Fact]
    public void CollapsesFragmentsAndDuplicates()
    {
        const string html = """
            <a href="/page">a</a><a href="/page#intro">b</a><a href="/page#end">c</a><a href="#top">d</a>
            """;

        var page = WebPageReader.Read(html, "https://example.test/");

        Assert.Single(page.Links);
        Assert.Equal("https://example.test/page", page.Links[0]);
    }

    /// <summary>Non-http schemes are not documents and are never followed.</summary>
    [Fact]
    public void IgnoresNonHttpSchemes()
    {
        const string html = """
            <a href="mailto:a@b.test">mail</a><a href="javascript:alert(1)">js</a>
            <a href="tel:+123">tel</a><a href="https://example.test/real">real</a>
            """;

        var page = WebPageReader.Read(html, "https://example.test/");

        Assert.Single(page.Links);
        Assert.Equal("https://example.test/real", page.Links[0]);
    }

    /// <summary>HTML entities are decoded so the embedder sees prose, not markup.</summary>
    [Fact]
    public void DecodesEntities()
    {
        var page = WebPageReader.Read("<p>Tom &amp; Jerry&#39;s</p>", "https://example.test/");

        Assert.Contains("Tom & Jerry's", page.Text, StringComparison.Ordinal);
    }

    /// <summary>A page with no title falls back to its host rather than an empty document name.</summary>
    [Fact]
    public void FallsBackToTheHostWhenThereIsNoTitle()
    {
        var page = WebPageReader.Read("<p>text</p>", "https://example.test/a/b");

        Assert.Equal("example.test", page.Title);
    }

    /// <summary>Empty input yields an empty page rather than throwing.</summary>
    [Fact]
    public void HandlesEmptyHtml()
    {
        var page = WebPageReader.Read(string.Empty, "https://example.test/");

        Assert.Empty(page.Text);
        Assert.Empty(page.Links);
    }
}
