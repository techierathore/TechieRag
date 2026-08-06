using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services;
using TechieDesk.Services.Hosting;
using TechieDesk.Services.Localization;
using TechieDesk.Services.Web;
using TechieDesk.Tests.Support;
using TechieRag;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Services;
using TechieRag.Web;
using Xunit;

namespace TechieDesk.Tests.Web;

/// <summary>
/// REQ-RAG-016/017/018: the "Add from web" surface, driven end to end over a real TechieRag
/// instance, a real <see cref="WorkspaceManager"/> and the real production
/// <see cref="WorkspaceDocumentLinker"/>.
/// </summary>
/// <remarks>
/// <para>Only two things are faked, and neither of them is behaviour under test: the network (a
/// scripted <see cref="IWebContentFetcher"/>, which is exactly the seam the library documents for
/// this purpose) and the embedding model. Everything else — crawl bounds, the breadth-first walk,
/// the skip reasons, deduplication, the SQLite workspace store — is the shipping code.</para>
/// <para>What is asserted throughout is the state of the WORKSPACE, not the return value alone. A
/// service that returned a happy result and linked nothing would leave the Documents screen empty,
/// and that is the failure this suite exists to catch.</para>
/// </remarks>
public sealed class WebIngestionServiceTests : IDisposable
{
    private const string SeedUrl = "https://example.com/";
    private const string WorkspaceName = "Research";

    /// <summary>
    /// REQ-UI-055: the run summary and the skip reasons are resource keys now, so this suite reads
    /// them back through the REAL English resources rather than against literals in the service.
    /// </summary>
    /// <remarks>
    /// Static because the nested harness builds the service, and holding the culture at English for
    /// this class is what keeps the substring assertions below meaningful.
    /// </remarks>
    private static readonly ResourceHarness Resources = new("en");

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"techiedesk-webingest-{Guid.NewGuid():N}");

    /// <summary>Gets the delegate the ingestion service and the outcome summary resolve through.</summary>
    private static LocalizeText Localize => Resources.Localize;

    /// <summary>
    /// REQ-RAG-016: one URL becomes one document that the workspace can actually see.
    /// </summary>
    [Fact]
    public async Task SinglePageIsIngestedAndAddedToTheWorkspace()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Fetcher.AddPage(SeedUrl, "Example Home", "The quick brown fox jumps over the lazy dog.");

        var outcome = await harness.Service.IngestAsync(harness.Request(WebIngestionSource.Page, SeedUrl));

        var ingested = Assert.Single(outcome.Ingested);
        Assert.Empty(outcome.Skipped);
        Assert.Equal("Example Home", ingested.Name);
        Assert.True(outcome.Succeeded);

        var inWorkspace = await harness.ListWorkspaceDocumentsAsync();
        Assert.Equal(ingested.DocumentId, Assert.Single(inWorkspace).DocumentId);
    }

    /// <summary>
    /// REQ-RAG-016: every ingested document reports the URL it came from, after a real round trip
    /// through the vector store.
    /// </summary>
    /// <remarks>
    /// <para>The regression. This assertion has to survive the STORE, not just the service: web
    /// ingestion recorded the URL in the document's metadata, <c>SqliteVecStore</c> wrote the
    /// document row's <c>Metadata</c> column as a literal <c>{}</c>, and the catalogue read back a
    /// document with no metadata at all. Every "Add from web" result carried an empty source URL —
    /// the count was right, the documents were searchable, and the column the user reads was
    /// blank.</para>
    /// <para>Found only by running the real stack end to end. Asserting the service's return value
    /// against a scripted store would have passed throughout.</para>
    /// </remarks>
    [Fact]
    public async Task IngestedDocumentsReportTheSourceUrlAfterAStoreRoundTrip()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Fetcher.AddPage(SeedUrl, "Example Home", "The quick brown fox jumps over the lazy dog.");

        var outcome = await harness.Service.IngestAsync(harness.Request(WebIngestionSource.Page, SeedUrl));

        var ingested = Assert.Single(outcome.Ingested);
        Assert.Equal(SeedUrl, ingested.SourceUrl);
    }

    /// <summary>
    /// REQ-RAG-017: every page of a crawl reports its OWN URL, not the seed's.
    /// </summary>
    /// <remarks>
    /// A fallback that filled the blank with the seed URL would have hidden the same bug while
    /// making every crawled page cite the wrong document.
    /// </remarks>
    [Fact]
    public async Task EachCrawledDocumentReportsItsOwnSourceUrl()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Fetcher.AddPage(SeedUrl, "Home", "Text of the home page.", "https://example.com/a");
        harness.Fetcher.AddPage("https://example.com/a", "Page A", "Text of page A.");

        var outcome = await harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Site, SeedUrl, maxDepth: 1));

        Assert.Equal(2, outcome.Ingested.Count);
        Assert.Contains(outcome.Ingested, document => document.SourceUrl == SeedUrl);
        Assert.Contains(outcome.Ingested, document => document.SourceUrl == "https://example.com/a");
    }

    /// <summary>
    /// REQ-RAG-016: a page that cannot be read is reported with the library's own reason, and
    /// nothing is added to the workspace.
    /// </summary>
    [Fact]
    public async Task UnreachablePageIsReportedWithItsReasonAndAddsNothing()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Fetcher.AddFailure(SeedUrl, "example.com replied 404 (Not Found).");

        var outcome = await harness.Service.IngestAsync(harness.Request(WebIngestionSource.Page, SeedUrl));

        Assert.Empty(outcome.Ingested);
        Assert.False(outcome.Succeeded);
        var skipped = Assert.Single(outcome.Skipped);
        Assert.Equal("example.com replied 404 (Not Found).", skipped.Reason);
        Assert.Empty(await harness.ListWorkspaceDocumentsAsync());
    }

    /// <summary>
    /// REQ-RAG-017: THE acceptance for honest failure. A crawl in which some pages fail and some
    /// hold no readable text reports each of them, with its reason, and its summary never describes
    /// the run as a plain success.
    /// </summary>
    [Fact]
    public async Task CrawlSurfacesEverySkippedPageWithItsReason()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Fetcher.AddPage(
            SeedUrl,
            "Index",
            "Documentation index for the example project.",
            "https://example.com/good",
            "https://example.com/broken",
            "https://example.com/gallery");
        harness.Fetcher.AddPage("https://example.com/good", "Guide", "A guide with genuine prose in it.");
        harness.Fetcher.AddFailure("https://example.com/broken", "example.com replied 500 (Server Error).");
        harness.Fetcher.AddPage("https://example.com/gallery", "Gallery", "   ");

        var outcome = await harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Site, SeedUrl, maxDepth: 1, maxPages: 10));

        Assert.Equal(2, outcome.Ingested.Count);
        Assert.Equal(2, outcome.Skipped.Count);
        Assert.True(outcome.IsPartial);

        Assert.Contains(outcome.Skipped, f =>
            f.Url == "https://example.com/broken" && f.Reason.Contains("500", StringComparison.Ordinal));
        Assert.Contains(outcome.Skipped, f =>
            f.Url == "https://example.com/gallery" && f.Reason.Contains("no readable text", StringComparison.OrdinalIgnoreCase));

        // The summary is what the screen and the toast both show. It must name the skips.
        Assert.Contains("skipped", outcome.SummaryText(Localize), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, (await harness.ListWorkspaceDocumentsAsync()).Count);
    }

    /// <summary>
    /// A run that ingested nothing says so. "Ingested 0 documents" would read as a success.
    /// </summary>
    [Fact]
    public void SummaryNeverOverstatesWhatHappened()
    {
        var nothing = new WebIngestionOutcome([], [new CrawlFailure("https://a", "gone")]);
        var partial = new WebIngestionOutcome(
            [new WebIngestedDocument("d1", "A", "https://a")],
            [new CrawlFailure("https://b", "gone")]);
        var clean = new WebIngestionOutcome([new WebIngestedDocument("d1", "A", "https://a")], []);

        Assert.Contains("Nothing was ingested", nothing.SummaryText(Localize), StringComparison.Ordinal);
        Assert.False(nothing.Succeeded);

        Assert.Contains("skipped", partial.SummaryText(Localize), StringComparison.OrdinalIgnoreCase);
        Assert.True(partial.IsPartial);

        Assert.Equal("Ingested 1 document.", clean.SummaryText(Localize));
        Assert.False(clean.IsPartial);
        Assert.Empty(WebIngestionOutcome.Empty.Ingested);
    }

    /// <summary>
    /// REQ-RAG-017: the page budget the form collected is the budget the crawl obeys. Without this,
    /// "maximum pages" is decoration and one click can fetch a whole site.
    /// </summary>
    [Fact]
    public async Task CrawlStopsAtThePageBudgetTheFormCollected()
    {
        await using var harness = await Harness.CreateAsync(directory);
        var links = Enumerable.Range(1, 12).Select(i => $"https://example.com/p{i}").ToArray();
        harness.Fetcher.AddPage(SeedUrl, "Index", "An index of many pages.", links);
        foreach (var link in links)
        {
            harness.Fetcher.AddPage(link, $"Page {link[^2..]}", $"Readable prose belonging to {link}.");
        }

        var outcome = await harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Site, SeedUrl, maxDepth: 1, maxPages: 3));

        Assert.Equal(3, harness.Fetcher.RequestedUrls.Count);
        Assert.Equal(3, outcome.Ingested.Count);
    }

    /// <summary>
    /// REQ-RAG-017: depth 0 fetches the seed and nothing else, however many links it carries.
    /// </summary>
    [Fact]
    public async Task DepthZeroFetchesOnlyTheSeed()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Fetcher.AddPage(
            SeedUrl, "Index", "An index page.", "https://example.com/a", "https://example.com/b");
        harness.Fetcher.AddPage("https://example.com/a", "A", "Page A prose.");
        harness.Fetcher.AddPage("https://example.com/b", "B", "Page B prose.");

        await harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Site, SeedUrl, maxDepth: 0, maxPages: 25));

        Assert.Equal(SeedUrl, Assert.Single(harness.Fetcher.RequestedUrls));
    }

    /// <summary>
    /// REQ-RAG-017: progress is reported per page, so a crawl that takes minutes never looks hung.
    /// </summary>
    [Fact]
    public async Task ProgressIsReportedForEveryPageAttempt()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Fetcher.AddPage(
            SeedUrl, "Index", "An index page.", "https://example.com/a", "https://example.com/bad");
        harness.Fetcher.AddPage("https://example.com/a", "A", "Page A prose.");
        harness.Fetcher.AddFailure("https://example.com/bad", "example.com replied 404 (Not Found).");

        var reports = new List<WebIngestionProgress>();
        await harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Site, SeedUrl, maxDepth: 1, maxPages: 10),
            new SynchronousProgress(reports.Add));

        Assert.Equal(3, reports.Count(r => r.Stage == WebIngestionStage.Fetching));
        Assert.Equal(2, reports.Count(r => r.Stage == WebIngestionStage.Fetched));
        Assert.Single(reports, r => r.Stage == WebIngestionStage.Failed);
        Assert.Contains(reports, r => r.Stage == WebIngestionStage.Done);

        // A failed page still advances the bar; a stalled bar is what a hang looks like.
        var failure = reports.Single(r => r.Stage == WebIngestionStage.Failed);
        Assert.Contains("404", failure.Message, StringComparison.Ordinal);
        Assert.InRange(failure.Percent, 1, 100);
    }

    /// <summary>
    /// REQ-RAG-017 (security): the SSRF guard is refused at the request boundary, before a socket is
    /// opened — the fetcher is never even asked for the private address.
    /// </summary>
    [Fact]
    public async Task PrivateNetworkSeedIsRefusedBeforeAnythingIsFetched()
    {
        await using var harness = await Harness.CreateAsync(directory);

        var refusal = await Assert.ThrowsAsync<WebFetchException>(() => harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Site, "http://169.254.169.254/latest/meta-data/")));

        Assert.Contains("private-network", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(harness.Fetcher.RequestedUrls);
        Assert.Empty(await harness.ListWorkspaceDocumentsAsync());
    }

    /// <summary>
    /// REQ-RAG-017: the intranet opt-in reaches BOTH the crawl options and the fetcher. Setting only
    /// one of them would leave the guard silently half on, which is worse than either state.
    /// </summary>
    [Fact]
    public async Task IntranetOptInReachesTheFetcherAndTheCrawlOptions()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Fetcher.AddPage("http://intranet.local/", "Handbook", "Internal handbook prose.");

        var outcome = await harness.Service.IngestAsync(new WebIngestionRequest
        {
            Source = WebIngestionSource.Site,
            WorkspaceId = harness.WorkspaceId,
            Url = "http://intranet.local/",
            AllowPrivateNetworkTargets = true,
            MaxDepth = 0,
            MaxPages = 5,
        });

        Assert.Single(outcome.Ingested);
        Assert.False(harness.FetcherFactory.LastBlockedPrivateTargets);

        // And the default is the opposite, for the same service instance.
        harness.Fetcher.AddPage(SeedUrl, "Public", "Public prose.");
        await harness.Service.IngestAsync(harness.Request(WebIngestionSource.Page, SeedUrl));
        Assert.True(harness.FetcherFactory.LastBlockedPrivateTargets);
    }

    /// <summary>
    /// REQ-RAG-013 carried into web ingestion: the pin switch is honoured on the stored membership.
    /// </summary>
    [Fact]
    public async Task PinnedRequestPinsTheDocumentInTheWorkspace()
    {
        await using var harness = await Harness.CreateAsync(directory);
        harness.Fetcher.AddPage(SeedUrl, "Example Home", "Prose worth keeping in context.");

        await harness.Service.IngestAsync(new WebIngestionRequest
        {
            Source = WebIngestionSource.Page,
            WorkspaceId = harness.WorkspaceId,
            Url = SeedUrl,
            Pinned = true,
        });

        Assert.True(Assert.Single(await harness.ListWorkspaceDocumentsAsync()).IsPinned);
    }

    /// <summary>
    /// REQ-RAG-018: a video transcript becomes a workspace document. The reader runs against a
    /// scripted watch page, so the undocumented shape it depends on is pinned by a test.
    /// </summary>
    [Fact]
    public async Task YouTubeTranscriptIsIngestedIntoTheWorkspace()
    {
        await using var harness = await Harness.CreateAsync(directory);

        var outcome = await harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Video, "https://www.youtube.com/watch?v=dQw4w9WgXcQ"));

        var ingested = Assert.Single(outcome.Ingested);
        Assert.Empty(outcome.Skipped);
        Assert.Equal("A Talk About Vectors", ingested.Name);
        Assert.Equal(ingested.DocumentId, Assert.Single(await harness.ListWorkspaceDocumentsAsync()).DocumentId);
    }

    /// <summary>
    /// REQ-RAG-018: a video with no caption track is refused with a reason, not ingested empty.
    /// </summary>
    [Fact]
    public async Task VideoWithoutCaptionsIsReportedNotIngestedEmpty()
    {
        await using var harness = await Harness.CreateAsync(directory, captions: false);

        var outcome = await harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Video, "https://www.youtube.com/watch?v=dQw4w9WgXcQ"));

        Assert.Empty(outcome.Ingested);
        Assert.Contains("captions", Assert.Single(outcome.Skipped).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await harness.ListWorkspaceDocumentsAsync());
    }

    /// <summary>An invalid request never reaches the network, whatever a programmatic caller passes.</summary>
    [Fact]
    public async Task ServiceRefusesARequestTheFormWouldHaveRefused()
    {
        await using var harness = await Harness.CreateAsync(directory);

        await Assert.ThrowsAsync<WebFetchException>(() =>
            harness.Service.IngestAsync(harness.Request(WebIngestionSource.Site, "not-a-url")));

        Assert.Empty(harness.Fetcher.RequestedUrls);
    }

    /// <summary>Removes the temporary store.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail a test run.
        }
    }

    /// <summary>An <see cref="IProgress{T}"/> that reports on the calling thread, so tests can assert.</summary>
    private sealed class SynchronousProgress : IProgress<WebIngestionProgress>
    {
        private readonly Action<WebIngestionProgress> onReport;

        public SynchronousProgress(Action<WebIngestionProgress> onReport) => this.onReport = onReport;

        public void Report(WebIngestionProgress value) => onReport(value);
    }

    /// <summary>
    /// The real service over a real RAG instance, a real workspace store and a scripted network.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ITechieRag rag;
        private readonly WorkspaceManager manager;

        private Harness(
            ITechieRag rag,
            WorkspaceManager manager,
            string workspaceId,
            ScriptedFetcher fetcher,
            RecordingFetcherFactory fetcherFactory,
            IWebIngestionService service)
        {
            this.rag = rag;
            this.manager = manager;
            WorkspaceId = workspaceId;
            Fetcher = fetcher;
            FetcherFactory = fetcherFactory;
            Service = service;
        }

        public string WorkspaceId { get; }

        public ScriptedFetcher Fetcher { get; }

        public RecordingFetcherFactory FetcherFactory { get; }

        public IWebIngestionService Service { get; }

        public static async Task<Harness> CreateAsync(string directory, bool captions = true)
        {
            var root = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            var rag = new TechieRagBuilder()
                .UseCustomEmbeddingProvider(() => new StubEmbeddingProvider())
                .UseVectorStore(VectorStoreType.SqliteVec, $"Data Source={Path.Combine(root, "vectors.db")}")
                .WithPersistence(StoreProvider.Sqlite, $"Data Source={Path.Combine(root, "rag.db")}")
                .Build();

            await rag.InitializeAsync();

            var manager = rag.GetWorkspaceManager()
                ?? throw new InvalidOperationException("The builder produced no workspace manager.");
            var workspace = await manager.CreateWorkspaceAsync(WorkspaceName);

            var fetcher = new ScriptedFetcher();
            var fetcherFactory = new RecordingFetcherFactory(fetcher);

            // The PRODUCTION linker, over a manager stub that hands it the real workspace manager.
            var linker = new WorkspaceDocumentLinker(
                new StubRagManager(manager, root), NullLogger<WorkspaceDocumentLinker>.Instance);

            var service = new WebIngestionService(
                rag,
                fetcherFactory,
                linker,
                new YouTubeTranscriptReader(new HttpClient(new StubHttpMessageHandler(
                    (request, _) => YouTubeResponse(request, captions)))),
                Localize,
                NullLogger<WebIngestionService>.Instance);

            return new Harness(rag, manager, workspace.WorkspaceId, fetcher, fetcherFactory, service);
        }

        public WebIngestionRequest Request(
            WebIngestionSource source, string url, int maxDepth = 1, int maxPages = 25) => new()
        {
            Source = source,
            WorkspaceId = WorkspaceId,
            Url = url,
            MaxDepth = maxDepth,
            MaxPages = maxPages,
        };

        public async Task<IReadOnlyList<TechieRag.Models.WorkspaceDocument>> ListWorkspaceDocumentsAsync() =>
            await manager.GetStore().ListDocumentsAsync(WorkspaceId);

        public ValueTask DisposeAsync()
        {
            (rag as IDisposable)?.Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>Serves a YouTube watch page and its caption track without a network.</summary>
        private static HttpResponseMessage YouTubeResponse(HttpRequestMessage request, bool captions)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("timedtext", StringComparison.Ordinal))
            {
                return Text(
                    """
                    <transcript>
                      <text start="0" dur="2">Vectors are just lists of numbers.</text>
                      <text start="2" dur="3">The interesting part is what the distances mean.</text>
                    </transcript>
                    """);
            }

            var tracks = captions
                ? @"""captionTracks"":[{""baseUrl"":""https://www.youtube.com/api/timedtext?v=dQw4w9WgXcQ"",""languageCode"":""en"",""kind"":""asr""}],"
                : @"""playerCaptionsTracklistRenderer"":{},";

            return Text("<html><head><title>A Talk About Vectors - YouTube</title></head>"
                        + "<body><script>var data = {" + tracks + "\"x\":1};</script></body></html>");
        }

        private static HttpResponseMessage Text(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html"),
        };
    }

    /// <summary>
    /// A <see cref="TechieRagManager"/> that hands out a workspace manager built over a temporary
    /// store, so the production linker can be exercised without an embedding model.
    /// </summary>
    private sealed class StubRagManager : TechieRagManager
    {
        private readonly WorkspaceManager manager;

        public StubRagManager(WorkspaceManager manager, string contentRoot)
            : base(
                new AppEnvironment(contentRoot),
                NullLoggerFactory.Instance,
                NullLogger<TechieRagManager>.Instance,
                DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(contentRoot, "keys"))),
                new ConfigurationBuilder().Build())
        {
            this.manager = manager;
        }

        public override Task<WorkspaceManager?> GetWorkspaceManagerAsync() =>
            Task.FromResult<WorkspaceManager?>(manager);
    }

    /// <summary>Records the private-network policy each run asked for, then hands over the fetcher.</summary>
    private sealed class RecordingFetcherFactory : IWebContentFetcherFactory
    {
        private readonly IWebContentFetcher fetcher;

        public RecordingFetcherFactory(IWebContentFetcher fetcher) => this.fetcher = fetcher;

        public bool? LastBlockedPrivateTargets { get; private set; }

        public IWebContentFetcher Create(bool blockPrivateNetworkTargets)
        {
            LastBlockedPrivateTargets = blockPrivateNetworkTargets;
            return fetcher;
        }
    }

    /// <summary>
    /// The network, scripted. This is the seam <see cref="IWebContentFetcher"/> exists for: every
    /// crawl rule can be exercised without a socket.
    /// </summary>
    private sealed class ScriptedFetcher : IWebContentFetcher
    {
        private readonly Dictionary<string, WebPage> pages = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> failures = new(StringComparer.OrdinalIgnoreCase);

        public List<string> RequestedUrls { get; } = new();

        public void AddPage(string url, string title, string text, params string[] links) =>
            pages[url] = new WebPage(url, url, title, text, links);

        public void AddFailure(string url, string reason) => failures[url] = reason;

        public Task<WebPage> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            RequestedUrls.Add(url);

            if (failures.TryGetValue(url, out var reason))
            {
                throw new WebFetchException(url, reason);
            }

            return pages.TryGetValue(url, out var page)
                ? Task.FromResult(page)
                : throw new WebFetchException(url, $"{url} was not scripted for this test.");
        }
    }

    /// <summary>
    /// An embedding provider with no model behind it. The vectors are deterministic and non-zero —
    /// an all-zero vector normalises to NaN in some stores and would fail for reasons unrelated to
    /// anything under test.
    /// </summary>
    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        public string Name => "Stub";

        public string ModelName => "stub";

        public int Dimensions => 8;

        public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            OnEmbeddingCompleted?.Invoke(this, null!);
            return Task.FromResult(Vector(text));
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
            IEnumerable<string> texts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Vector).ToList());

        private float[] Vector(string text)
        {
            var vector = new float[Dimensions];
            for (var index = 0; index < Dimensions; index++)
            {
                vector[index] = ((text.Length + index) % 7 + 1) / 10f;
            }

            return vector;
        }
    }
}
