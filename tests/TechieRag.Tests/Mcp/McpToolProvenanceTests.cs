using TechieRag.Mcp;
using Xunit;

namespace TechieRag.Tests.Mcp;

/// <summary>
/// Covers per-tool server provenance (TR-RAG-041): a host applying a PER-SERVER policy — such as
/// gating tools hosted off the machine by an HTTP server through an egress prompt — can look the
/// server up instead of deducing it from a qualified tool name.
/// </summary>
/// <remarks>
/// The name-prefix heuristic this replaces is sound only because of two facts that were never
/// contractual: <see cref="McpToolHandler.QualifyToolName"/> puts the server name first, and it
/// truncates from the right. It also cannot separate a server named <c>acme</c> from one named
/// <c>acme-eu</c>, which is why that pair is exercised here explicitly — under the heuristic, a tool
/// belonging to <c>acme-eu</c> matches the <c>acme-</c> prefix too.
/// </remarks>
public class McpToolProvenanceTests
{
    private const string SearchToolJson =
        """{"tools":[{"name":"search","description":"Searches","inputSchema":{"type":"object"}}]}""";

    /// <summary>Each qualified tool name resolves to the server that advertised it.</summary>
    [Fact]
    public async Task EveryQualifiedToolNameResolvesToItsServer()
    {
        await using var handler = await McpToolHandler.CreateAsync(
        [
            new McpClient(new FakeMcpTransport("ledger", SearchToolJson), Config("ledger")),
            new McpClient(new FakeMcpTransport("local-index", SearchToolJson), Config("local-index"))
        ],
        McpTrustPolicy.Strict);

        Assert.Equal("ledger", handler.ServerNameFor("ledger-search"));
        Assert.Equal("local-index", handler.ServerNameFor("local-index-search"));
    }

    /// <summary>
    /// The ambiguous pair the prefix heuristic cannot separate resolves exactly, so a per-server
    /// security decision is a lookup rather than a guess.
    /// </summary>
    [Fact]
    public async Task ServersWhoseNamesSharreAPrefixResolveExactly()
    {
        await using var handler = await McpToolHandler.CreateAsync(
        [
            new McpClient(new FakeMcpTransport("acme", SearchToolJson), Config("acme")),
            new McpClient(new FakeMcpTransport("acme-eu", SearchToolJson), Config("acme-eu"))
        ],
        McpTrustPolicy.Strict);

        Assert.Equal("acme", handler.ServerNameFor("acme-search"));
        Assert.Equal("acme-eu", handler.ServerNameFor("acme-eu-search"));
        Assert.NotEqual(
            handler.ServerNameFor("acme-search"),
            handler.ServerNameFor("acme-eu-search"));
    }

    /// <summary>A name no server advertised resolves to null rather than to a plausible server.</summary>
    [Fact]
    public async Task AnUnknownToolNameResolvesToNull()
    {
        await using var handler = await McpToolHandler.CreateAsync(
            [new McpClient(new FakeMcpTransport("ledger", SearchToolJson), Config("ledger"))],
            McpTrustPolicy.Strict);

        Assert.Null(handler.ServerNameFor("ledger-nonexistent"));
        Assert.Null(handler.ServerNameFor("rag-search"));
    }

    /// <summary>
    /// The workspace result relates each server to what it advertised, and answers the reverse
    /// question for a qualified name — a local (non-MCP) tool correctly having no server.
    /// </summary>
    [Fact]
    public async Task TheWorkspaceResultRelatesToolsToServersInBothDirections()
    {
        await using var mcpHandler = await McpToolHandler.CreateAsync(
            [new McpClient(new FakeMcpTransport("ledger", SearchToolJson), Config("ledger"))],
            McpTrustPolicy.Strict);

        var descriptors = new Dictionary<string, IReadOnlyList<McpToolDescriptor>>(StringComparer.Ordinal)
        {
            ["ledger"] = [new McpToolDescriptor("search", "Searches", """{"type":"object"}""")]
        };

        await using var workspaceTools = new McpWorkspaceTools(
            mcpHandler, mcpHandler, ["ledger"], [], descriptors);

        Assert.Equal("search", Assert.Single(workspaceTools.ToolsByServer["ledger"]).Name);
        Assert.Equal("ledger", workspaceTools.ServerNameFor("ledger-search"));

        // A locally registered skill has no MCP server behind it, and null is the correct answer.
        Assert.Null(workspaceTools.ServerNameFor("rag-search"));
    }

    /// <summary>Builds a permitted HTTP server configuration for the given name.</summary>
    /// <param name="name">The configured server name.</param>
    /// <returns>The configuration.</returns>
    private static McpServerConfig Config(string name) => new()
    {
        Name = name,
        Transport = McpTransportKind.Http,
        Endpoint = "https://mcp.example.com/rpc"
    };
}
