using System.Text;
using TechieRag.Connectors;
using TechieRag.Connectors.Confluence;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-020 / BRD-64: a space walk and a page-tree walk, their paging, and the conversion of
/// storage-format XHTML into prose.
/// </summary>
public sealed class ConfluenceConnectorTests
{
    private const string BaseUrl = "https://acme.example.test/wiki";

    /// <summary>A space listing yields its pages with titles and versions.</summary>
    [Fact]
    public async Task ListsPagesInASpace()
    {
        var transport = new FakeConnectorTransport()
            .Route("spaceKey=ENG", Listing(next: null, ("1", "Runbook", 3), ("2", "Onboarding", 1)));

        var page = await SpaceConnector(transport).ListAsync(new ConnectorListRequest());

        Assert.Equal(["Runbook", "Onboarding"], page.Items.Select(i => i.Name));
        Assert.Equal("3", page.Items[0].Version);
    }

    /// <summary>
    /// Paging follows the link the site states, resolved against the base the same response
    /// declares — Cloud answers on a prefix a caller may or may not have configured.
    /// </summary>
    [Fact]
    public async Task FollowsTheSitesOwnNextLink()
    {
        var transport = new FakeConnectorTransport()
            .Route("start=25", Listing(next: null, ("3", "Second page", 1)))
            .Route("spaceKey=ENG", Listing("/rest/api/content?spaceKey=ENG&start=25", ("1", "First page", 1)));

        var connector = SpaceConnector(transport);
        var first = await connector.ListAsync(new ConnectorListRequest());

        Assert.Equal($"{BaseUrl}/rest/api/content?spaceKey=ENG&start=25", first.NextCursor);

        var second = await connector.ListAsync(new ConnectorListRequest(first.NextCursor));
        Assert.Equal(["Second page"], second.Items.Select(i => i.Name));
        Assert.Null(second.NextCursor);
    }

    /// <summary>
    /// A page tree starts at the page the user named. Beginning at its children would silently drop
    /// the page they actually asked for.
    /// </summary>
    [Fact]
    public async Task IncludesTheRootOfAPageTree()
    {
        var transport = new FakeConnectorTransport()
            .Route("/content/100/child/page", Listing(next: null))
            .Route("/content/100", Single("100", "Root", 2));

        var page = await TreeConnector(transport).ListAsync(new ConnectorListRequest());

        Assert.Equal(["Root"], page.Items.Select(i => i.Name));
    }

    /// <summary>The walk descends to children, one level at a time, breadth first.</summary>
    [Fact]
    public async Task DescendsIntoChildPages()
    {
        var transport = new FakeConnectorTransport()
            .Route("/content/200/child/page", Listing(next: null))
            .Route("/content/100/child/page", Listing(next: null, ("200", "Child", 1)))
            .Route("/content/100", Single("100", "Root", 2));

        var connector = TreeConnector(transport);

        var root = await connector.ListAsync(new ConnectorListRequest());
        Assert.NotNull(root.NextCursor);

        var children = await connector.ListAsync(new ConnectorListRequest(root.NextCursor));
        Assert.Equal(["Child"], children.Items.Select(i => i.Name));

        var grandchildren = await connector.ListAsync(new ConnectorListRequest(children.NextCursor));
        Assert.Empty(grandchildren.Items);
    }

    /// <summary>With child pages switched off, the walk is the named page and nothing else.</summary>
    [Fact]
    public async Task StopsAtTheRootWhenChildPagesAreExcluded()
    {
        var transport = new FakeConnectorTransport().Route("/content/100", Single("100", "Root", 2));

        var connector = new ConfluenceConnector(transport, new ConfluenceConnectorOptions
        {
            BaseUrl = BaseUrl,
            RootPageId = "100",
            IncludeChildPages = false,
        });

        var page = await connector.ListAsync(new ConnectorListRequest());

        Assert.Single(page.Items);
        Assert.Null(page.NextCursor);
    }

    /// <summary>Storage-format XHTML becomes prose, not a page of angle brackets.</summary>
    [Fact]
    public async Task ConvertsStorageFormatToText()
    {
        var storage = "<p>The renewal was <strong>approved</strong>.</p><ac:structured-macro ac:name=\"info\"/>";
        var transport = new FakeConnectorTransport()
            .Route("expand=body.storage", Body(storage))
            .Route("spaceKey=ENG", Listing(next: null, ("1", "Renewal", 1)));

        var connector = SpaceConnector(transport);
        var page = await connector.ListAsync(new ConnectorListRequest());
        var document = await connector.FetchAsync(page.Items[0]);

        Assert.Contains("approved", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>", document.Text, StringComparison.Ordinal);
    }

    /// <summary>Cloud pairs an email with a token over basic auth, and the token is not in the URL.</summary>
    [Fact]
    public async Task UsesBasicAuthWhenAnEmailIsSupplied()
    {
        var transport = new FakeConnectorTransport().Route("spaceKey=ENG", Listing(next: null));

        await SpaceConnector(transport, o =>
        {
            o.UserEmail = "ada@example.test";
            o.ApiToken = "cloud-token";
        }).ListAsync(new ConnectorListRequest());

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("ada@example.test:cloud-token"));
        Assert.Equal($"Basic {expected}", transport.Requests[0].Headers!["Authorization"]);
        Assert.DoesNotContain("cloud-token", transport.Requests[0].Url, StringComparison.Ordinal);
    }

    /// <summary>A token with no email is a personal access token, sent as a bearer token.</summary>
    [Fact]
    public async Task UsesBearerAuthWhenOnlyATokenIsSupplied()
    {
        var transport = new FakeConnectorTransport().Route("spaceKey=ENG", Listing(next: null));

        await SpaceConnector(transport, o => o.ApiToken = "pat-value").ListAsync(new ConnectorListRequest());

        Assert.Equal("Bearer pat-value", transport.Requests[0].Headers!["Authorization"]);
    }

    /// <summary>A rejected token ends the run, and the message does not repeat it back.</summary>
    [Fact]
    public async Task RejectedTokenEndsTheRun()
    {
        var transport = new FakeConnectorTransport().Route("spaceKey=ENG", "{}", 401);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => SpaceConnector(transport, o => o.ApiToken = "pat-value").ListAsync(new ConnectorListRequest()));

        Assert.Equal(401, error.StatusCode);
        Assert.DoesNotContain("pat-value", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A space that does not exist says so rather than reporting an empty space.</summary>
    [Fact]
    public async Task MissingSpaceIsNamedHonestly()
    {
        var transport = new FakeConnectorTransport().Route("spaceKey=ENG", "{}", 404);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => SpaceConnector(transport).ListAsync(new ConnectorListRequest()));

        Assert.Contains("does not exist", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A page deleted between listing and fetching costs that page only, so it does not raise the
    /// run-level failure type.
    /// </summary>
    [Fact]
    public async Task MissingPageCostsOnlyThatPage()
    {
        var transport = new FakeConnectorTransport().Route("spaceKey=ENG", Listing(next: null, ("1", "Gone", 1)));

        var connector = SpaceConnector(transport);
        var page = await connector.ListAsync(new ConnectorListRequest());

        var error = await Record.ExceptionAsync(() => connector.FetchAsync(page.Items[0]));

        Assert.NotNull(error);
        Assert.IsNotType<ConnectorException>(error);
    }

    /// <summary>A citation points at the page's own web link, resolved against the site's base.</summary>
    [Fact]
    public async Task BuildsAHumanUrlFromTheSitesLink()
    {
        var transport = new FakeConnectorTransport().Route("spaceKey=ENG", Listing(next: null, ("1", "Runbook", 1)));

        var page = await SpaceConnector(transport).ListAsync(new ConnectorListRequest());

        Assert.Equal($"{BaseUrl}/spaces/ENG/pages/1", page.Items[0].SourceUrl);
    }

    /// <summary>A connector must be told whether it is walking a space or a tree, and cannot be both.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("ENG", "100")]
    public void RefusesAnAmbiguousScope(string? spaceKey, string? rootPageId) =>
        Assert.Throws<ArgumentException>(() => new ConfluenceConnector(
            new FakeConnectorTransport(),
            new ConfluenceConnectorOptions { BaseUrl = BaseUrl, SpaceKey = spaceKey, RootPageId = rootPageId }));

    /// <summary>A base URL that is not absolute is refused before any request is made.</summary>
    [Fact]
    public async Task RefusesARelativeBaseUrl()
    {
        var connector = new ConfluenceConnector(
            new FakeConnectorTransport(),
            new ConfluenceConnectorOptions { BaseUrl = "/wiki", SpaceKey = "ENG" });

        await Assert.ThrowsAsync<ConnectorException>(() => connector.ListAsync(new ConnectorListRequest()));
    }

    /// <summary>
    /// A paging link pointing off the configured site is refused, and the credential is never sent
    /// to it.
    /// </summary>
    /// <remarks>
    /// <para>The API states its own <c>next</c> link inside the response body, so the response is an
    /// input that names the next request — and every request carries the site's API token in an
    /// <c>Authorization</c> header. Following a foreign link would hand that token to whoever wrote
    /// the response.</para>
    /// <para>The assertion that matters is the request log: refusing to return the PAGE after the
    /// request has gone out would not un-send the credential.</para>
    /// </remarks>
    [Theory]
    [InlineData("https://attacker.example.test/collect")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://acme.example.test/wiki/rest/api/content")]
    public async Task RefusesAPagingLinkThatLeavesTheConfiguredSite(string hostileNext)
    {
        var transport = new FakeConnectorTransport()
            .Route("spaceKey=ENG", Listing(hostileNext, ("1", "First page", 1)));

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => SpaceConnector(transport).ListAsync(new ConnectorListRequest()));

        Assert.Contains("not on the configured site", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            transport.Requests,
            request => request.Url.StartsWith(hostileNext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A cursor handed back in is re-checked, not trusted because it looks like ours.</summary>
    /// <remarks>
    /// The runner persists cursors and hands them to the next call, so the value arriving at
    /// <c>ListAsync</c> has been outside this object. Checking only where a cursor is produced would
    /// leave the actual request site unguarded.
    /// </remarks>
    [Fact]
    public async Task RefusesAForeignCursorSuppliedByTheCaller()
    {
        var transport = new FakeConnectorTransport();

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => SpaceConnector(transport).ListAsync(
                new ConnectorListRequest("https://attacker.example.test/collect")));

        Assert.Contains("not on the configured site", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(transport.Requests);
    }

    /// <summary>The refusal names the offending URL but never the credential it protected.</summary>
    [Fact]
    public async Task RefusalNamesTheUrlAndNeverTheToken()
    {
        const string Token = "super-secret-atlassian-token";
        var transport = new FakeConnectorTransport()
            .Route("spaceKey=ENG", Listing("https://attacker.example.test/collect", ("1", "Page", 1)));

        var connector = SpaceConnector(transport, options =>
        {
            options.UserEmail = "someone@acme.test";
            options.ApiToken = Token;
        });

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => connector.ListAsync(new ConnectorListRequest()));

        Assert.Contains("attacker.example.test", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
    }

    /// <summary>A link base stated off-site is ignored rather than used to build citation URLs.</summary>
    /// <remarks>
    /// No credential is at stake in a citation URL — the connector never requests one — but it is
    /// shown to a user as "where this answer came from", so a response that could set it to any
    /// address would turn every citation into a phishing link wearing the site's name.
    /// </remarks>
    [Fact]
    public async Task IgnoresALinkBaseThatLeavesTheConfiguredSite()
    {
        const string Body =
            "{\"results\":[{\"id\":\"1\",\"title\":\"Runbook\",\"type\":\"page\","
            + "\"version\":{\"number\":1},\"_links\":{\"webui\":\"/spaces/ENG/pages/1\"}}],"
            + "\"_links\":{\"base\":\"https://attacker.example.test\"}}";

        var transport = new FakeConnectorTransport().Route("spaceKey=ENG", Body);

        var page = await SpaceConnector(transport).ListAsync(new ConnectorListRequest());

        Assert.StartsWith(BaseUrl, page.Items[0].SourceUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker.example.test", page.Items[0].SourceUrl, StringComparison.Ordinal);
    }

    /// <summary>An absolute page link off-site is replaced by a constructed one.</summary>
    [Fact]
    public async Task IgnoresAPageLinkThatLeavesTheConfiguredSite()
    {
        const string Body =
            "{\"results\":[{\"id\":\"7\",\"title\":\"Runbook\",\"type\":\"page\","
            + "\"version\":{\"number\":1},"
            + "\"_links\":{\"webui\":\"https://attacker.example.test/phish\"}}],"
            + "\"_links\":{\"base\":\"" + BaseUrl + "\"}}";

        var transport = new FakeConnectorTransport().Route("spaceKey=ENG", Body);

        var page = await SpaceConnector(transport).ListAsync(new ConnectorListRequest());

        Assert.DoesNotContain("attacker.example.test", page.Items[0].SourceUrl, StringComparison.Ordinal);
        Assert.Contains("7", page.Items[0].SourceUrl, StringComparison.Ordinal);
    }

    /// <summary>An ordinary same-site paging link is still followed.</summary>
    /// <remarks>The guard must not break the paging it sits in front of.</remarks>
    [Fact]
    public async Task StillFollowsAnAbsoluteSameSitePagingLink()
    {
        var absoluteNext = $"{BaseUrl}/rest/api/content?spaceKey=ENG&start=25";
        var transport = new FakeConnectorTransport()
            .Route("start=25", Listing(next: null, ("3", "Second page", 1)))
            .Route("spaceKey=ENG", Listing(absoluteNext, ("1", "First page", 1)));

        var connector = SpaceConnector(transport);
        var first = await connector.ListAsync(new ConnectorListRequest());

        Assert.Equal(absoluteNext, first.NextCursor);

        var second = await connector.ListAsync(new ConnectorListRequest(first.NextCursor));
        Assert.Equal(["Second page"], second.Items.Select(i => i.Name));
    }

    private static ConfluenceConnector SpaceConnector(
        FakeConnectorTransport transport,
        Action<ConfluenceConnectorOptions>? configure = null)
    {
        var options = new ConfluenceConnectorOptions { BaseUrl = BaseUrl, SpaceKey = "ENG" };
        configure?.Invoke(options);
        return new ConfluenceConnector(transport, options);
    }

    private static ConfluenceConnector TreeConnector(FakeConnectorTransport transport) =>
        new(transport, new ConfluenceConnectorOptions { BaseUrl = BaseUrl, RootPageId = "100" });

    private static string Listing(string? next, params (string Id, string Title, int Version)[] pages)
    {
        var results = pages.Select(p => PageJson(p.Id, p.Title, p.Version));
        var links = next is null
            ? $"\"base\":\"{BaseUrl}\""
            : $"\"base\":\"{BaseUrl}\",\"next\":\"{next}\"";

        return $"{{\"results\":[{string.Join(",", results)}],\"_links\":{{{links}}}}}";
    }

    private static string Single(string id, string title, int version) =>
        $"{{\"id\":\"{id}\",\"title\":\"{title}\",\"type\":\"page\","
        + $"\"version\":{{\"number\":{version},\"when\":\"2026-01-02T03:04:05.000Z\"}},"
        + $"\"space\":{{\"key\":\"ENG\"}},"
        + $"\"_links\":{{\"base\":\"{BaseUrl}\",\"webui\":\"/spaces/ENG/pages/{id}\"}}}}";

    private static string PageJson(string id, string title, int version) =>
        $"{{\"id\":\"{id}\",\"title\":\"{title}\",\"type\":\"page\","
        + $"\"version\":{{\"number\":{version},\"when\":\"2026-01-02T03:04:05.000Z\"}},"
        + $"\"space\":{{\"key\":\"ENG\"}},\"_links\":{{\"webui\":\"/spaces/ENG/pages/{id}\"}}}}";

    private static string Body(string storage) =>
        $"{{\"id\":\"1\",\"title\":\"Renewal\",\"body\":{{\"storage\":{{\"value\":\"{storage.Replace("\"", "\\\"")}\"}}}}}}";
}
