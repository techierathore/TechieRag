using System.Net;
using TechieRag.Connectors;
using TechieRag.Connectors.Http;
using TechieRag.Web;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-032 / BRD-113: the connector transport's SSRF guard and its response cap, pinned without
/// touching the internet.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists.</b> An earlier version of the transport defaulted to allowing
/// private targets and, when told to block them, checked only the host TEXT of the URL — the exact
/// check the web cluster proved walkable with a public DNS name that resolves to loopback. It also
/// built its default client on a bare <see cref="HttpClientHandler"/>, so nothing whatsoever ran at
/// connect time. These tests pin the corrected behaviour.</para>
/// <para><b>The stake is higher here than for the crawler.</b> This transport attaches an
/// <c>Authorization</c> header to every request it makes. A URL steered at an internal endpoint does
/// not merely read it, it presents the source's credential to it.</para>
/// <para>Loopback is used as the private target throughout, and it is genuinely local — these tests
/// open sockets to 127.0.0.1 and to nothing else.</para>
/// </remarks>
public sealed class HttpConnectorTransportTests
{
    /// <summary>
    /// The client the transport ships with refuses to connect to loopback, and the request never
    /// arrives.
    /// </summary>
    /// <remarks>
    /// <para>The transport's own textual check is switched OFF here on purpose, so the only thing
    /// that can refuse this request is the connect-time guard inside the handler. That makes this the
    /// test that fails if <see cref="HttpConnectorTransport.CreateDefaultClient"/> ever goes back to
    /// a plain handler.</para>
    /// <para>The listener's request count is the assertion that matters. Refusing to return the body
    /// after the internal request has been issued leaks nothing to the caller but still lets the
    /// connector be used to poke an internal endpoint, and for anything with a side effect the call
    /// IS the attack.</para>
    /// </remarks>
    [Fact]
    public async Task DefaultClientRefusesToConnectToLoopback()
    {
        using var listener = LoopbackServer.Start();
        using var client = HttpConnectorTransport.CreateDefaultClient();
        var transport = new HttpConnectorTransport(client, logger: null, blockPrivateTargets: false);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => transport.GetAsync(new ConnectorHttpRequest(listener.Url)));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, listener.RequestCount);
    }

    /// <summary>An operator who asks for a self-hosted server on their own network gets one.</summary>
    /// <remarks>
    /// The guard must be an opt-out that an operator can actually take, or every self-managed GitLab
    /// and Data Center wiki becomes unreachable and the setting gets disabled wholesale instead.
    /// </remarks>
    [Fact]
    public async Task PermissiveClientReachesLoopbackWhenPrivateTargetsAreAllowed()
    {
        using var listener = LoopbackServer.Start();
        using var client = HttpConnectorTransport.CreateDefaultClient(blockPrivateTargets: false);
        var transport = new HttpConnectorTransport(client, logger: null, blockPrivateTargets: false);

        var response = await transport.GetAsync(new ConnectorHttpRequest(listener.Url));

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("internal", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, listener.RequestCount);
    }

    /// <summary>A transport constructed with no explicit choice blocks private targets.</summary>
    /// <remarks>
    /// The default is the whole control for every caller that never reads the parameter. The inner
    /// handler asserts it was never invoked, so a flipped default cannot pass by being refused for
    /// some later, unrelated reason.
    /// </remarks>
    [Fact]
    public async Task PrivateTargetsAreBlockedByDefault()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var transport = new HttpConnectorTransport(client);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => transport.GetAsync(new ConnectorHttpRequest("http://127.0.0.1:9/admin")));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>Cloud metadata is refused like any other private address.</summary>
    /// <remarks>169.254.169.254 is the single most valuable SSRF target in a hosted deployment.</remarks>
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://localhost:8080/admin")]
    [InlineData("http://10.0.0.5/internal")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://[::1]:8080/")]
    public async Task WellKnownPrivateTargetsAreRefused(string url)
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var transport = new HttpConnectorTransport(client);

        await Assert.ThrowsAsync<ConnectorException>(
            () => transport.GetAsync(new ConnectorHttpRequest(url)));

        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// A redirect chain that ends on a private address is refused rather than returned, even when
    /// the caller supplied its own unguarded client.
    /// </summary>
    /// <remarks>
    /// Defence in depth behind the connect-time guard. A caller passing its own
    /// <see cref="HttpClient"/> gets no <c>ConnectCallback</c>, so this final-URL check is the only
    /// thing standing between it and an internal response body.
    /// </remarks>
    [Fact]
    public async Task RedirectEndingOnAPrivateAddressIsRefused()
    {
        using var client = new HttpClient(new ArrivedAtHandler("http://169.254.169.254/latest/meta-data/"));
        var transport = new HttpConnectorTransport(client);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => transport.GetAsync(new ConnectorHttpRequest("https://wiki.example.test/rest/api/content")));

        Assert.Contains("private-network", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("169.254.169.254", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A redirect that ends on an ordinary public host is left alone.</summary>
    /// <remarks>The guard must not turn every redirect a site performs into a refused run.</remarks>
    [Fact]
    public async Task RedirectEndingOnAPublicHostIsAllowed()
    {
        using var client = new HttpClient(new ArrivedAtHandler("https://elsewhere.example.test/page"));
        var transport = new HttpConnectorTransport(client);

        var response = await transport.GetAsync(
            new ConnectorHttpRequest("https://wiki.example.test/rest/api/content"));

        Assert.Equal(200, response.StatusCode);
    }

    /// <summary>A body larger than the cap is refused while it downloads, not after.</summary>
    /// <remarks>
    /// The stream declares no length, which is the case that matters: <c>Content-Length</c> is
    /// advisory and often absent on these APIs, so a check that trusts it is no check at all.
    /// </remarks>
    [Fact]
    public async Task OversizedBodyWithoutAContentLengthIsRefusedWhileReading()
    {
        using var client = new HttpClient(
            new EndlessBodyHandler(HttpConnectorTransport.MaxResponseBytes + (1024 * 1024)));
        var transport = new HttpConnectorTransport(client);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => transport.GetAsync(new ConnectorHttpRequest("https://api.example.test/export")));

        Assert.Contains("while downloading", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A declared oversized length is refused before the body is read at all.</summary>
    [Fact]
    public async Task OversizedDeclaredLengthIsRefusedBeforeReading()
    {
        using var client = new HttpClient(
            new DeclaredLengthHandler(HttpConnectorTransport.MaxResponseBytes + 1));
        var transport = new HttpConnectorTransport(client);

        var error = await Assert.ThrowsAsync<ConnectorException>(
            () => transport.GetAsync(new ConnectorHttpRequest("https://api.example.test/export")));

        Assert.Contains("response limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A body inside the cap comes back intact.</summary>
    [Fact]
    public async Task BodyInsideTheCapIsReturned()
    {
        using var client = new HttpClient(new ArrivedAtHandler(null, "{\"ok\":true}"));
        var transport = new HttpConnectorTransport(client);

        var response = await transport.GetAsync(new ConnectorHttpRequest("https://api.example.test/thing"));

        Assert.Equal("{\"ok\":true}", response.Body);
    }

    /// <summary>A non-http scheme is refused before anything is opened.</summary>
    /// <remarks><c>file://</c> is the other half of the SSRF family: local disk rather than the local network.</remarks>
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.test/x")]
    [InlineData("not a url")]
    public async Task NonHttpSchemesAreRefused(string url)
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler);
        var transport = new HttpConnectorTransport(client);

        await Assert.ThrowsAsync<ConnectorException>(
            () => transport.GetAsync(new ConnectorHttpRequest(url)));

        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>Cancellation propagates rather than being reported as an unreachable host.</summary>
    [Fact]
    public async Task CancellationIsNotReportedAsAnUnreachableHost()
    {
        using var client = new HttpClient(new RecordingHandler());
        var transport = new HttpConnectorTransport(client);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transport.GetAsync(new ConnectorHttpRequest("https://api.example.test/x"), cancellation.Token));
    }

    /// <summary>Records whether it was ever asked to send anything.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => Volatile.Read(ref requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
        }
    }

    /// <summary>
    /// Answers 200 while reporting that the request ended up somewhere else, standing in for a
    /// redirect chain the transport already followed.
    /// </summary>
    private sealed class ArrivedAtHandler : HttpMessageHandler
    {
        private readonly string? finalUrl;
        private readonly string body;

        public ArrivedAtHandler(string? finalUrl, string body = "{}")
        {
            this.finalUrl = finalUrl;
            this.body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The transport reads the FINAL url off RequestMessage, which is what a real
            // redirect-following handler rewrites.
            if (finalUrl is not null)
            {
                request.RequestUri = new Uri(finalUrl);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(body),
            });
        }
    }

    /// <summary>Streams more bytes than the cap allows, declaring no length.</summary>
    private sealed class EndlessBodyHandler : HttpMessageHandler
    {
        private readonly long totalBytes;

        public EndlessBodyHandler(long totalBytes) => this.totalBytes = totalBytes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(new ZeroStream(totalBytes)),
            });
    }

    /// <summary>Declares an oversized <c>Content-Length</c> without sending one.</summary>
    private sealed class DeclaredLengthHandler : HttpMessageHandler
    {
        private readonly long declaredLength;

        public DeclaredLengthHandler(long declaredLength) => this.declaredLength = declaredLength;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StringContent("{}");
            content.Headers.ContentLength = declaredLength;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content,
            });
        }
    }

    /// <summary>A read-only stream of a fixed number of zero bytes, allocated lazily.</summary>
    private sealed class ZeroStream : Stream
    {
        private readonly long length;
        private long position;

        public ZeroStream(long length) => this.length = length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = length - position;
            if (remaining <= 0)
            {
                return 0;
            }

            var read = (int)Math.Min(count, remaining);
            Array.Clear(buffer, offset, read);
            position += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A real HTTP server on 127.0.0.1 that counts the requests that reach it.</summary>
    private sealed class LoopbackServer : IDisposable
    {
        private const string ResponseBody = "{\"message\":\"internal service you were never supposed to reach\"}";

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
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                context.Response.Close();
            }
        }
    }
}
