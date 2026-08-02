using TechieRag.Connectors;
using TechieRag.Connectors.Http;
using TechieRag.Connectors.Repository;
using TechieRag.Tests.Web.Live;
using Xunit;

namespace TechieRag.Tests.Connectors.Live;

/// <summary>
/// REQ-RAG-019 / BRD-63: the repository connector against a real public repository.
/// </summary>
/// <remarks>
/// <para><b>What the hermetic suite cannot prove.</b> Every other repository test drives a fake
/// transport whose JSON the test author wrote, which proves the paging, filtering and decoding logic
/// and proves nothing about the API's actual shape — that the tree endpoint really nests entries
/// under <c>tree</c>, that a blob really comes back base64 in a <c>content</c> field, that the
/// default branch really appears as <c>default_branch</c>. A connector can be entirely correct
/// against a fixture and entirely wrong against the host.</para>
/// <para><b>The target.</b> <c>octocat/Spoon-Knife</c> is GitHub's own demonstration repository,
/// published expressly so people have something to fork and experiment against. It is three files —
/// one Markdown, one HTML, one CSS — which is what makes it a real test of glob filtering rather
/// than a large download.</para>
/// <para><b>Anonymous.</b> No token is supplied, so this reads at the unauthenticated rate limit.
/// That is deliberate: needing a credential to run the suite is how a live test quietly stops being
/// run.</para>
/// </remarks>
[Trait("Category", LiveNetworkFactAttribute.CategoryName)]
public sealed class LiveRepositoryConnectorTests : IDisposable
{
    private const string PublicRepository = "octocat/Spoon-Knife";

    private readonly HttpClient httpClient = HttpConnectorTransport.CreateDefaultClient();

    /// <summary>
    /// A glob-filtered listing of a real repository returns the file that matches and genuinely
    /// omits the ones that do not.
    /// </summary>
    /// <remarks>
    /// The absence assertion is the point. A filter that returns the right file while also returning
    /// everything else is not a filter, and on a real repository "everything else" is the lockfiles
    /// and vendored blobs the glob exists to keep out of the index.
    /// </remarks>
    [LiveNetworkFact]
    public async Task ListsOnlyGlobMatchedFilesFromARealRepository()
    {
        var connector = Connector(options => options.IncludeGlobs = ["**/*.md"]);

        var page = await connector.ListAsync(new ConnectorListRequest());
        var paths = page.Items.Select(item => item.Id).ToList();

        Assert.Contains("README.md", paths);
        Assert.DoesNotContain("index.html", paths);
        Assert.DoesNotContain("styles.css", paths);
    }

    /// <summary>A matched file fetches back its real content, decoded to text.</summary>
    [LiveNetworkFact]
    public async Task FetchesRealContentForAMatchedFile()
    {
        var connector = Connector(options => options.IncludeGlobs = ["**/*.md"]);

        var page = await connector.ListAsync(new ConnectorListRequest());
        var readme = page.Items.Single(item => item.Id == "README.md");
        var document = await connector.FetchAsync(readme);

        Assert.False(string.IsNullOrWhiteSpace(document.Text));
        Assert.Contains("Spoon-Knife", document.Text, StringComparison.OrdinalIgnoreCase);

        // The blob SHA is what makes an incremental run exact, so it has to actually arrive.
        Assert.False(string.IsNullOrWhiteSpace(readme.Version));

        // A citation has to point at something a human can open.
        Assert.StartsWith("https://github.com/", readme.SourceUrl, StringComparison.Ordinal);
    }

    /// <summary>An explicit branch is honoured, and a branch that does not exist says so.</summary>
    /// <remarks>
    /// The 404-to-message mapping is worth proving live because it is the error an operator hits
    /// most: a repository still on <c>master</c> configured as <c>main</c>.
    /// </remarks>
    [LiveNetworkFact]
    public async Task SelectsAnExplicitBranchAndNamesAMissingOneHonestly()
    {
        var onMain = Connector(options =>
        {
            options.Branch = "main";
            options.IncludeGlobs = ["**/*.md"];
        });

        var page = await onMain.ListAsync(new ConnectorListRequest());
        Assert.Contains("README.md", page.Items.Select(item => item.Id));

        var onNonsense = Connector(options => options.Branch = "no-such-branch-exists-here");

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => onNonsense.ListAsync(new ConnectorListRequest()));

        Assert.Equal(404, error.StatusCode);
        Assert.Contains("no-such-branch-exists-here", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An end-to-end run ingests the filtered files and reports state for the next run.</summary>
    /// <remarks>
    /// Drives <see cref="ConnectorRunner"/> over the real host, so the budgets, the per-item failure
    /// model and the sync state are exercised against real listings rather than a fixture.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RunsEndToEndAgainstTheRealRepository()
    {
        var connector = Connector(options => options.IncludeGlobs = ["**/*.md", "**/*.html"]);

        var result = await new ConnectorRunner().RunAsync(
            connector,
            previousSync: null,
            new ConnectorRunOptions { RequestDelay = TimeSpan.FromMilliseconds(200) });

        Assert.Empty(result.Failures);
        Assert.False(result.ReachedLimit);
        Assert.Equal(
            ["README.md", "index.html"],
            result.Documents.Select(d => d.Item.Id).OrderBy(id => id, StringComparer.Ordinal));

        // Every fetched item must be resumable, or the second run re-downloads the repository.
        Assert.All(result.Documents, d => Assert.True(result.Sync.ItemVersions.ContainsKey(d.Item.Id)));

        // A second run with that state fetches nothing, which is the whole point of holding it.
        var second = await new ConnectorRunner().RunAsync(
            Connector(options => options.IncludeGlobs = ["**/*.md", "**/*.html"]),
            result.Sync,
            new ConnectorRunOptions { RequestDelay = TimeSpan.Zero });

        Assert.Empty(second.Documents);
    }

    /// <summary>
    /// A public DNS name that resolves to loopback is refused, on the connector transport, at
    /// connect time.
    /// </summary>
    /// <remarks>
    /// <para>This is the bypass that was actually proven walkable: nothing in
    /// <c>127.0.0.1.nip.io</c> looks private, so the textual host check passes it and the request
    /// lands on loopback. Only a live DNS lookup can demonstrate it, which is why the hermetic guard
    /// tests cannot cover this case and this one exists.</para>
    /// <para>It matters more here than for the crawler: this transport attaches the source's
    /// <c>Authorization</c> header to whatever it connects to.</para>
    /// </remarks>
    [LiveNetworkFact]
    public async Task PublicHostnameResolvingToLoopbackIsRefused()
    {
        var transport = new HttpConnectorTransport(httpClient);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => transport.GetAsync(new ConnectorHttpRequest(
                $"http://{LiveTargets.HostnameResolvingToLoopback}/admin")));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose() => httpClient.Dispose();

    private RepositoryConnector Connector(Action<RepositoryConnectorOptions>? configure = null)
    {
        var options = new RepositoryConnectorOptions
        {
            Host = RepositoryHost.GitHub,
            ProjectPath = PublicRepository,
        };

        configure?.Invoke(options);

        // The rate-limit decorator is part of what is being proved: anonymous access to this API is
        // the case most likely to be throttled on a shared build agent.
        return new RepositoryConnector(
            new RateLimitedTransport(new HttpConnectorTransport(httpClient)),
            options);
    }
}
