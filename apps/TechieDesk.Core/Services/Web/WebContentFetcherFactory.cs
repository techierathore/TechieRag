using TechieRag.Web;

namespace TechieDesk.Services.Web;

/// <summary>
/// Creates a page fetcher for one ingestion run (REQ-RAG-016/017).
/// </summary>
/// <remarks>
/// A factory rather than a registered <see cref="IWebContentFetcher"/> singleton because the SSRF
/// guard is a per-run decision: <see cref="HttpWebContentFetcher"/> takes
/// <c>blockPrivateTargets</c> at construction, so one shared instance could only ever hold one
/// answer, and the intranet opt-in would either be permanently on or permanently unavailable.
/// </remarks>
public interface IWebContentFetcherFactory
{
    /// <summary>Creates a fetcher bound to one run's private-network policy.</summary>
    /// <param name="blockPrivateNetworkTargets">
    /// Refuse loopback, private and link-local hosts. See <see cref="WebCrawlOptions.BlockPrivateNetworkTargets"/>.
    /// </param>
    /// <returns>A fetcher the caller uses for the duration of a single run.</returns>
    IWebContentFetcher Create(bool blockPrivateNetworkTargets);
}

/// <summary>
/// Builds <see cref="HttpWebContentFetcher"/> instances over a pooled, pre-configured client.
/// </summary>
public sealed class HttpWebContentFetcherFactory : IWebContentFetcherFactory
{
    /// <summary>Name of the configured <see cref="HttpClient"/> registration crawls use.</summary>
    /// <remarks>Its handler refuses to connect to a private address on any hop.</remarks>
    public const string HttpClientName = "TechieDeskWebIngestion";

    /// <summary>Name of the client used when the operator opted into reading their own network.</summary>
    /// <remarks>
    /// A separate registration because the guard lives in the message handler, which is fixed when
    /// the client is registered. Selecting between two named clients is what keeps the opt-in a
    /// per-run decision without making the guard something a URL check could be talked out of.
    /// </remarks>
    public const string PrivateAllowedHttpClientName = "TechieDeskWebIngestion.PrivateAllowed";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILoggerFactory loggerFactory;

    /// <summary>Initializes a new instance of the <see cref="HttpWebContentFetcherFactory"/> class.</summary>
    /// <param name="httpClientFactory">Supplies the pooled, pre-configured client.</param>
    /// <param name="loggerFactory">Creates the fetcher's logger.</param>
    public HttpWebContentFetcherFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc />
    public IWebContentFetcher Create(bool blockPrivateNetworkTargets) => new HttpWebContentFetcher(
        httpClientFactory.CreateClient(
            blockPrivateNetworkTargets ? HttpClientName : PrivateAllowedHttpClientName),
        loggerFactory.CreateLogger<HttpWebContentFetcher>(),
        blockPrivateNetworkTargets);
}
