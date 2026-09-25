using TechieRag.Connectors;
using TechieRag.Connectors.Confluence;
using TechieRag.Connectors.Repository;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-079 / BRD-63 and REQ-RAG-080 / BRD-64 acceptance: a repository and a Confluence space run
/// through <c>IngestConnectorAsync</c> end to end over a scripted transport, observing what is ingested
/// and every request that was sent.
/// </summary>
public sealed class SourceConnectorIngestionTests
{
    private const string ConfluenceBase = "https://acme.example.test/wiki";

    /// <summary>
    /// Acceptance of REQ-RAG-079: a repository connected on branch <c>release</c> with the glob
    /// <c>*.md</c> ingests only the markdown file, reads the tree of that branch only, and never fetches
    /// the content of a file the glob excludes.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-079 RepositoryIngestsOnlyMatchingFilesOnTheBranch")]
    public async Task RepositoryIngestsOnlyMatchingFilesOnTheBranch()
    {
        var transport = new FakeConnectorTransport()
            .Route("/trees/", Tree(("docs/guide.md", "sha1", 10), ("src/app.cs", "sha2", 20), ("package-lock.json", "sha3", 30)))
            .Route("/blobs/sha1", "{\"content\":\"aGVsbG8gd29ybGQ=\",\"encoding\":\"base64\"}")
            .Route("/blobs/", "{\"content\":\"c2hvdWxkIG5vdCBiZSByZWFk\",\"encoding\":\"base64\"}");
        var connector = new RepositoryConnector(transport, new RepositoryConnectorOptions
        {
            Host = RepositoryHost.GitHub,
            ProjectPath = "acme/tools",
            Branch = "release",
            IncludeGlobs = ["*.md"]
        });
        var rag = new RecordingRag();

        var result = await rag.IngestConnectorAsync(connector, options: NoDelay());

        var ingested = Assert.Single(rag.Ingested);
        Assert.Single(result.DocumentIds);
        Assert.Equal("hello world", ingested.Text);
        var treeRequests = transport.Requests.Where(r => r.Url.Contains("/trees/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(treeRequests);
        Assert.All(treeRequests, r => Assert.Contains("/trees/release", r.Url, StringComparison.Ordinal));
        Assert.DoesNotContain(transport.Requests, r => r.Url.Contains("sha2", StringComparison.Ordinal) || r.Url.Contains("sha3", StringComparison.Ordinal));
    }

    /// <summary>
    /// Acceptance of REQ-RAG-080: connecting a Confluence space ingests each of its pages as text, and
    /// every request the run sends goes to the configured scheme, host and port.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-080 ConfluenceSpaceIsIngestedWithoutLeavingTheHost")]
    public async Task ConfluenceSpaceIsIngestedWithoutLeavingTheHost()
    {
        var transport = new FakeConnectorTransport()
            .Route("expand=body.storage", "{\"id\":\"1\",\"title\":\"Runbook\",\"body\":{\"storage\":{\"value\":\"<p>Restart the <strong>worker</strong> nightly.</p>\"}}}")
            .Route("spaceKey=ENG", Listing(("1", "Runbook"), ("2", "Onboarding")));
        var connector = new ConfluenceConnector(transport, new ConfluenceConnectorOptions { BaseUrl = ConfluenceBase, SpaceKey = "ENG" });
        var rag = new RecordingRag();

        var result = await rag.IngestConnectorAsync(connector, options: NoDelay());

        Assert.Equal(2, result.DocumentIds.Count);
        Assert.Equal(["Runbook", "Onboarding"], rag.Ingested.Select(entry => entry.Name));
        Assert.All(rag.Ingested, entry =>
        {
            Assert.Contains("Restart the", entry.Text, StringComparison.Ordinal);
            Assert.Contains("worker", entry.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("<strong>", entry.Text, StringComparison.Ordinal);
        });
        Assert.NotEmpty(transport.Requests);
        var configured = new Uri(ConfluenceBase);
        Assert.All(transport.Requests, request =>
        {
            var uri = new Uri(request.Url);
            Assert.Equal(configured.Scheme, uri.Scheme);
            Assert.Equal(configured.Host, uri.Host);
            Assert.Equal(configured.Port, uri.Port);
        });
    }

    private static ConnectorRunOptions NoDelay() => new() { RequestDelay = TimeSpan.Zero };

    private static string Tree(params (string Path, string Sha, int Size)[] files)
    {
        var entries = files.Select(f => $"{{\"path\":\"{f.Path}\",\"type\":\"blob\",\"sha\":\"{f.Sha}\",\"size\":{f.Size}}}");
        return $"{{\"tree\":[{string.Join(",", entries)}],\"truncated\":false}}";
    }

    private static string Listing(params (string Id, string Title)[] pages)
    {
        var results = pages.Select(p =>
            $"{{\"id\":\"{p.Id}\",\"title\":\"{p.Title}\",\"type\":\"page\","
            + "\"version\":{\"number\":1,\"when\":\"2026-01-02T03:04:05.000Z\"},"
            + $"\"space\":{{\"key\":\"ENG\"}},\"_links\":{{\"webui\":\"/spaces/ENG/pages/{p.Id}\"}}}}");
        return $"{{\"results\":[{string.Join(",", results)}],\"_links\":{{\"base\":\"{ConfluenceBase}\"}}}}";
    }
}
