using System.Net;
using TechieRag.Connectors.Http;
using Xunit;

namespace TechieRag.Tests.Connectors;

/// <summary>
/// REQ-RAG-083 / BRD-127 (TR-RAG-021): <see cref="IConnectorTransport"/> reaches methods beyond GET
/// where a connector needs them — a search API that takes a query body — through the same guarded
/// transport. The only sockets opened are to a loopback listener in this process.
/// </summary>
public sealed class ConnectorTransportMethodTests
{
    /// <summary>A POST with a JSON body arrives at the server as a POST carrying that body.</summary>
    [Fact]
    public async Task APostCarriesItsMethodAndBody()
    {
        using var server = EchoServer.Start();
        using var client = HttpConnectorTransport.CreateDefaultClient(blockPrivateTargets: false);
        var transport = new HttpConnectorTransport(client, logger: null, blockPrivateTargets: false);

        var response = await transport.SendAsync(new ConnectorHttpRequest(server.Url)
        {
            Method = "POST",
            Body = """{"cql":"space = DOCS"}"""
        });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("POST", server.LastMethod);
        Assert.Equal("""{"cql":"space = DOCS"}""", server.LastBody);
        Assert.StartsWith("application/json", server.LastContentType, StringComparison.Ordinal);
    }

    /// <summary>A non-GET request is refused for a private target exactly as a GET is.</summary>
    [Fact]
    public async Task APostToAPrivateTargetIsRefusedLikeAGet()
    {
        using var server = EchoServer.Start();
        using var client = HttpConnectorTransport.CreateDefaultClient();
        var transport = new HttpConnectorTransport(client);

        await Assert.ThrowsAsync<TechieRag.Connectors.ConnectorException>(
            () => transport.SendAsync(new ConnectorHttpRequest(server.Url) { Method = "POST", Body = "{}" }));

        Assert.Null(server.LastMethod);
    }

    /// <summary>
    /// A transport written before this change keeps compiling and keeps working for GET through the
    /// interface's default; asked for anything else it says so instead of silently sending a GET.
    /// </summary>
    [Fact]
    public async Task AGetOnlyTransportRefusesOtherMethodsHonestly()
    {
        IConnectorTransport transport = new FakeConnectorTransport().Route("/items", "[]");

        var get = await transport.SendAsync(new ConnectorHttpRequest("https://example.test/items"));

        Assert.Equal(200, get.StatusCode);
        await Assert.ThrowsAsync<NotSupportedException>(
            () => transport.SendAsync(new ConnectorHttpRequest("https://example.test/items") { Method = "POST" }));
    }

    /// <summary>The rate-limit decorator waits out a throttle on a POST as it does on a GET.</summary>
    [Fact]
    public async Task TheRateLimiterRetriesAThrottledPost()
    {
        var inner = new ThrottleOnceTransport();
        var transport = new RateLimitedTransport(inner) { DefaultDelay = TimeSpan.Zero };

        var response = await transport.SendAsync(new ConnectorHttpRequest("https://example.test/search") { Method = "POST" });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(new[] { "POST", "POST" }, inner.Methods);
    }

    /// <summary>Answers 429 once, then 200, recording the method of every request.</summary>
    private sealed class ThrottleOnceTransport : IConnectorTransport
    {
        public List<string> Methods { get; } = [];

        public Task<ConnectorHttpResponse> GetAsync(ConnectorHttpRequest request, CancellationToken cancellationToken = default) =>
            SendAsync(request with { }, cancellationToken);

        public Task<ConnectorHttpResponse> SendAsync(ConnectorHttpRequest request, CancellationToken cancellationToken = default)
        {
            Methods.Add(request.Method);
            return Task.FromResult(new ConnectorHttpResponse(Methods.Count == 1 ? 429 : 200, "{}"));
        }
    }

    /// <summary>A loopback HTTP listener that records the method, body and content type it received.</summary>
    private sealed class EchoServer : IDisposable
    {
        private readonly HttpListener listener;

        private EchoServer(HttpListener listener, string url)
        {
            this.listener = listener;
            Url = url;
        }

        public string Url { get; }

        public string? LastMethod { get; private set; }

        public string? LastBody { get; private set; }

        public string? LastContentType { get; private set; }

        public static EchoServer Start()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            var server = new EchoServer(listener, $"http://127.0.0.1:{port}/");
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
                    return;
                }

                using (var reader = new StreamReader(context.Request.InputStream))
                {
                    LastBody = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                LastContentType = context.Request.ContentType;
                LastMethod = context.Request.HttpMethod;

                var bytes = System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}");
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                context.Response.Close();
            }
        }
    }
}
