using System.Net;
using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Web;

/// <summary>
/// REQ-RAG-031 / BRD-112: regressions for the SSRF guard, pinned without touching the internet.
/// </summary>
/// <remarks>
/// <para>The live suite (<c>Web/Live/LiveSsrfGuardTests</c>) proves the guard against a real open
/// redirector and a real DNS name. These pin the same behaviours hermetically so a regression is
/// caught by the DEFAULT test run, which is the run that actually gates a change. A security control
/// covered only by an opt-in suite is a security control nobody is running.</para>
/// <para>Loopback is used as the private target throughout, and it is genuinely local — these tests
/// open sockets to 127.0.0.1 and to nothing else.</para>
/// </remarks>
public sealed class HttpWebContentFetcherGuardTests
{
    /// <summary>
    /// The guarded handler refuses to connect to loopback even when the fetcher is not the caller.
    /// </summary>
    /// <remarks>
    /// Deliberately bypasses <see cref="HttpWebContentFetcher"/> so the assertion is about the
    /// HANDLER. The fetcher's own textual check would refuse this URL before a socket was opened,
    /// which would let a broken handler pass.
    /// </remarks>
    [Fact]
    public async Task GuardedHandlerRefusesToConnectToLoopback()
    {
        using var listener = LoopbackServer.Start();
        using var client = new HttpClient(HttpWebContentFetcher.CreateGuardedHandler());

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetStringAsync(listener.Url));

        Assert.Contains("private-network", Flatten(error), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, listener.RequestCount);
    }

    /// <summary>
    /// A redirect the transport declines to follow is still reported by where it pointed, not by its
    /// status code.
    /// </summary>
    /// <remarks>
    /// .NET refuses https→http redirects, so an open redirector aimed at an internal endpoint used to
    /// surface as "replied 302 (Found)" — safe, but it told the operator nothing about the URL they
    /// had pasted.
    /// </remarks>
    [Fact]
    public async Task UnfollowedRedirectToAPrivateHostIsReportedAsRefused()
    {
        using var client = new HttpClient(new RedirectingHandler("http://169.254.169.254/latest/meta-data/"));
        var fetcher = new HttpWebContentFetcher(client);

        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => fetcher.FetchAsync("https://redirector.test/go"));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("169.254.169.254", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("replied 302", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A redirect to an ordinary public host is left alone by the private-target check.
    /// </summary>
    /// <remarks>The guard must not turn every unfollowed redirect into a security incident.</remarks>
    [Fact]
    public async Task UnfollowedRedirectToAPublicHostStillReportsItsStatusCode()
    {
        using var client = new HttpClient(new RedirectingHandler("http://elsewhere.test/page"));
        var fetcher = new HttpWebContentFetcher(client);

        var error = await Assert.ThrowsAsync<WebFetchException>(
            () => fetcher.FetchAsync("https://redirector.test/go"));

        Assert.Contains("302", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An operator who allows private targets gets an unguarded client and can read loopback.
    /// </summary>
    [Fact]
    public async Task PermissiveHandlerReachesLoopbackWhenPrivateTargetsAreAllowed()
    {
        using var listener = LoopbackServer.Start();
        using var client = new HttpClient(HttpWebContentFetcher.CreateGuardedHandler(blockPrivateTargets: false));
        var fetcher = new HttpWebContentFetcher(client, logger: null, blockPrivateTargets: false);

        var page = await fetcher.FetchAsync(listener.Url);

        Assert.Contains("Internal service", page.Text, StringComparison.Ordinal);
        Assert.Equal(1, listener.RequestCount);
    }

    /// <summary>
    /// An IPv4 loopback address wearing an IPv6 mapping is still a loopback address.
    /// </summary>
    /// <remarks>
    /// <c>::ffff:127.0.0.1</c> walks straight through a check that reads four address bytes, because
    /// as an IPv6 address it has sixteen.
    /// </remarks>
    [Theory]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    public void MappedAndCarrierGradeAddressesAreTreatedAsPrivate(string address) =>
        Assert.True(WebCrawlOptions.IsPrivateNetworkAddress(IPAddress.Parse(address)));

    /// <summary>Ordinary public addresses are not caught by the widened ranges.</summary>
    [Theory]
    [InlineData("93.184.215.14")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("99.255.255.255")]
    [InlineData("128.64.0.1")]
    public void PublicAddressesAreNotTreatedAsPrivate(string address) =>
        Assert.False(WebCrawlOptions.IsPrivateNetworkAddress(IPAddress.Parse(address)));

    private static string Flatten(Exception exception)
    {
        var text = new System.Text.StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            text.Append(current.Message).Append(' ');
        }

        return text.ToString();
    }

    /// <summary>Answers every request with a 302 to a fixed target, without following it.</summary>
    private sealed class RedirectingHandler : HttpMessageHandler
    {
        private readonly string location;

        public RedirectingHandler(string location) => this.location = location;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
            response.Headers.Location = new Uri(location);
            return Task.FromResult(response);
        }
    }

    /// <summary>A real HTTP server on 127.0.0.1 that counts the requests that reach it.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private const string ResponseBody =
            "<html><head><title>Admin</title></head>"
            + "<body><p>Internal service you were never supposed to reach.</p></body></html>";

        private readonly HttpListener listener;
        private int requestCount;

        private LoopbackServer(HttpListener listener, string url)
        {
            this.listener = listener;
            Url = url;
        }

        public string Url { get; }

        public int RequestCount => Volatile.Read(ref requestCount);

        public static LoopbackServer Start()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            var server = new LoopbackServer(listener, $"http://127.0.0.1:{port}/");
            _ = server.AcceptLoopAsync();
            return server;
        }

        public void Dispose() => listener.Close();

        private async Task AcceptLoopAsync()
        {
            while (true)
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
