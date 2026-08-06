using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieRag.Web;

/// <summary>
/// Fetches pages over HTTP (REQ-RAG-031 / BRD-112).
/// </summary>
public sealed class HttpWebContentFetcher : IWebContentFetcher
{
    /// <summary>Largest response body accepted, in bytes.</summary>
    /// <remarks>
    /// A cap is required, not defensive garnish: without one a single link to a large file streams
    /// into memory and takes the process with it. 8 MB is far beyond any HTML document that is
    /// actually prose.
    /// </remarks>
    public const int MaxContentBytes = 8 * 1024 * 1024;

    private readonly HttpClient httpClient;
    private readonly ILogger<HttpWebContentFetcher> logger;
    private readonly bool blockPrivateTargets;

    /// <summary>Initializes a new instance of the <see cref="HttpWebContentFetcher"/> class.</summary>
    /// <param name="httpClient">Client used for the fetch.</param>
    /// <param name="logger">Diagnostics.</param>
    /// <param name="blockPrivateTargets">Refuse loopback/private/link-local hosts. See <see cref="WebCrawlOptions.BlockPrivateNetworkTargets"/>.</param>
    public HttpWebContentFetcher(
        HttpClient httpClient,
        ILogger<HttpWebContentFetcher>? logger = null,
        bool blockPrivateTargets = true)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.logger = logger ?? NullLogger<HttpWebContentFetcher>.Instance;
        this.blockPrivateTargets = blockPrivateTargets;
    }

    /// <inheritdoc />
    public async Task<WebPage> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new WebFetchException(url, $"'{url}' is not an absolute http or https URL.");
        }

        if (blockPrivateTargets && WebCrawlOptions.IsPrivateNetworkHost(uri.Host))
        {
            throw new WebFetchException(url, $"'{uri.Host}' is a private-network address and was refused.");
        }

        // The textual check above sees only what the URL says. A name that resolves into a private
        // range — 127.0.0.1.nip.io is a real, public, freely available one — reads as an ordinary
        // domain and lands on loopback. Resolving before the request is what closes that.
        if (blockPrivateTargets)
        {
            await RefusePrivateResolutionAsync(uri, url, cancellationToken).ConfigureAwait(false);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A client built by CreateGuardedHandler refuses the socket itself, on the address it is
            // about to connect to. That refusal is the accurate message; the transport's "connection
            // failed" wrapper around it is not.
            if (FindRefusal(ex) is { } refused)
            {
                throw refused;
            }

            throw new WebFetchException(url, $"{uri.Host} could not be reached: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // A redirect the transport declined to follow still reveals where it was pointing.
                // .NET refuses https→http downgrades outright, so an open redirector aimed at an
                // internal http endpoint surfaces here as a bare 302 — reporting it as "replied 302"
                // would hide an attempted SSRF behind a status code.
                RefusePrivateRedirectTarget(response, uri, url, blockPrivateTargets);

                throw new WebFetchException(
                    url,
                    $"{uri.Host} replied {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            // A redirect to a private address bypasses the pre-flight check entirely, so the FINAL
            // URL is re-checked. Without this, the SSRF guard is one HTTP 302 away from useless.
            var finalUri = response.RequestMessage?.RequestUri ?? uri;
            if (blockPrivateTargets && WebCrawlOptions.IsPrivateNetworkHost(finalUri.Host))
            {
                throw new WebFetchException(
                    url,
                    $"'{url}' redirected to the private-network address '{finalUri.Host}' and was refused.");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null
                && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                && !mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                throw new WebFetchException(
                    url,
                    $"{url} is {mediaType}, which is not a readable web page. Ingest binary documents as files instead.");
            }

            if (response.Content.Headers.ContentLength is > MaxContentBytes)
            {
                throw new WebFetchException(
                    url,
                    $"{url} is larger than the {MaxContentBytes / 1024 / 1024} MB limit for a web page.");
            }

            var html = await ReadCappedAsync(response, url, cancellationToken).ConfigureAwait(false);
            logger.LogDebug("Fetched {Url} ({Bytes} bytes)", url, html.Length);
            return WebPageReader.Read(html, url, finalUri.ToString());
        }
    }

    /// <summary>Refuses a host that resolves into a private range, before any request is made.</summary>
    /// <remarks>
    /// A resolution failure is NOT a refusal. An unknown host is an ordinary "could not be reached",
    /// and turning DNS trouble into "this is a private address" would tell the operator something
    /// false about their own URL.
    /// </remarks>
    private static async Task RefusePrivateResolutionAsync(
        Uri uri,
        string url,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(uri.DnsSafeHost, out _))
        {
            // Already covered by the literal check; resolving it again buys nothing.
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return;
        }

        var blocked = Array.Find(addresses, WebCrawlOptions.IsPrivateNetworkAddress);
        if (blocked is not null)
        {
            throw new WebFetchException(
                url,
                $"'{uri.Host}' resolves to the private-network address {blocked} and was refused.");
        }
    }

    /// <summary>Reports an unfollowed redirect that was aimed at a private-network host.</summary>
    private static void RefusePrivateRedirectTarget(
        HttpResponseMessage response,
        Uri requestUri,
        string url,
        bool blockPrivateTargets)
    {
        if (!blockPrivateTargets
            || (int)response.StatusCode is < 300 or >= 400
            || response.Headers.Location is not { } location)
        {
            return;
        }

        var target = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        if (WebCrawlOptions.IsPrivateNetworkHost(target.Host))
        {
            throw new WebFetchException(
                url,
                $"'{url}' redirected to the private-network address '{target.Host}' and was refused.");
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

    // Content-Length is advisory and often absent, so the cap is also enforced while reading.
    private static async Task<string> ReadCappedAsync(
        HttpResponseMessage response,
        string url,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var buffer = new byte[81920];
        using var memory = new MemoryStream();
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > MaxContentBytes)
            {
                throw new WebFetchException(
                    url,
                    $"{url} exceeded the {MaxContentBytes / 1024 / 1024} MB limit while downloading.");
            }

            memory.Write(buffer, 0, read);
        }

        var encoding = ResolveEncoding(response);
        return encoding.GetString(memory.ToArray());
    }

    private static System.Text.Encoding ResolveEncoding(HttpResponseMessage response)
    {
        var charset = response.Content.Headers.ContentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset))
        {
            return System.Text.Encoding.UTF8;
        }

        try
        {
            return System.Text.Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            // An unknown charset is a reason to fall back, not to fail an otherwise good page.
            return System.Text.Encoding.UTF8;
        }
    }

    /// <summary>Creates a client configured the way a polite crawler should present itself.</summary>
    /// <param name="blockPrivateTargets">
    /// Refuse to open a socket to a loopback, private or link-local address. Leave on unless the
    /// operator has explicitly chosen to read from their own network.
    /// </param>
    /// <returns>A configured client the caller owns.</returns>
    public static HttpClient CreateDefaultClient(bool blockPrivateTargets = true)
    {
        var client = new HttpClient(CreateGuardedHandler(blockPrivateTargets))
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("TechieRag/1.0 (+https://github.com/techierathore/TechieRag)");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        return client;
    }

    /// <summary>
    /// Creates the message handler ingestion must use: one that refuses to connect to a private
    /// address, on every hop.
    /// </summary>
    /// <param name="blockPrivateTargets">Refuse loopback, private and link-local addresses.</param>
    /// <returns>A handler the caller owns and passes to an <see cref="HttpClient"/>.</returns>
    /// <remarks>
    /// <para><b>Why the check lives at connect time.</b> Checking the URL before the request and the
    /// final URL after it leaves a real hole in between: the transport follows redirects on its own,
    /// so by the time a private final URL can be observed, the request to that private address has
    /// already been sent and whatever it does has already happened. Refusing the body afterwards
    /// prevents the response leaking back to the caller; it does not prevent the internal call. For
    /// anything with a side effect — an admin endpoint, a queue, a metadata service — the call IS the
    /// attack.</para>
    /// <para>Deciding on the resolved address, at the moment of connecting, is the only placement
    /// that covers all four shapes at once: a literal private address, a public hostname that
    /// resolves into a private range, any hop of a redirect chain, and a name that answers
    /// differently on the second lookup than the first. The socket is then connected to the address
    /// that was checked, not to a fresh resolution, so there is no window to rebind into.</para>
    /// </remarks>
    public static SocketsHttpHandler CreateGuardedHandler(bool blockPrivateTargets = true)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        if (blockPrivateTargets)
        {
            handler.ConnectCallback = ConnectToPublicAddressAsync;
        }

        return handler;
    }

    /// <summary>Connects only to an address that is not on a private network.</summary>
    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

        foreach (var address in addresses)
        {
            if (WebCrawlOptions.IsPrivateNetworkAddress(address))
            {
                // Thrown rather than skipped: a host with one private and one public address is a
                // rebinding attempt, not a multi-homed server worth being lenient about.
                throw new WebFetchException(
                    $"{host}:{port}",
                    $"'{host}' resolves to the private-network address {address} and was refused.");
            }
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            // Connect to the addresses that were CHECKED, never to a fresh resolution of the name.
            await socket.ConnectAsync(addresses, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
