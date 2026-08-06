using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Web;

namespace TechieRag.Connectors.Http;

/// <summary>
/// The real HTTP transport for REST-backed connectors (REQ-RAG-032 / BRD-113).
/// </summary>
/// <remarks>
/// <para>Raw <see cref="HttpClient"/> and nothing else, per the library's dependency-light rule for
/// provider code.</para>
/// <para><b>It never logs a header.</b> Authorization headers pass through this class on every
/// request; the logger sees the URL and the status only. That is a deliberate limit on what a debug
/// log can leak, not an oversight in the diagnostics.</para>
/// <para><b>Server-side request forgery is guarded at connect time, by the same handler the crawler
/// uses.</b> A connector's base URL is attacker-influenced input in exactly the way a crawl target
/// is — it arrives from whoever configured the workspace, and a Confluence site's own response can
/// name the next URL to walk. The textual host check below is a cheap fast path and is explicitly
/// NOT the enforcement point: a public DNS name that resolves to loopback reads as an ordinary
/// domain and walks straight through it. Enforcement is
/// <see cref="HttpWebContentFetcher.CreateGuardedHandler"/>, which decides on the RESOLVED address
/// and connects only to the address it checked. <see cref="CreateDefaultClient"/> builds that
/// handler; a caller supplying its own <see cref="HttpClient"/> is responsible for doing the
/// same.</para>
/// </remarks>
public sealed class HttpConnectorTransport : IConnectorTransport
{
    /// <summary>Largest response body accepted, in bytes.</summary>
    /// <remarks>
    /// A cap is required, not garnish: a connector following a paging cursor into an unexpectedly
    /// large export would otherwise stream it into memory and take the process with it.
    /// </remarks>
    public const int MaxResponseBytes = 16 * 1024 * 1024;

    private readonly HttpClient httpClient;
    private readonly ILogger<HttpConnectorTransport> logger;
    private readonly bool blockPrivateTargets;

    /// <summary>Initializes a new instance of the <see cref="HttpConnectorTransport"/> class.</summary>
    /// <param name="httpClient">Client used for requests.</param>
    /// <param name="logger">Diagnostics. Never receives request headers.</param>
    /// <param name="blockPrivateTargets">
    /// Refuse loopback/private/link-local hosts. Defaults to <b>true</b>, the same as the crawler.
    /// <para>An earlier version of this class defaulted to false on the reasoning that a self-hosted
    /// server on the company LAN is the normal case and that an operator-typed URL is not
    /// attacker-influenced. Both halves of that are wrong. A base URL arrives from whoever can
    /// configure a workspace, not from the person reading the logs; and this transport attaches an
    /// <c>Authorization</c> header to every request, so a URL aimed at an internal endpoint does not
    /// merely read it — it hands the source's credential to it. Reaching a private network is
    /// therefore an explicit opt-in, made by the operator who actually meant it.</para>
    /// </param>
    public HttpConnectorTransport(
        HttpClient httpClient,
        ILogger<HttpConnectorTransport>? logger = null,
        bool blockPrivateTargets = true)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.logger = logger ?? NullLogger<HttpConnectorTransport>.Instance;
        this.blockPrivateTargets = blockPrivateTargets;
    }

    /// <inheritdoc />
    public async Task<ConnectorHttpResponse> GetAsync(
        ConnectorHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.Url);

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ConnectorException("http", $"'{request.Url}' is not an absolute http or https URL.");
        }

        // Cheap fast path only. It sees what the URL SAYS, so a public name that resolves to
        // loopback walks through it untouched; the check that actually holds runs at connect time in
        // the guarded handler. Kept because refusing an obviously private literal without opening a
        // socket gives the operator a better message than a connect failure does.
        if (blockPrivateTargets && WebCrawlOptions.IsPrivateNetworkHost(uri.Host))
        {
            throw new ConnectorException("http", $"'{uri.Host}' is a private-network address and was refused.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        if (request.Headers is not null)
        {
            foreach (var header in request.Headers)
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller stopping the run and the host failing to answer both surface as this one
            // exception type. Separating them on whether the token was actually signalled is what
            // lets a cancelled job report "cancelled" instead of libelling a host that was fine —
            // and it keeps ConnectorRunner's cancellation path reachable, since that path is
            // selected by the exception TYPE.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A client built by CreateDefaultClient refuses the socket itself, on the address it was
            // about to connect to. That refusal is the accurate message; the transport's generic
            // "connection failed" wrapper around it tells the operator nothing about why.
            if (FindRefusal(ex) is { } refused)
            {
                throw new ConnectorException("http", refused.Message, refused);
            }

            throw new ConnectorException("http", $"{uri.Host} could not be reached: {ex.Message}", ex);
        }

        using (response)
        {
            // Defence in depth behind the connect-time guard: if a caller supplied its own unguarded
            // HttpClient, a redirect chain into a private address would otherwise return its body.
            var finalUri = response.RequestMessage?.RequestUri ?? uri;
            if (blockPrivateTargets && WebCrawlOptions.IsPrivateNetworkHost(finalUri.Host))
            {
                throw new ConnectorException(
                    "http",
                    $"'{uri.Host}' redirected to the private-network address '{finalUri.Host}' and was refused.");
            }

            var body = await ReadCappedAsync(response, uri, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("GET {Url} -> {Status}", uri, (int)response.StatusCode);
            return new ConnectorHttpResponse((int)response.StatusCode, body, CollectHeaders(response));
        }
    }

    /// <summary>Digs a connect-time refusal out of the transport exception wrapping it.</summary>
    private static WebFetchException? FindRefusal(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is WebFetchException refused)
            {
                return refused;
            }
        }

        return null;
    }

    private static async Task<string> ReadCappedAsync(
        HttpResponseMessage response,
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
        {
            throw new ConnectorException(
                "http",
                $"{uri.Host} returned more than the {MaxResponseBytes / 1024 / 1024} MB response limit.");
        }

#if NET8_0_OR_GREATER
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#endif

        var buffer = new byte[81920];
        using var memory = new MemoryStream();
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            // Content-Length is advisory and often absent on these APIs, so the cap is enforced
            // while reading as well as before it.
            if (memory.Length + read > MaxResponseBytes)
            {
                throw new ConnectorException(
                    "http",
                    $"{uri.Host} exceeded the {MaxResponseBytes / 1024 / 1024} MB response limit while downloading.");
            }

            memory.Write(buffer, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    private static Dictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return headers;
    }

    /// <summary>Creates a client configured the way a connector should present itself.</summary>
    /// <param name="blockPrivateTargets">
    /// Refuse to open a socket to a loopback, private or link-local address, on every redirect hop.
    /// Leave on unless the operator has explicitly chosen to read a self-hosted server on their own
    /// network, and pass the same value to the <see cref="HttpConnectorTransport"/> constructor.
    /// </param>
    /// <returns>A configured client the caller owns and disposes.</returns>
    /// <remarks>
    /// <para>Automatic redirects are allowed but capped, and compression is on because these APIs
    /// return large JSON.</para>
    /// <para><b>The handler is the crawler's, deliberately.</b>
    /// <see cref="HttpWebContentFetcher.CreateGuardedHandler"/> decides on the resolved address at
    /// connect time and then connects to the address it checked, which is the only placement that
    /// covers a literal private address, a public name that resolves into a private range, every hop
    /// of a redirect chain, and a name that answers differently on a second lookup. Writing a second
    /// textual check here instead would reproduce the hole that one was already proven to have.</para>
    /// </remarks>
    public static HttpClient CreateDefaultClient(bool blockPrivateTargets = true)
    {
        var client = new HttpClient(HttpWebContentFetcher.CreateGuardedHandler(blockPrivateTargets))
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("TechieRag/1.0 (+https://github.com/techierathore/TechieRag)");
        return client;
    }
}
