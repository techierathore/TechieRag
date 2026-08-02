using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TechieDesk.Services;
using TechieDesk.Services.Hosting;
using TechieDesk.Services.Web;
using TechieRag;
using TechieRag.Embedded;
using TechieRag.Models;
using TechieRag.Services;
using TechieRag.Web;
using Xunit;

namespace TechieDesk.Tests.Web.Live;

/// <summary>
/// REQ-RAG-016/017/018: "Add from web" driven the whole way — real URL, real HTTP, real embedding
/// model, real vector store, and then asked to find what it just ingested.
/// </summary>
/// <remarks>
/// <para><b>Nothing is faked.</b> The hermetic suite fakes the network and the embedder, which is
/// right for testing crawl rules and skip reasons and wrong for answering the only question that
/// matters to a user: after clicking "Add from web", can they find the content. This drives the same
/// stack the desktop app runs — <c>UseEmbedded()</c> (local BGE-M3 ONNX) and SQLite-vec, the
/// application's own defaults — so it needs neither Ollama nor Qdrant.</para>
/// <para><b>Ingestion is not the assertion; retrieval is.</b> A run that reports "ingested 3
/// documents" and then returns nothing for a query about their contents has produced exactly the
/// empty library a user would file a bug about. Every success case here ends with a search whose
/// result text has to contain something that was only ever on the fetched page.</para>
/// <para><b>The client is the real one.</b> The fetcher comes out of
/// <c>AddTechieDeskWebIngestion</c> through the real <see cref="IWebContentFetcherFactory"/>, so the
/// SSRF guard being exercised is the one the product actually registers, not one the test built.</para>
/// </remarks>
[Trait("Category", LiveNetworkFactAttribute.CategoryName)]
public sealed class LiveWebIngestionEndToEndTests : IDisposable
{
    private const string SinglePageUrl = "https://example.com/";
    private const string CrawlSeedUrl = "https://quotes.toscrape.com/";
    private const string VideoUrl = "https://www.youtube.com/watch?v=aircAruvnKk";

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"techiedesk-liveweb-{Guid.NewGuid():N}");

    /// <summary>
    /// REQ-RAG-016: a real page is fetched, embedded, added to the workspace, and then found by a
    /// search that only its own content could answer.
    /// </summary>
    [LiveNetworkFact]
    public async Task RealPageIsIngestedEmbeddedAndRetrievable()
    {
        await using var harness = await Harness.CreateAsync(directory);

        var outcome = await harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Page, SinglePageUrl));

        Assert.Empty(outcome.Skipped);
        var ingested = Assert.Single(outcome.Ingested);
        Assert.Contains("Example Domain", ingested.Name, StringComparison.OrdinalIgnoreCase);

        // In the workspace, not merely in the global catalogue. This is what the Documents screen reads.
        var inWorkspace = await harness.ListWorkspaceDocumentsAsync();
        Assert.Equal(ingested.DocumentId, Assert.Single(inWorkspace).DocumentId);

        // The point of the whole exercise: it comes back out.
        var results = await harness.Rag.SearchAsync("what is this domain reserved for", topK: 3);
        Assert.NotEmpty(results);
        Assert.Contains(
            results,
            result => result.Chunk.Text.Contains("This domain is for use in", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// REQ-RAG-017: a bounded crawl of a real site puts every page it fetched into the workspace,
    /// and their text is retrievable.
    /// </summary>
    [LiveNetworkFact]
    public async Task RealSiteCrawlIsIngestedAndRetrievable()
    {
        await using var harness = await Harness.CreateAsync(directory);

        var outcome = await harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Site, CrawlSeedUrl, maxDepth: 1, maxPages: 3));

        Assert.True(
            outcome.Ingested.Count >= 2,
            $"Expected the crawl to ingest at least 2 pages, got {outcome.Ingested.Count}. "
            + $"Skipped: {string.Join("; ", outcome.Skipped.Select(s => $"{s.Url} — {s.Reason}"))}");

        // The count on screen and the rows in the workspace must be the same number.
        var inWorkspace = await harness.ListWorkspaceDocumentsAsync();
        Assert.Equal(outcome.Ingested.Count, inWorkspace.Count);

        // Every source URL is on the seed host — the same-host bound survived the whole pipeline.
        Assert.All(outcome.Ingested, document => Assert.Contains(
            "quotes.toscrape.com", document.SourceUrl, StringComparison.OrdinalIgnoreCase));

        var results = await harness.Rag.SearchAsync(
            "the world as we have created it is a process of our thinking", topK: 5);
        Assert.NotEmpty(results);
        Assert.Contains(
            results,
            result => result.Chunk.Text.Contains("Albert Einstein", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// REQ-RAG-018: when the transcript cannot be read, the run says so and adds nothing — it does
    /// not put an empty document in the library.
    /// </summary>
    /// <remarks>
    /// <b>This currently exercises a real outage, not a hypothetical.</b> As of 2026-07-27 YouTube
    /// serves every watch-page-derived caption URL as HTTP 200 with a zero-byte body (TR-RAG-015), so
    /// no video ingests. The requirement being asserted is the one that still has to hold while that
    /// is true: fail loudly, name the reason, and leave the workspace clean. An empty document here
    /// would be worse than the failure, because it would look like a successful ingest of a video in
    /// which nobody spoke.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RealVideoIngestionEitherYieldsATranscriptOrAddsNothingAtAll()
    {
        await using var harness = await Harness.CreateAsync(directory);

        var outcome = await harness.Service.IngestAsync(
            harness.Request(WebIngestionSource.Video, VideoUrl));

        var inWorkspace = await harness.ListWorkspaceDocumentsAsync();

        if (outcome.Ingested.Count == 0)
        {
            var skipped = Assert.Single(outcome.Skipped);
            Assert.Contains("caption", skipped.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.False(outcome.Succeeded);
            Assert.DoesNotContain("Ingested", outcome.Summary, StringComparison.Ordinal);

            // Nothing half-written: no empty document left behind for the user to find.
            Assert.Empty(inWorkspace);
            return;
        }

        // If YouTube starts serving transcripts again, the success path must be a real one.
        var ingested = Assert.Single(outcome.Ingested);
        Assert.Equal(ingested.DocumentId, Assert.Single(inWorkspace).DocumentId);

        var results = await harness.Rag.SearchAsync("what is a neural network", topK: 3);
        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.False(string.IsNullOrWhiteSpace(result.Chunk.Text)));
    }

    /// <summary>
    /// The SSRF guard holds through the application's own registered client: a public hostname that
    /// resolves to loopback is refused, and the internal service is never contacted.
    /// </summary>
    /// <remarks>
    /// <para>The URL passes <see cref="WebIngestionRequest.Validate"/> — nothing in
    /// <c>127.0.0.1.nip.io</c> looks private — so this is the case where the request-level check
    /// cannot help and the transport-level guard is the only thing standing there.</para>
    /// <para>The listener's request count is the assertion that matters. Refusing the body after the
    /// internal request has gone out still lets an attacker use the app to reach an endpoint that
    /// does something.</para>
    /// </remarks>
    [LiveNetworkFact]
    public async Task PrivateNetworkTargetBehindAPublicHostnameIsRefusedEndToEnd()
    {
        await using var harness = await Harness.CreateAsync(directory);
        using var listener = LoopbackServer.Start();

        var request = harness.Request(WebIngestionSource.Page, listener.DisguisedUrl);
        Assert.Null(request.Validate());

        var outcome = await harness.Service.IngestAsync(request);

        Assert.Empty(outcome.Ingested);
        var skipped = Assert.Single(outcome.Skipped);
        Assert.Contains("private-network", skipped.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, listener.RequestCount);
        Assert.Empty(await harness.ListWorkspaceDocumentsAsync());
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

    /// <summary>
    /// The whole shipping stack: real embedder, real vector store, real HTTP, real workspace store.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private const string WorkspaceName = "Live web research";

        private readonly ServiceProvider provider;
        private readonly WorkspaceManager manager;

        private Harness(
            ITechieRag rag,
            ServiceProvider provider,
            WorkspaceManager manager,
            string workspaceId,
            IWebIngestionService service)
        {
            Rag = rag;
            this.provider = provider;
            this.manager = manager;
            WorkspaceId = workspaceId;
            Service = service;
        }

        /// <summary>Gets the RAG instance, so tests can search what was ingested.</summary>
        public ITechieRag Rag { get; }

        /// <summary>Gets the workspace the run targets.</summary>
        public string WorkspaceId { get; }

        /// <summary>Gets the service under test.</summary>
        public IWebIngestionService Service { get; }

        /// <summary>Builds the stack over a fresh temporary directory.</summary>
        /// <param name="directory">Root for the vector store, catalogue and data-protection keys.</param>
        /// <returns>A ready harness.</returns>
        public static async Task<Harness> CreateAsync(string directory)
        {
            var root = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            // Exactly what TechieRagManager builds for a stock install: embedded ONNX + SQLite-vec.
            // No Ollama, no Qdrant, nothing this host has to be running.
            var rag = new TechieRagBuilder()
                .UseEmbedded()
                .UseVectorStore(VectorStoreType.SqliteVec, $"Data Source={Path.Combine(root, "vectors.db")}")
                .WithPersistence(StoreProvider.Sqlite, $"Data Source={Path.Combine(root, "rag.db")}")
                .Build();

            await rag.InitializeAsync();

            var manager = rag.GetWorkspaceManager()
                ?? throw new InvalidOperationException("The builder produced no workspace manager.");
            var workspace = await manager.CreateWorkspaceAsync(WorkspaceName);

            // The REAL registration, so the fetcher and its SSRF-guarded handler are the product's.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddTechieDeskWebIngestion();
            var provider = services.BuildServiceProvider();

            var linker = new WorkspaceDocumentLinker(
                new WorkspaceOnlyRagManager(manager, root), NullLogger<WorkspaceDocumentLinker>.Instance);

            var service = new WebIngestionService(
                rag,
                provider.GetRequiredService<IWebContentFetcherFactory>(),
                linker,
                provider.GetRequiredService<YouTubeTranscriptReader>(),
                NullLogger<WebIngestionService>.Instance);

            return new Harness(rag, provider, manager, workspace.WorkspaceId, service);
        }

        /// <summary>Builds a request against this harness's workspace.</summary>
        /// <param name="source">Page, site or video.</param>
        /// <param name="url">The URL to ingest.</param>
        /// <param name="maxDepth">Crawl depth.</param>
        /// <param name="maxPages">Crawl page budget.</param>
        /// <returns>The request.</returns>
        public WebIngestionRequest Request(
            WebIngestionSource source, string url, int maxDepth = 1, int maxPages = 3) => new()
        {
            Source = source,
            WorkspaceId = WorkspaceId,
            Url = url,
            MaxDepth = maxDepth,
            MaxPages = maxPages,
        };

        /// <summary>Lists what the Documents screen would show for this workspace.</summary>
        /// <returns>The workspace's document rows.</returns>
        public async Task<IReadOnlyList<WorkspaceDocument>> ListWorkspaceDocumentsAsync() =>
            await manager.GetStore().ListDocumentsAsync(WorkspaceId);

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            (Rag as IDisposable)?.Dispose();
            await provider.DisposeAsync();
        }
    }

    /// <summary>
    /// A <see cref="TechieRagManager"/> that supplies the real workspace manager and nothing else.
    /// </summary>
    /// <remarks>
    /// The production linker takes the manager, not the workspace store, and the manager's normal
    /// construction path reads saved application configuration. This narrows it to the one dependency
    /// the linker uses, so the linker under test is the shipping one.
    /// </remarks>
    private sealed class WorkspaceOnlyRagManager : TechieRagManager
    {
        private readonly WorkspaceManager manager;

        public WorkspaceOnlyRagManager(WorkspaceManager manager, string contentRoot)
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

    /// <summary>
    /// A real HTTP server on loopback, advertised under a public hostname that resolves to it.
    /// </summary>
    /// <remarks>
    /// <c>nip.io</c> is a public wildcard DNS service: <c>127.0.0.1.nip.io</c> resolves to
    /// 127.0.0.1. It is what makes this a real bypass attempt rather than a simulated one — the URL
    /// is genuinely public-looking and the resolution genuinely happens.
    /// </remarks>
    private sealed class LoopbackServer : IDisposable
    {
        private const string ResponseBody =
            "<html><head><title>Admin</title></head>"
            + "<body><p>Internal service you were never supposed to reach.</p></body></html>";

        private readonly HttpListener listener;
        private int requestCount;

        private LoopbackServer(HttpListener listener, int port)
        {
            this.listener = listener;
            DisguisedUrl = $"http://127.0.0.1.nip.io:{port}/";
        }

        /// <summary>Gets the public-looking URL that resolves to this listener.</summary>
        public string DisguisedUrl { get; }

        /// <summary>Gets how many requests actually arrived.</summary>
        public int RequestCount => Volatile.Read(ref requestCount);

        public static LoopbackServer Start()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            var server = new LoopbackServer(listener, port);
            _ = server.AcceptLoopAsync();
            return server;
        }

        public void Dispose() => listener.Close();

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Disposal races the accept loop; a closed listener is the expected end state.
                    return;
                }

                Interlocked.Increment(ref requestCount);

                var bytes = System.Text.Encoding.UTF8.GetBytes(ResponseBody);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                context.Response.Close();
            }
        }
    }
}
