using System.Net;
using System.Text;
using TechieRag.Tests.Connectors;
using TechieRag.Tests.TestDoubles;
using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Web;

/// <summary>
/// REQ-RAG-074 / BRD-60 and REQ-RAG-075 / BRD-61 acceptance: ingesting one page by URL, and crawling a
/// site within its depth and page limits, through the public <see cref="WebIngestionExtensions"/>.
/// </summary>
/// <remarks>
/// The single-page test runs the real <see cref="HttpWebContentFetcher"/> and <c>WebPageReader</c> over
/// a stubbed <see cref="HttpMessageHandler"/> and stores into a real temporary SQLite database; no
/// network is touched.
/// </remarks>
public sealed class WebIngestionEndToEndTests : IDisposable
{
    private const string ArticleHtml =
        "<html><head><title>Release notes</title>"
        + "<script>var tracker = 'TRACKING-PIXEL';</script><style>.banner { color: red; }</style></head>"
        + "<body><nav><a href=\"/\">Home</a> <a href=\"/about\">About us</a></nav>"
        + "<article><h1>Release notes</h1><p>Version four adds calendar sync for every workspace.</p></article>"
        + "</body></html>";

    private readonly string workFolder = Path.Combine(Path.GetTempPath(), $"trweb-{Guid.NewGuid():N}");

    /// <summary>Creates the temporary folder holding the vector database.</summary>
    public WebIngestionEndToEndTests() => Directory.CreateDirectory(workFolder);

    /// <summary>
    /// Acceptance of REQ-RAG-074: <c>IngestUrlAsync</c> fetches the page, drops script, style and
    /// navigation chrome, and stores the readable text as one searchable document whose source is the URL.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-074 IngestUrlStoresCleanTextAsOneDocument")]
    public async Task IngestUrlStoresCleanTextAsOneDocument()
    {
        var rag = new TechieRagBuilder()
            .UseCustomEmbeddingProvider(() => new FakeEmbeddingProvider())
            .UseSqliteVec(Path.Combine(workFolder, "vectors.db"))
            .Build();
        await rag.InitializeAsync();
        using var client = new HttpClient(new HtmlHandler(ArticleHtml));
        var fetcher = new HttpWebContentFetcher(client, logger: null, blockPrivateTargets: false);

        var documentId = await rag.IngestUrlAsync("https://news.example.test/release-notes", fetcher);
        var found = await rag.SearchAsync("calendar sync", 10);
        var documents = await rag.ListDocumentsAsync();

        var document = Assert.Single(documents);
        Assert.Equal(documentId, document.Id);
        Assert.True(document.ChunkCount > 0);
        Assert.Equal("https://news.example.test/release-notes", document.WebSourceUrl());
        Assert.NotEmpty(found);
        Assert.All(found, result => Assert.Equal(documentId, result.Chunk.DocumentId));
        var text = string.Join("\n", found.Select(result => result.Chunk.Text));
        Assert.Contains("calendar sync", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TRACKING-PIXEL", text, StringComparison.Ordinal);
        Assert.DoesNotContain(".banner", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Acceptance of REQ-RAG-075, depth: crawling a chain seed → a → b → c with depth 2 ingests the seed,
    /// a and b, and never fetches or ingests c.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-075 SiteIngestionStopsAtDepthTwo")]
    public async Task SiteIngestionStopsAtDepthTwo()
    {
        var fetcher = new ChainFetcher()
            .Page("https://site.example.test/", "https://site.example.test/a")
            .Page("https://site.example.test/a", "https://site.example.test/b")
            .Page("https://site.example.test/b", "https://site.example.test/c")
            .Page("https://site.example.test/c");
        var rag = new RecordingRag();

        var result = await rag.IngestSiteAsync(
            "https://site.example.test/", fetcher, new WebCrawlOptions { MaxDepth = 2, MaxPages = 25, RequestDelay = TimeSpan.Zero });

        Assert.Equal(3, result.DocumentIds.Count);
        Assert.Equal(
            ["https://site.example.test/", "https://site.example.test/a", "https://site.example.test/b"],
            rag.Ingested.Select(entry => (string)entry.Metadata!["SourceUrl"]));
        Assert.DoesNotContain("https://site.example.test/c", fetcher.Requests);
    }

    /// <summary>
    /// Acceptance of REQ-RAG-075, link cap: a seed linking to five pages, crawled with depth 2 and a cap
    /// of three pages, ingests exactly three pages and fetches no more.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-075 SiteIngestionStopsAtPageCap")]
    public async Task SiteIngestionStopsAtPageCap()
    {
        var links = Enumerable.Range(1, 5).Select(i => $"https://site.example.test/{i}").ToArray();
        var fetcher = new ChainFetcher().Page("https://site.example.test/", links);
        foreach (var link in links)
        {
            fetcher.Page(link);
        }

        var rag = new RecordingRag();

        var result = await rag.IngestSiteAsync(
            "https://site.example.test/", fetcher, new WebCrawlOptions { MaxDepth = 2, MaxPages = 3, RequestDelay = TimeSpan.Zero });

        Assert.Equal(3, result.DocumentIds.Count);
        Assert.Equal(3, rag.Ingested.Count);
        Assert.Equal(3, fetcher.Requests.Count);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(workFolder)) Directory.Delete(workFolder, recursive: true);
    }

    /// <summary>Answers every request with the same HTML page.</summary>
    private sealed class HtmlHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });
    }

    /// <summary>A fetcher serving declared pages, each with readable text and its own links.</summary>
    private sealed class ChainFetcher : IWebContentFetcher
    {
        private readonly Dictionary<string, WebPage> pages = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Requests { get; } = [];

        public ChainFetcher Page(string url, params string[] links)
        {
            pages[url] = new WebPage(url, url, url, $"Readable text of {url}", links);
            return this;
        }

        public Task<WebPage> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            Requests.Add(url);
            return pages.TryGetValue(url, out var page) || pages.TryGetValue(url.TrimEnd('/'), out page)
                ? Task.FromResult(page)
                : throw new WebFetchException(url, "not declared in the fake");
        }
    }
}
