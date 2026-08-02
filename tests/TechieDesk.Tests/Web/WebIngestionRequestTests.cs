using TechieDesk.Services.Web;
using TechieDesk.Tests.Support;
using Xunit;

namespace TechieDesk.Tests.Web;

/// <summary>
/// REQ-RAG-016/017/018: everything the "Add from web" surface can refuse before it opens a socket.
/// </summary>
/// <remarks>
/// <see cref="WebIngestionRequest.Validate"/> is the single definition the form and the service both
/// use, so these are simultaneously the inline form errors and the service's guard. The one that
/// matters most is the private-network case: the SSRF guard is only a guard if the DEFAULT refuses.
/// </remarks>
public sealed class WebIngestionRequestTests : IDisposable
{
    /// <summary>
    /// REQ-UI-055: <c>Validate</c> returns sentences resolved from the REAL English resources, so
    /// the substring assertions below still describe what the card actually shows.
    /// </summary>
    private readonly ResourceHarness resources = new("en");

    /// <summary>Restores the ambient UI culture the harness moved.</summary>
    public void Dispose() => resources.Dispose();

    /// <summary>An empty address is refused, not treated as an empty crawl.</summary>
    [Fact]
    public void UrlIsRequired()
    {
        var request = Build(WebIngestionSource.Page, string.Empty);

        Assert.Equal("Enter a URL first.", request.Validate(resources.Localize));
    }

    /// <summary>A request with no workspace has nowhere to put its result and is refused.</summary>
    [Fact]
    public void WorkspaceIsRequired()
    {
        var request = new WebIngestionRequest
        {
            Source = WebIngestionSource.Page,
            WorkspaceId = string.Empty,
            Url = "https://example.com",
        };

        Assert.Contains("workspace", request.Validate(resources.Localize), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A scheme-less or relative address is refused with the fix in the message.</summary>
    [Theory]
    [InlineData("example.com")]
    [InlineData("/docs/index.html")]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("file:///etc/passwd")]
    public void OnlyAbsoluteHttpAddressesAreAccepted(string candidate)
    {
        var error = Build(WebIngestionSource.Page, candidate).Validate(resources.Localize);

        Assert.NotNull(error);
        Assert.Contains("http", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A normal public page passes.</summary>
    [Fact]
    public void PublicHttpsPageIsAccepted()
    {
        Assert.Null(Build(WebIngestionSource.Page, "https://example.com/article").Validate(resources.Localize));
    }

    /// <summary>
    /// THE security acceptance: a private-network address is refused while the guard is on, and the
    /// message names the switch that would allow it rather than leaving the operator guessing.
    /// </summary>
    [Theory]
    [InlineData("http://localhost:8080/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.1.2.3/")]
    [InlineData("http://192.168.0.10/")]
    [InlineData("http://172.16.5.4/")]
    [InlineData("http://router.local/")]
    public void PrivateNetworkTargetsAreRefusedByDefault(string candidate)
    {
        var error = Build(WebIngestionSource.Site, candidate).Validate(resources.Localize);

        Assert.NotNull(error);
        Assert.Contains("private-network", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Allow intranet", error, StringComparison.Ordinal);
    }

    /// <summary>Crawling an intranet is legitimate — once it has been asked for explicitly.</summary>
    [Fact]
    public void PrivateNetworkTargetIsAcceptedOnceExplicitlyAllowed()
    {
        var request = new WebIngestionRequest
        {
            Source = WebIngestionSource.Site,
            WorkspaceId = "ws",
            Url = "http://intranet.local/handbook",
            AllowPrivateNetworkTargets = true,
        };

        Assert.Null(request.Validate(resources.Localize));
    }

    /// <summary>
    /// The opt-in is inverted so that forgetting it leaves the guard ON. A default-constructed
    /// request must produce crawl options that block private targets.
    /// </summary>
    [Fact]
    public void CrawlOptionsBlockPrivateTargetsUnlessAllowed()
    {
        Assert.True(Build(WebIngestionSource.Site, "https://example.com").ToCrawlOptions()
            .BlockPrivateNetworkTargets);

        var opted = new WebIngestionRequest
        {
            Source = WebIngestionSource.Site,
            WorkspaceId = "ws",
            Url = "https://example.com",
            AllowPrivateNetworkTargets = true,
        };

        Assert.False(opted.ToCrawlOptions().BlockPrivateNetworkTargets);
    }

    /// <summary>Depth and page bounds are carried into the library options verbatim.</summary>
    [Fact]
    public void CrawlOptionsCarryTheBoundsTheFormCollected()
    {
        var request = new WebIngestionRequest
        {
            Source = WebIngestionSource.Site,
            WorkspaceId = "ws",
            Url = "https://example.com",
            MaxDepth = 3,
            MaxPages = 40,
            SameHostOnly = false,
        };

        var options = request.ToCrawlOptions();

        Assert.Equal(3, options.MaxDepth);
        Assert.Equal(40, options.MaxPages);
        Assert.False(options.SameHostOnly);
    }

    /// <summary>Depth outside the surface's ceiling is refused; link counts grow exponentially.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(99)]
    public void DepthOutsideTheCeilingIsRefused(int depth)
    {
        var request = new WebIngestionRequest
        {
            Source = WebIngestionSource.Site,
            WorkspaceId = "ws",
            Url = "https://example.com",
            MaxDepth = depth,
        };

        Assert.Contains("depth", request.Validate(resources.Localize), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A page budget of zero or beyond the ceiling is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(201)]
    public void PageBudgetOutsideTheCeilingIsRefused(int pages)
    {
        var request = new WebIngestionRequest
        {
            Source = WebIngestionSource.Site,
            WorkspaceId = "ws",
            Url = "https://example.com",
            MaxPages = pages,
        };

        Assert.Contains("page limit", request.Validate(resources.Localize), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Depth and page bounds are a crawl concern only; a single page ignores them.</summary>
    [Fact]
    public void SinglePageIgnoresCrawlBounds()
    {
        var request = new WebIngestionRequest
        {
            Source = WebIngestionSource.Page,
            WorkspaceId = "ws",
            Url = "https://example.com",
            MaxDepth = 99,
            MaxPages = 0,
        };

        Assert.Null(request.Validate(resources.Localize));
    }

    /// <summary>Every YouTube URL shape the library recognises is accepted, plus a bare id.</summary>
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ")]
    public void YouTubeAddressesAreAccepted(string candidate)
    {
        Assert.Null(Build(WebIngestionSource.Video, candidate).Validate(resources.Localize));
    }

    /// <summary>A page URL pasted into the video field fails there rather than later, over the wire.</summary>
    [Fact]
    public void NonYouTubeAddressIsRefusedForAVideo()
    {
        var error = Build(WebIngestionSource.Video, "https://vimeo.com/12345").Validate(resources.Localize);

        Assert.NotNull(error);
        Assert.Contains("YouTube", error, StringComparison.Ordinal);
    }

    /// <summary>A crawl's progress arithmetic uses the page budget; anything else expects one fetch.</summary>
    [Fact]
    public void ExpectedFetchCountFollowsTheSource()
    {
        var crawl = new WebIngestionRequest
        {
            Source = WebIngestionSource.Site,
            WorkspaceId = "ws",
            Url = "https://example.com",
            MaxPages = 12,
        };

        Assert.Equal(12, crawl.ExpectedFetchCount());
        Assert.Equal(1, Build(WebIngestionSource.Page, "https://example.com").ExpectedFetchCount());
        Assert.Equal(1, Build(WebIngestionSource.Video, "dQw4w9WgXcQ").ExpectedFetchCount());
    }

    /// <summary>Surrounding whitespace from a paste is not a validation failure.</summary>
    [Fact]
    public void PastedWhitespaceIsTrimmed()
    {
        var request = Build(WebIngestionSource.Page, "  https://example.com/a  ");

        Assert.Null(request.Validate(resources.Localize));
        Assert.Equal("https://example.com/a", request.TrimmedUrl);
    }

    private static WebIngestionRequest Build(WebIngestionSource source, string url) => new()
    {
        Source = source,
        WorkspaceId = "workspace-1",
        Url = url,
    };
}
