using System.Net;
using System.Text;
using System.Text.Json;
using TechieRag.Mcp;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Mcp;

/// <summary>
/// REQ-RAG-084 / BRD-71 acceptance over a real transport: an MCP server reached through the
/// streamable-HTTP transport has its tools offered in the agent loop and executed through
/// <see cref="McpToolHandler"/>.
/// </summary>
/// <remarks>
/// The server is a stubbed <see cref="HttpMessageHandler"/> that answers JSON-RPC by method and echoes
/// each request's id, so the whole path — <see cref="HttpMcpTransport"/>, <see cref="McpClient"/>'s
/// initialize / tools/list / tools/call handshake, the handler and <see cref="AgentLoopRunner"/> — runs
/// with no network.
/// </remarks>
public class McpHttpAgentLoopTests
{
    /// <summary>
    /// Acceptance of REQ-RAG-084: the HTTP server's <c>search</c> tool is offered to the model as
    /// <c>docs-search</c>, the model's call reaches the server as <c>tools/call</c> with its arguments,
    /// and the server's answer is fed back to the model.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-084 AgentLoopRunsToolFromHttpMcpServer")]
    public async Task AgentLoopRunsToolFromHttpMcpServer()
    {
        var server = new JsonRpcServer();
        var config = new McpServerConfig { Name = "docs", Transport = McpTransportKind.Http, Endpoint = "https://mcp.example.test/mcp" };
        var transport = new HttpMcpTransport(new HttpClient(server) { BaseAddress = new Uri("https://mcp.example.test") }, config, "/mcp");
        await using var handler = await McpToolHandler.CreateAsync([new McpClient(transport, config)], McpTrustPolicy.Strict);

        var llm = new ScriptedToolCallingLlmProvider("docs-search", """{"q":"meaning"}""");
        var response = await new AgentLoopRunner(llm, handler).RunAsync([ChatMessage.User("what is the answer?")]);

        Assert.Equal("done", response.Content);
        Assert.Contains(llm.OfferedTools!, definition => definition.Name == "docs-search");
        Assert.Equal(["initialize", "notifications/initialized", "tools/list", "tools/call"], server.Methods);
        Assert.Equal("search", server.CalledTool);
        Assert.Equal("meaning", server.CalledQuery);
        Assert.Contains(llm.ObservedToolMessages, message => message.Content == "the answer is 42");
    }

    /// <summary>A minimal MCP server over HTTP: answers by JSON-RPC method, echoing the request id.</summary>
    private sealed class JsonRpcServer : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];

        public string? CalledTool { get; private set; }

        public string? CalledQuery { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString()!;
            Methods.Add(method);

            if (!root.TryGetProperty("id", out var idElement))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent(string.Empty) };
            }

            var result = method switch
            {
                "initialize" => """{"protocolVersion":"2025-06-18","capabilities":{"tools":{}},"serverInfo":{"name":"docs-server","version":"1"}}""",
                "tools/list" => """{"tools":[{"name":"search","description":"Searches","inputSchema":{"type":"object","properties":{"q":{"type":"string"}}}}]}""",
                "tools/call" => Call(root.GetProperty("params")),
                _ => "{}"
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"jsonrpc":"2.0","id":{{idElement.GetInt64()}},"result":{{result}}}""", Encoding.UTF8, "application/json")
            };
        }

        private string Call(JsonElement parameters)
        {
            CalledTool = parameters.GetProperty("name").GetString();
            CalledQuery = parameters.GetProperty("arguments").GetProperty("q").GetString();
            return """{"content":[{"type":"text","text":"the answer is 42"}]}""";
        }
    }
}
