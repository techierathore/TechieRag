using System.Net;
using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Web.Live;

/// <summary>
/// REQ-RAG-031 / BRD-112: the SSRF guard, proved against a real open redirector and a real DNS name.
/// </summary>
/// <remarks>
/// <para>This is the one file in the live suite where a failure is a vulnerability rather than a
/// cosmetic defect. Web ingestion takes a URL from a user and follows redirects and links it did not
/// choose, so without a guard the application is a proxy into whatever network it is running on:
/// <c>http://localhost:8080/admin</c>, a database on the LAN, or <c>169.254.169.254</c> and the
/// cloud instance's credentials.</para>
/// <para><b>Why a real redirector and a real listener.</b> A mocked 302 proves the code path is
/// reachable. It cannot prove the guard is applied to the URL the HTTP stack actually ended up at,
/// because the mock decided that value. Here a third-party host issues a genuine 302 to a loopback
/// address, and a real listener on that address records whether the request arrived — which
/// separates "the body was refused" from "the internal request never happened". Only the second is
/// actually safe: a blind SSRF that reaches an internal endpoint has already had its effect by the
/// time the body is discarded.</para>
/// </remarks>
[Trait("Category", LiveNetworkFactAttribute.CategoryName)]
public sealed class LiveSsrfGuardTests : IDisposable
{
    private readonly HttpClient httpClient = HttpWebContentFetcher.CreateDefaultClient();
    private HttpClient? permissiveClient;

    /// <summary>
    /// A real 302 that lands on loopback is refused, and — the part that matters — the request never
    /// reaches the loopback listener at all.
    /// </summary>
    /// <remarks>
    /// The listener is the assertion. Refusing to return the body after the internal request has
    /// already been issued leaks nothing back to the caller but still lets an attacker use the
    /// application to poke an internal endpoint, which for anything with a side effect is the whole
    /// attack.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RealRedirectToLoopbackIsRefusedBeforeTheInternalRequestIsMade()
    {
        using var listener = LoopbackListener.Start();

        // Plain http on purpose. An https seed would hit .NET's refusal to downgrade the scheme and
        // the redirect would never be followed, so the test would pass without the guard existing.
        var redirectUrl = LiveTargets.RedirectOverPlainHttpTo(listener.Url);

        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => Fetcher(blockPrivateTargets: true).FetchAsync(redirectUrl));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, listener.RequestCount);
    }

    /// <summary>
    /// An https redirect aimed at an internal http endpoint is reported as a refused private target,
    /// not as an ordinary 302.
    /// </summary>
    /// <remarks>
    /// .NET declines to follow the scheme downgrade, which is safe but silent: the fetch surfaced as
    /// "postman-echo.com replied 302 (Found)" and an operator reading that has no idea the URL they
    /// pasted was pointing into their own network.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RealHttpsRedirectAimedAtLoopbackIsReportedAsPrivateRatherThanAsA302()
    {
        using var listener = LoopbackListener.Start();
        var redirectUrl = LiveTargets.RedirectTo(listener.Url);

        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => Fetcher(blockPrivateTargets: true).FetchAsync(redirectUrl));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("replied 302", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, listener.RequestCount);
    }

    /// <summary>
    /// A hostname that resolves to loopback is refused even though nothing in the URL text looks
    /// private.
    /// </summary>
    /// <remarks>
    /// The classic bypass. A guard that only inspects the literal host string sees
    /// <c>127.0.0.1.nip.io</c> as an ordinary public domain and lets the request through to
    /// 127.0.0.1. Resolving the name is what closes it.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RealHostnameResolvingToLoopbackIsRefused()
    {
        using var listener = LoopbackListener.Start(LiveTargets.HostnameResolvingToLoopback);

        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => Fetcher(blockPrivateTargets: true).FetchAsync(listener.Url));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, listener.RequestCount);
    }

    /// <summary>
    /// A real 302 that lands on a link-local cloud metadata address is refused.
    /// </summary>
    /// <remarks>
    /// 169.254.169.254 is the single most valuable SSRF target in existence — on most cloud
    /// providers it hands out instance credentials to anything that asks. It is covered separately
    /// from loopback because it is a different branch of the address check and the consequence of
    /// missing it is not comparable to the others.
    /// </remarks>
    [LiveNetworkFact]
    public async Task RealRedirectToCloudMetadataAddressIsRefused()
    {
        var redirectUrl = LiveTargets.RedirectTo("http://169.254.169.254/latest/meta-data/");

        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => Fetcher(blockPrivateTargets: true).FetchAsync(redirectUrl));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An operator who deliberately allows private targets can still read from their own network.
    /// </summary>
    /// <remarks>
    /// A guard that cannot be turned off is not a guard, it is a missing feature. Crawling an
    /// intranet is a legitimate thing to want; the requirement is only that it be chosen explicitly.
    /// </remarks>
    [LiveNetworkFact]
    public async Task LoopbackIsReachableWhenPrivateTargetsAreExplicitlyAllowed()
    {
        using var listener = LoopbackListener.Start();

        var page = await Fetcher(blockPrivateTargets: false).FetchAsync(listener.Url);

        Assert.Contains("Internal service", page.Text, StringComparison.Ordinal);
        Assert.Equal(1, listener.RequestCount);
    }

    /// <summary>Releases the shared client.</summary>
    public void Dispose()
    {
        httpClient.Dispose();
        permissiveClient?.Dispose();
    }

    /// <summary>
    /// Builds a fetcher over the client that matches the policy, exactly as the application does.
    /// </summary>
    /// <remarks>
    /// The guard lives in the message handler, which is fixed when a client is built, so "allow
    /// private targets" cannot be expressed by passing a flag to a fetcher over a guarded client —
    /// the socket would be refused before the fetcher's own flag was consulted. This mirrors
    /// <c>AddTechieDeskWebIngestion</c>, which registers two clients for the same reason.
    /// </remarks>
    private HttpWebContentFetcher Fetcher(bool blockPrivateTargets)
    {
        if (blockPrivateTargets)
        {
            return new HttpWebContentFetcher(httpClient, logger: null, blockPrivateTargets: true);
        }

        permissiveClient ??= HttpWebContentFetcher.CreateDefaultClient(blockPrivateTargets: false);
        return new HttpWebContentFetcher(permissiveClient, logger: null, blockPrivateTargets: false);
    }

    /// <summary>
    /// A real HTTP server on 127.0.0.1 standing in for whatever an attacker hopes to reach.
    /// </summary>
    /// <remarks>
    /// It exists to be counted. Its body is incidental; <see cref="RequestCount"/> is the evidence.
    /// </remarks>
    private sealed class LoopbackListener : IDisposable
    {
        private const string ResponseBody =
            "<html><head><title>Admin</title></head>"
            + "<body><p>Internal service you were never supposed to reach.</p></body></html>";

        private readonly HttpListener listener;
        private readonly CancellationTokenSource cancellation = new();
        private int requestCount;

        private LoopbackListener(HttpListener listener, string url)
        {
            this.listener = listener;
            Url = url;
        }

        /// <summary>Gets the URL the listener answers on.</summary>
        public string Url { get; }

        /// <summary>Gets how many requests actually arrived.</summary>
        public int RequestCount => Volatile.Read(ref requestCount);

        /// <summary>Starts a listener on a free loopback port.</summary>
        /// <param name="advertisedHost">Host to put in <see cref="Url"/>; defaults to 127.0.0.1.</param>
        /// <returns>The running listener.</returns>
        public static LoopbackListener Start(string? advertisedHost = null)
        {
            var port = FreePort();
            var listener = new HttpListener();

            // Bound to the literal address; the advertised host may be a DNS name that resolves here.
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            var instance = new LoopbackListener(listener, $"http://{advertisedHost ?? "127.0.0.1"}:{port}/");
            _ = instance.AcceptLoopAsync();
            return instance;
        }

        public void Dispose()
        {
            cancellation.Cancel();
            listener.Close();
            cancellation.Dispose();
        }

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task AcceptLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
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
