using TechieRag.Connectors;
using TechieRag.Connectors.Repository;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-019 / BRD-63: branch selection, glob filtering, paging and honest failures across both
/// hosted-repository APIs — none of which needs a network or an account to prove.
/// </summary>
public sealed class RepositoryConnectorTests
{
    private const string HelloBase64 = "aGVsbG8gd29ybGQ=";

    /// <summary>A repository with no branch named is read on the branch the host calls default.</summary>
    [Fact]
    public async Task ResolvesTheDefaultBranchWhenNoneIsGiven()
    {
        var transport = new FakeConnectorTransport()
            .Route("/trees/", Tree(("docs/a.md", "sha1", 10)))
            .Route("/repos/acme/tools", "{\"default_branch\":\"develop\"}");

        var connector = Connector(transport, RepositoryHost.GitHub, o => o.Branch = null);
        await connector.ListAsync(new ConnectorListRequest());

        Assert.Contains(transport.Requests, r => r.Url.Contains("/trees/develop", StringComparison.Ordinal));
        Assert.Contains("develop", connector.SourceName, StringComparison.Ordinal);
    }

    /// <summary>Only files matching an include glob are listed, so the rest are never fetched.</summary>
    [Fact]
    public async Task ListsOnlyFilesMatchingIncludeGlobs()
    {
        var transport = GitHubTransport(
            ("docs/a.md", "sha1", 10),
            ("src/app.cs", "sha2", 20),
            ("package-lock.json", "sha3", 30));

        var connector = Connector(transport, RepositoryHost.GitHub, options => options.IncludeGlobs = ["*.md"]);
        var page = await connector.ListAsync(new ConnectorListRequest());

        var item = Assert.Single(page.Items);
        Assert.Equal("docs/a.md", item.Id);
    }

    /// <summary>An exclude glob removes a file that an include would otherwise admit.</summary>
    [Fact]
    public async Task ExcludeGlobRemovesAFile()
    {
        var transport = GitHubTransport(("docs/a.md", "sha1", 10), ("docs/CHANGELOG.md", "sha2", 20));

        var connector = Connector(transport, RepositoryHost.GitHub, options =>
        {
            options.IncludeGlobs = ["**/*.md"];
            options.ExcludeGlobs = ["CHANGELOG.md"];
        });

        var page = await connector.ListAsync(new ConnectorListRequest());

        Assert.Equal(["docs/a.md"], page.Items.Select(i => i.Id));
    }

    /// <summary>Directory entries in a tree are not documents and are not listed.</summary>
    [Fact]
    public async Task IgnoresTreeEntriesThatAreNotFiles()
    {
        var transport = new FakeConnectorTransport()
            .Route("/trees/", "{\"tree\":[{\"path\":\"docs\",\"type\":\"tree\",\"sha\":\"d1\"}],\"truncated\":false}");

        var page = await Connector(transport, RepositoryHost.GitHub).ListAsync(new ConnectorListRequest());

        Assert.Empty(page.Items);
    }

    /// <summary>The token travels in a header, never in the URL where proxies and logs would keep it.</summary>
    [Fact]
    public async Task SendsTheTokenInAHeaderNotTheUrl()
    {
        var transport = GitHubTransport(("docs/a.md", "sha1", 10));

        await Connector(transport, RepositoryHost.GitHub, o => o.AccessToken = "secret-token-value")
            .ListAsync(new ConnectorListRequest());

        var request = transport.Requests[0];
        Assert.DoesNotContain("secret-token-value", request.Url, StringComparison.Ordinal);
        Assert.Equal("Bearer secret-token-value", request.Headers!["Authorization"]);
    }

    /// <summary>The other host's token header is the one that host expects.</summary>
    [Fact]
    public async Task UsesThePrivateTokenHeaderOnTheOtherHost()
    {
        var transport = new FakeConnectorTransport().Route("/repository/tree", "[]");

        await Connector(transport, RepositoryHost.GitLab, o => o.AccessToken = "glpat-value")
            .ListAsync(new ConnectorListRequest());

        var request = transport.Requests[0];
        Assert.DoesNotContain("glpat-value", request.Url, StringComparison.Ordinal);
        Assert.Equal("glpat-value", request.Headers!["PRIVATE-TOKEN"]);
    }

    /// <summary>
    /// A tree the host truncated is reported. A run that ingested an arbitrary prefix of a
    /// repository and called itself complete would be the worst possible outcome for an index.
    /// </summary>
    [Fact]
    public async Task ReportsATruncatedTree()
    {
        var transport = new FakeConnectorTransport()
            .Route("/trees/", "{\"tree\":[{\"path\":\"a.md\",\"type\":\"blob\",\"sha\":\"s\"}],\"truncated\":true}");

        var page = await Connector(transport, RepositoryHost.GitHub).ListAsync(new ConnectorListRequest());

        Assert.Contains(page.Failures!, f => f.Reason.Contains("truncated", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A blob's base64 content is decoded to text.</summary>
    [Fact]
    public async Task DecodesBlobContent()
    {
        var transport = GitHubTransport(("docs/a.md", "sha1", 10))
            .Route("/blobs/sha1", $"{{\"content\":\"{HelloBase64}\",\"encoding\":\"base64\"}}");

        var connector = Connector(transport, RepositoryHost.GitHub);
        var page = await connector.ListAsync(new ConnectorListRequest());
        var document = await connector.FetchAsync(page.Items[0]);

        Assert.Equal("hello world", document.Text);
    }

    /// <summary>
    /// A binary file is refused with a reason rather than ingested. Decoded as text it becomes a
    /// page of replacement characters that embeds into noise and pollutes every later search.
    /// </summary>
    [Fact]
    public async Task RefusesBinaryContent()
    {
        var binary = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x00, 0x4E, 0x47 });
        var transport = GitHubTransport(("logo.png", "sha1", 10))
            .Route("/blobs/sha1", $"{{\"content\":\"{binary}\",\"encoding\":\"base64\"}}");

        var connector = Connector(transport, RepositoryHost.GitHub);
        var page = await connector.ListAsync(new ConnectorListRequest());

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => connector.FetchAsync(page.Items[0]));
        Assert.Contains("binary", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A rejected credential ends the run rather than becoming one file's failure, and the message
    /// does not repeat the token back.
    /// </summary>
    [Fact]
    public async Task RejectedCredentialEndsTheRun()
    {
        var transport = new FakeConnectorTransport().Route("/trees/", "{\"message\":\"Bad credentials\"}", 401);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => Connector(transport, RepositoryHost.GitHub, o => o.AccessToken = "secret-token-value")
                .ListAsync(new ConnectorListRequest()));

        Assert.Equal(401, error.StatusCode);
        Assert.DoesNotContain("secret-token-value", error.Message, StringComparison.Ordinal);
        Assert.Contains("token", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Anonymous access being refused says so, rather than blaming a token nobody supplied.</summary>
    [Fact]
    public async Task NamesAnonymousAccessWhenNoTokenWasSupplied()
    {
        var transport = new FakeConnectorTransport().Route("/trees/", "{}", 403);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => Connector(transport, RepositoryHost.GitHub).ListAsync(new ConnectorListRequest()));

        Assert.Contains("anonymous", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A branch that does not exist is named as such, not reported as an empty repository.</summary>
    [Fact]
    public async Task MissingBranchIsNamedHonestly()
    {
        var transport = new FakeConnectorTransport().Route("/trees/", "{}", 404);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => Connector(transport, RepositoryHost.GitHub).ListAsync(new ConnectorListRequest()));

        Assert.Contains("no branch 'main'", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A file that vanished between listing and fetching costs that file only — it does not raise
    /// the run-level failure type, so the runner records it and continues.
    /// </summary>
    [Fact]
    public async Task MissingFileCostsOnlyThatFile()
    {
        var transport = GitHubTransport(("docs/a.md", "sha1", 10));
        var connector = Connector(transport, RepositoryHost.GitHub);
        var page = await connector.ListAsync(new ConnectorListRequest());

        var error = await Record.ExceptionAsync(() => connector.FetchAsync(page.Items[0]));

        Assert.NotNull(error);
        Assert.IsNotType<ConnectorException>(error);
    }

    /// <summary>The other host pages its tree, and paging follows the header rather than a count.</summary>
    [Fact]
    public async Task PagesTheTreeUsingTheHostsNextPageHeader()
    {
        var transport = new FakeConnectorTransport()
            .Route(
                "&page=1",
                "[{\"id\":\"i1\",\"type\":\"blob\",\"path\":\"a.md\"}]",
                200,
                new Dictionary<string, string> { ["X-Next-Page"] = "2" })
            .Route("&page=2", "[{\"id\":\"i2\",\"type\":\"blob\",\"path\":\"b.md\"}]");

        var connector = Connector(transport, RepositoryHost.GitLab);

        var first = await connector.ListAsync(new ConnectorListRequest());
        Assert.Equal("2", first.NextCursor);

        var second = await connector.ListAsync(new ConnectorListRequest(first.NextCursor));
        Assert.Null(second.NextCursor);
        Assert.Equal(["b.md"], second.Items.Select(i => i.Id));
    }

    /// <summary>
    /// Paging follows the host's header even when every file on a page was filtered out. A
    /// count-based loop would stop at the first fully-excluded page and miss the rest of the repo.
    /// </summary>
    [Fact]
    public async Task ContinuesPagingPastAFullyFilteredPage()
    {
        var transport = new FakeConnectorTransport()
            .Route(
                "&page=1",
                "[{\"id\":\"i1\",\"type\":\"blob\",\"path\":\"a.bin\"}]",
                200,
                new Dictionary<string, string> { ["X-Next-Page"] = "2" });

        var connector = Connector(transport, RepositoryHost.GitLab, o => o.IncludeGlobs = ["*.md"]);
        var page = await connector.ListAsync(new ConnectorListRequest());

        Assert.Empty(page.Items);
        Assert.Equal("2", page.NextCursor);
    }

    /// <summary>The other host addresses a project by an encoded path, not by owner and name.</summary>
    [Fact]
    public async Task EncodesTheProjectPathForTheOtherHost()
    {
        var transport = new FakeConnectorTransport().Route("/repository/tree", "[]");

        await Connector(transport, RepositoryHost.GitLab).ListAsync(new ConnectorListRequest());

        Assert.Contains("/projects/acme%2Ftools/", transport.Requests[0].Url, StringComparison.Ordinal);
    }

    /// <summary>A citation points at a page a human can open, and each host spells that differently.</summary>
    [Theory]
    [InlineData(RepositoryHost.GitHub, "https://github.com/acme/tools/blob/main/docs/a.md")]
    [InlineData(RepositoryHost.GitLab, "https://gitlab.com/acme/tools/-/blob/main/docs/a.md")]
    public async Task BuildsAHumanUrlForEachHost(RepositoryHost host, string expected)
    {
        var transport = host == RepositoryHost.GitHub
            ? GitHubTransport(("docs/a.md", "sha1", 10))
            : new FakeConnectorTransport().Route("/repository/tree", "[{\"id\":\"i1\",\"type\":\"blob\",\"path\":\"docs/a.md\"}]");

        var page = await Connector(transport, host).ListAsync(new ConnectorListRequest());

        Assert.Equal(expected, page.Items[0].SourceUrl);
    }

    /// <summary>The content hash is carried as the item version, which is what makes sync exact.</summary>
    [Fact]
    public async Task CarriesTheContentHashAsTheVersion()
    {
        var transport = GitHubTransport(("docs/a.md", "abc123", 10));

        var page = await Connector(transport, RepositoryHost.GitHub).ListAsync(new ConnectorListRequest());

        Assert.Equal("abc123", page.Items[0].Version);
    }

    /// <summary>A connector with no project to read refuses to be constructed.</summary>
    [Fact]
    public void RefusesAnEmptyProjectPath() =>
        Assert.Throws<ArgumentException>(() =>
            new RepositoryConnector(new FakeConnectorTransport(), new RepositoryConnectorOptions()));

    private static RepositoryConnector Connector(
        FakeConnectorTransport transport,
        RepositoryHost host,
        Action<RepositoryConnectorOptions>? configure = null)
    {
        var options = new RepositoryConnectorOptions
        {
            Host = host,
            ProjectPath = "acme/tools",
            Branch = "main",
        };

        // Naming a branch by default keeps every other test to one fake route; the default-branch
        // test clears it to exercise resolution.
        configure?.Invoke(options);

        return new RepositoryConnector(transport, options);
    }

    private static FakeConnectorTransport GitHubTransport(params (string Path, string Sha, int Size)[] files) =>
        new FakeConnectorTransport().Route("/trees/", Tree(files));

    private static string Tree(params (string Path, string Sha, int Size)[] files)
    {
        var entries = files.Select(f =>
            $"{{\"path\":\"{f.Path}\",\"type\":\"blob\",\"sha\":\"{f.Sha}\",\"size\":{f.Size}}}");

        return $"{{\"tree\":[{string.Join(",", entries)}],\"truncated\":false}}";
    }
}
