using System.Net;
using System.Text;
using System.Text.Json;
using TechieRag.Mcp;
using Xunit;

namespace TechieRag.Tests.Mcp;

/// <summary>
/// Unit tests for the streamable-HTTP MCP transport (REQ-RAG-038), driven through a stubbed
/// <see cref="HttpMessageHandler"/> so no server is contacted.
/// </summary>
public class HttpMcpTransportTests
{
    /// <summary>A plain JSON-RPC response body is read and its result returned.</summary>
    [Fact]
    public async Task PlainJsonResponseIsRead()
    {
        var handler = new StubHandler("""{"jsonrpc":"2.0","id":1,"result":{"tools":[]}}""");
        await using var transport = CreateTransport(handler);

        var result = await transport.SendRequestAsync("tools/list", null);

        Assert.True(result.TryGetProperty("tools", out _));
    }

    /// <summary>A Server-Sent Events body is parsed and the matching JSON-RPC message extracted.</summary>
    [Fact]
    public async Task ServerSentEventsResponseIsParsed()
    {
        var body = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"ok\":true}}\n\n";
        var handler = new StubHandler(body, "text/event-stream");
        await using var transport = CreateTransport(handler);

        var result = await transport.SendRequestAsync("tools/list", null);

        Assert.True(result.GetProperty("ok").GetBoolean());
    }

    /// <summary>Messages for other ids are skipped; only the awaited response is returned.</summary>
    [Fact]
    public async Task UnrelatedMessagesAreSkipped()
    {
        var body = "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{}}\n"
            + "data: {\"jsonrpc\":\"2.0\",\"id\":99,\"result\":{\"wrong\":true}}\n"
            + "data: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"right\":true}}\n";
        var handler = new StubHandler(body, "text/event-stream");
        await using var transport = CreateTransport(handler);

        var result = await transport.SendRequestAsync("tools/list", null);

        Assert.True(result.GetProperty("right").GetBoolean());
    }

    /// <summary>A JSON-RPC error member becomes an McpException carrying the server's code.</summary>
    [Fact]
    public async Task JsonRpcErrorBecomesMcpException()
    {
        var handler = new StubHandler("""{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"method not found"}}""");
        await using var transport = CreateTransport(handler);

        var error = await Assert.ThrowsAsync<McpException>(() => transport.SendRequestAsync("tools/list", null));

        Assert.Equal(-32601, error.ErrorCode);
        Assert.Contains("method not found", error.Message);
    }

    /// <summary>An HTTP failure reports the status only, never the response body.</summary>
    [Fact]
    public async Task HttpFailureDoesNotLeakTheResponseBody()
    {
        var handler = new StubHandler("""{"echoedAuthorization":"Bearer super-secret-token"}""", statusCode: HttpStatusCode.Unauthorized);
        await using var transport = CreateTransport(handler);

        var error = await Assert.ThrowsAsync<McpException>(() => transport.SendRequestAsync("tools/list", null));

        Assert.Contains("401", error.Message);
        Assert.DoesNotContain("super-secret-token", error.Message);
    }

    /// <summary>A session id issued by the server is echoed on every subsequent request.</summary>
    [Fact]
    public async Task SessionIdIsEchoedOnLaterRequests()
    {
        var handler = new StubHandler("""{"jsonrpc":"2.0","id":1,"result":{}}""")
        {
            SessionIdToIssue = "sess-123",
            EchoRequestId = true
        };
        await using var transport = CreateTransport(handler);

        await transport.SendRequestAsync("initialize", null);
        await transport.SendRequestAsync("tools/list", null);

        Assert.Null(handler.SessionIdsSeen[0]);
        Assert.Equal("sess-123", handler.SessionIdsSeen[1]);
    }

    /// <summary>The JSON-RPC envelope carries the version, an id and the method name.</summary>
    [Fact]
    public async Task RequestEnvelopeIsWellFormedJsonRpc()
    {
        var handler = new StubHandler("""{"jsonrpc":"2.0","id":1,"result":{}}""");
        await using var transport = CreateTransport(handler);

        await transport.SendRequestAsync("tools/list", new Dictionary<string, object> { ["cursor"] = "abc" });

        using var document = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal("tools/list", document.RootElement.GetProperty("method").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("id").GetInt64());
        Assert.Equal("abc", document.RootElement.GetProperty("params").GetProperty("cursor").GetString());
    }

    private static HttpMcpTransport CreateTransport(StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://mcp.example.com") };
        var config = new McpServerConfig
        {
            Name = "docs",
            Transport = McpTransportKind.Http,
            Endpoint = "https://mcp.example.com/rpc"
        };
        return new HttpMcpTransport(httpClient, config, "/rpc");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string responseBody;
        private readonly string contentType;
        private readonly HttpStatusCode statusCode;

        public StubHandler(string responseBody, string contentType = "application/json", HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            this.responseBody = responseBody;
            this.contentType = contentType;
            this.statusCode = statusCode;
        }

        public string? SessionIdToIssue { get; init; }

        /// <summary>When set, the response echoes the request's JSON-RPC id, as a real server does.</summary>
        public bool EchoRequestId { get; init; }

        public List<string?> SessionIdsSeen { get; } = [];

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SessionIdsSeen.Add(request.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.FirstOrDefault() : null);
            if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            var body = responseBody;
            if (EchoRequestId && LastBody is not null)
            {
                using var requestDocument = JsonDocument.Parse(LastBody);
                var id = requestDocument.RootElement.GetProperty("id").GetInt64();
                body = $$$"""{"jsonrpc":"2.0","id":{{{id}}},"result":{}}""";
            }

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            };

            if (SessionIdToIssue is not null) response.Headers.TryAddWithoutValidation("Mcp-Session-Id", SessionIdToIssue);

            return response;
        }
    }
}
