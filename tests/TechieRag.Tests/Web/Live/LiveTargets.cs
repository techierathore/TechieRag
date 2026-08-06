namespace TechieRag.Tests.Web.Live;

/// <summary>
/// The real hosts the live-network suite reads from (REQ-RAG-016/017/018).
/// </summary>
/// <remarks>
/// <para>Chosen for stability and for being fair game. <c>example.com</c> and <c>iana.org</c> exist
/// to be referenced; <c>quotes.toscrape.com</c> is a sandbox published specifically so scrapers have
/// something to practise on; Wikipedia is the most stable large HTML document on the web. Nothing
/// here is a commercial site being crawled without its consent, and every crawl in the suite is
/// bounded to a handful of pages with the politeness delay left on.</para>
/// <para>Each constant is named for the PROPERTY of the target the test depends on, not for the
/// host, so a test that has to move can be re-pointed without its assertions becoming a lie.</para>
/// </remarks>
public static class LiveTargets
{
    /// <summary>A tiny, extremely stable page carrying exactly one off-host link.</summary>
    /// <remarks>Its whole value is that the link graph is knowable: 0 same-host links, 1 off-host.</remarks>
    public const string TinyPageWithOneOffHostLink = "https://example.com/";

    /// <summary>A large real-world page with navigation, headers and a footer to be stripped.</summary>
    public const string ArticleWithSiteChrome = "https://en.wikipedia.org/wiki/Retrieval-augmented_generation";

    /// <summary>Text that appears only inside <see cref="ArticleWithSiteChrome"/>'s footer.</summary>
    public const string ChromeOnlyFooterMarker = "rendered with Parsoid";

    /// <summary>A scraping sandbox with a dense same-host link graph and two off-host links.</summary>
    public const string CrawlableSandbox = "https://quotes.toscrape.com/";

    /// <summary>Host component of <see cref="CrawlableSandbox"/>.</summary>
    public const string CrawlableSandboxHost = "quotes.toscrape.com";

    /// <summary>Text that appears only inside <see cref="CrawlableSandbox"/>'s footer.</summary>
    /// <remarks>
    /// Verified against the live markup on 2026-07-27: the sandbox's <c>&lt;footer&gt;</c> holds
    /// "Quotes by: GoodReads.com Made with ❤ by Zyte" and nothing else. Its "Top Ten tags" sidebar
    /// is an ordinary <c>&lt;div&gt;</c>, NOT chrome, and is correctly kept — asserting its absence
    /// would be asserting a bug.
    /// </remarks>
    public const string SandboxFooterMarker = "Made with";

    /// <summary>A URL on a live host that answers 404.</summary>
    public const string MissingPage = "https://quotes.toscrape.com/definitely-not-a-page";

    /// <summary>A live URL that serves JSON, which is not a readable web page.</summary>
    public const string NonHtmlResource = "https://registry.npmjs.org/left-pad";

    /// <summary>A public open redirector, used to prove the SSRF guard survives a redirect.</summary>
    /// <remarks>
    /// An open redirector on a third-party host is exactly the shape of the real attack: the operator
    /// pastes a URL on a host they trust and the response points somewhere they did not choose. Using
    /// a real one is the only way to know the guard holds against a real 302 rather than a mocked one.
    /// </remarks>
    public const string OpenRedirectorFormat = "https://postman-echo.com/redirect-to?url={0}";

    /// <summary>The same open redirector reached over plain http.</summary>
    /// <remarks>
    /// Both schemes are needed because they exercise DIFFERENT code. .NET refuses to auto-follow an
    /// https→http redirect, so the https form never leaves the first hop and is caught by inspecting
    /// the unfollowed <c>Location</c>. The http form IS followed, so it is the only one that reaches
    /// the connect-time guard — the case where the internal request would actually be issued.
    /// </remarks>
    public const string PlainHttpOpenRedirectorFormat = "http://postman-echo.com/redirect-to?url={0}";

    /// <summary>A public hostname that resolves to 127.0.0.1.</summary>
    /// <remarks>
    /// The DNS-name bypass: nothing in the URL text looks private, so a guard that only inspects the
    /// literal host string lets it through and the request lands on loopback.
    /// </remarks>
    public const string HostnameResolvingToLoopback = "127.0.0.1.nip.io";

    /// <summary>A video with a large, stable set of caption tracks, including a manual English one.</summary>
    public const string VideoWithCaptions = "https://www.youtube.com/watch?v=aircAruvnKk";

    /// <summary>A video that publishes no caption tracks at all.</summary>
    public const string VideoWithoutCaptions = "https://www.youtube.com/watch?v=1La4QzGeaaQ";

    /// <summary>Builds an open-redirect URL that lands on the given target.</summary>
    /// <param name="target">The absolute URL the redirector should send the client to.</param>
    /// <returns>A URL on the redirector's host that 302s to <paramref name="target"/>.</returns>
    public static string RedirectTo(string target) =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            OpenRedirectorFormat,
            Uri.EscapeDataString(target));

    /// <summary>Builds an open-redirect URL over plain http, so the redirect is actually followed.</summary>
    /// <param name="target">The absolute URL the redirector should send the client to.</param>
    /// <returns>An http URL on the redirector's host that 302s to <paramref name="target"/>.</returns>
    public static string RedirectOverPlainHttpTo(string target) =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            PlainHttpOpenRedirectorFormat,
            Uri.EscapeDataString(target));
}
