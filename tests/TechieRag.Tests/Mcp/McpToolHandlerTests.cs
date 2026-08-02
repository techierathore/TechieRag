using TechieRag.Mcp;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Mcp;

/// <summary>
/// Unit tests proving MCP tools reach the agent through the existing <c>IToolHandler</c> contract
/// (REQ-RAG-038) — same shape as a delegate registered on <c>ToolRegistry</c>, including the rule
/// that a failing tool is a message back to the model rather than an exception.
/// </summary>
public class McpToolHandlerTests
{
    private const string TwoToolsJson = """
    {"tools":[
      {"name":"search","description":"Searches the docs","inputSchema":{"type":"object","properties":{"q":{"type":"string"}}}},
      {"name":"fetch","description":"Fetches a page","inputSchema":{"type":"object","properties":{}}}
    ]}
    """;

    /// <summary>Advertised tools become tool definitions, qualified by server name.</summary>
    [Fact]
    public async Task AdvertisedToolsBecomeQualifiedToolDefinitions()
    {
        await using var handler = await CreateHandlerAsync(new FakeMcpTransport("docs", TwoToolsJson));

        Assert.Equal(2, handler.ToolDefinitions.Count);
        Assert.Contains(handler.ToolDefinitions, definition => definition.Name == "docs-search");
        Assert.Contains(handler.ToolDefinitions, definition => definition.Name == "docs-fetch");
        Assert.Equal("Searches the docs", handler.ToolDefinitions.First(d => d.Name == "docs-search").Description);
    }

    /// <summary>The server's JSON Schema is passed through verbatim as the tool's parameter schema.</summary>
    [Fact]
    public async Task ToolSchemaIsPassedThrough()
    {
        await using var handler = await CreateHandlerAsync(new FakeMcpTransport("docs", TwoToolsJson));

        var schema = handler.ToolDefinitions.First(definition => definition.Name == "docs-search").ParametersSchema;

        Assert.Contains("\"q\"", schema);
    }

    /// <summary>Executing a qualified tool calls the server's unqualified tool and returns its text.</summary>
    [Fact]
    public async Task ExecutingToolUnqualifiesTheNameAndReturnsText()
    {
        var invoked = new List<string>();
        var transport = new FakeMcpTransport("docs", TwoToolsJson, (name, _) =>
        {
            invoked.Add(name);
            return """{"content":[{"type":"text","text":"three results"}]}""";
        });

        await using var handler = await CreateHandlerAsync(transport);
        var result = await handler.ExecuteToolAsync(Call("docs-search", """{"q":"rag"}"""));

        Assert.Equal(["search"], invoked);
        Assert.True(result.IsSuccess);
        Assert.Equal("three results", result.Content);
        Assert.Equal("""{"q":"rag"}""", transport.LastArgumentsJson);
    }

    /// <summary>A tool the server flagged as an error comes back as an unsuccessful result, not an exception.</summary>
    [Fact]
    public async Task ServerReportedErrorBecomesUnsuccessfulResult()
    {
        var transport = new FakeMcpTransport("docs", TwoToolsJson,
            (_, _) => """{"isError":true,"content":[{"type":"text","text":"no such page"}]}""");

        await using var handler = await CreateHandlerAsync(transport);
        var result = await handler.ExecuteToolAsync(Call("docs-fetch", "{}"));

        Assert.False(result.IsSuccess);
        Assert.Equal("no such page", result.Content);
    }

    /// <summary>A transport that throws is reported to the model, never rethrown into the agent loop.</summary>
    [Fact]
    public async Task TransportFailureBecomesUnsuccessfulResult()
    {
        var transport = new FakeMcpTransport("docs", TwoToolsJson,
            (_, _) => throw new McpException("docs", "server went away"));

        await using var handler = await CreateHandlerAsync(transport);
        var result = await handler.ExecuteToolAsync(Call("docs-search", "{}"));

        Assert.False(result.IsSuccess);
        Assert.Contains("server went away", result.Content);
    }

    /// <summary>An unknown tool name is reported rather than throwing.</summary>
    [Fact]
    public async Task UnknownToolIsReported()
    {
        await using var handler = await CreateHandlerAsync(new FakeMcpTransport("docs", TwoToolsJson));

        var result = await handler.ExecuteToolAsync(Call("docs-nope", "{}"));

        Assert.False(result.IsSuccess);
        Assert.Contains("Unknown tool", result.Content);
    }

    /// <summary>An oversized server response is truncated so one tool call cannot evict the conversation.</summary>
    [Fact]
    public async Task OversizedResultIsTruncated()
    {
        var huge = new string('x', 500);
        var transport = new FakeMcpTransport("docs", TwoToolsJson,
            (_, _) => $$"""{"content":[{"type":"text","text":"{{huge}}"}]}""");

        await using var handler = await McpToolHandler.CreateAsync(
            [new McpClient(transport, Config("docs"))],
            new McpTrustPolicy { MaxToolResultCharacters = 100 });

        var result = await handler.ExecuteToolAsync(Call("docs-search", "{}"));

        Assert.Contains("truncated by TechieRag", result.Content);
        Assert.True(result.Content.Length < huge.Length);
    }

    /// <summary>A tool excluded by the configured allow-list is never advertised to the model.</summary>
    [Fact]
    public async Task AllowListHidesUnlistedTools()
    {
        var config = new McpServerConfig
        {
            Name = "docs",
            Transport = McpTransportKind.Http,
            Endpoint = "https://mcp.example.com",
            AllowedTools = ["search"]
        };

        await using var handler = await McpToolHandler.CreateAsync(
            [new McpClient(new FakeMcpTransport("docs", TwoToolsJson), config)],
            McpTrustPolicy.Strict);

        Assert.Single(handler.ToolDefinitions);
        Assert.Equal("docs-search", handler.ToolDefinitions[0].Name);
    }

    /// <summary>A tool outside the allow-list is refused at call time too, not only hidden.</summary>
    [Fact]
    public async Task AllowListRefusesDirectCallToUnlistedTool()
    {
        var config = new McpServerConfig
        {
            Name = "docs",
            Transport = McpTransportKind.Http,
            Endpoint = "https://mcp.example.com",
            AllowedTools = ["search"]
        };
        await using var client = new McpClient(new FakeMcpTransport("docs", TwoToolsJson), config);

        await Assert.ThrowsAsync<McpException>(() => client.CallToolAsync("fetch", "{}"));
    }

    /// <summary>Two servers exposing the same tool name stay distinguishable to the model.</summary>
    [Fact]
    public async Task TwoServersWithTheSameToolNameStayDistinct()
    {
        var first = new McpClient(new FakeMcpTransport("alpha", TwoToolsJson), Config("alpha"));
        var second = new McpClient(new FakeMcpTransport("beta", TwoToolsJson), Config("beta"));

        await using var handler = await McpToolHandler.CreateAsync([first, second], McpTrustPolicy.Strict);

        Assert.Equal(4, handler.ToolDefinitions.Count);
        Assert.Contains(handler.ToolDefinitions, definition => definition.Name == "alpha-search");
        Assert.Contains(handler.ToolDefinitions, definition => definition.Name == "beta-search");
    }

    /// <summary>A qualified name too long for the provider limit is shortened, not dropped.</summary>
    [Fact]
    public void OverlongQualifiedNameIsShortenedWithinTheProviderLimit()
    {
        var qualified = McpToolHandler.QualifyToolName("a-very-long-server-name-indeed", new string('t', 60));

        Assert.True(qualified.Length <= 64);
        Assert.StartsWith("a-very-long-server-name-indeed-", qualified);
    }

    /// <summary>Local tools win over an MCP tool of the same name when composed.</summary>
    [Fact]
    public async Task LocalToolsTakePrecedenceOverMcpTools()
    {
        var registry = new ToolRegistry();
        registry.Register("docs-search", "local override", """{"type":"object"}""", _ => "local answer");

        await using var mcp = await CreateHandlerAsync(new FakeMcpTransport("docs", TwoToolsJson));
        var composite = new CompositeToolHandler(registry, mcp);

        var result = await composite.ExecuteToolAsync(Call("docs-search", "{}"));

        Assert.Equal("local answer", result.Content);

        // The MCP server's own docs-search is hidden entirely, so the name the model sees and the
        // handler that runs can never disagree: local docs-search plus MCP docs-fetch.
        Assert.Equal(2, composite.ToolDefinitions.Count);
        Assert.Equal("local override", composite.ToolDefinitions.First(d => d.Name == "docs-search").Description);
    }

    private static Task<McpToolHandler> CreateHandlerAsync(FakeMcpTransport transport) =>
        McpToolHandler.CreateAsync([new McpClient(transport, Config(transport.ServerName))], McpTrustPolicy.Strict);

    private static McpServerConfig Config(string name) => new()
    {
        Name = name,
        Transport = McpTransportKind.Http,
        Endpoint = "https://mcp.example.com"
    };

    private static ToolCall Call(string name, string argumentsJson) =>
        new() { Id = "call-1", Name = name, ArgumentsJson = argumentsJson };
}
