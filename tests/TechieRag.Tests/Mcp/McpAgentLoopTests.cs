using TechieRag.Mcp;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Mcp;

/// <summary>
/// Unit tests proving the agent loop drives MCP tools with no MCP-specific code path
/// (REQ-RAG-038): <c>AgentLoopRunner</c> is handed an <c>IToolHandler</c> that happens to be
/// MCP-backed and behaves exactly as it does with a local tool registry.
/// </summary>
public class McpAgentLoopTests
{
    private const string SearchToolJson = """
    {"tools":[{"name":"search","description":"Searches","inputSchema":{"type":"object","properties":{"q":{"type":"string"}}}}]}
    """;

    /// <summary>The agent loop offers MCP tools to the model, runs the call, and feeds the result back.</summary>
    [Fact]
    public async Task AgentLoopCallsMcpToolAndFeedsResultBack()
    {
        var transport = new FakeMcpTransport("docs", SearchToolJson,
            (_, _) => """{"content":[{"type":"text","text":"the answer is 42"}]}""");
        await using var handler = await McpToolHandler.CreateAsync(
            [new McpClient(transport, Config())], McpTrustPolicy.Strict);

        var llm = new ScriptedToolCallingLlmProvider("docs-search", """{"q":"meaning"}""");
        var runner = new AgentLoopRunner(llm, handler);

        var response = await runner.RunAsync([ChatMessage.User("what is the answer?")]);

        Assert.Equal("done", response.Content);
        Assert.Contains(llm.OfferedTools!, definition => definition.Name == "docs-search");
        Assert.Contains(llm.ObservedToolMessages, message => message.Content == "the answer is 42");
    }

    /// <summary>Each agent step is reported, so an MCP tool call is as visible in a trace as any other.</summary>
    [Fact]
    public async Task AgentLoopReportsMcpToolExecutionAsAStep()
    {
        var transport = new FakeMcpTransport("docs", SearchToolJson,
            (_, _) => """{"content":[{"type":"text","text":"ok"}]}""");
        await using var handler = await McpToolHandler.CreateAsync(
            [new McpClient(transport, Config())], McpTrustPolicy.Strict);

        var progress = new SynchronousProgress();
        var runner = new AgentLoopRunner(new ScriptedToolCallingLlmProvider("docs-search"), handler);

        await runner.RunAsync([ChatMessage.User("go")], progress: progress);

        var executed = Assert.Single(progress.Steps.Where(step => step.Kind == AgentStepKind.ToolExecuted));
        Assert.Equal("docs-search", executed.ToolName);
        Assert.True(executed.IsSuccess);
    }

    /// <summary>A failing MCP tool keeps the loop running: the model sees the error and finishes.</summary>
    [Fact]
    public async Task FailingMcpToolDoesNotAbortTheAgentLoop()
    {
        var transport = new FakeMcpTransport("docs", SearchToolJson,
            (_, _) => throw new McpException("docs", "upstream exploded"));
        await using var handler = await McpToolHandler.CreateAsync(
            [new McpClient(transport, Config())], McpTrustPolicy.Strict);

        var llm = new ScriptedToolCallingLlmProvider("docs-search");
        var runner = new AgentLoopRunner(llm, handler);

        var response = await runner.RunAsync([ChatMessage.User("go")]);

        Assert.Equal("done", response.Content);
        Assert.Contains(llm.ObservedToolMessages, message => message.Content!.Contains("upstream exploded"));
    }

    private static McpServerConfig Config() => new()
    {
        Name = "docs",
        Transport = McpTransportKind.Http,
        Endpoint = "https://mcp.example.com"
    };

    /// <summary>
    /// Progress sink that records on the calling thread, so a test can assert immediately.
    /// </summary>
    /// <remarks><c>Progress&lt;T&gt;</c> posts to the thread pool when there is no synchronisation
    /// context, which would make this assertion a race.</remarks>
    private sealed class SynchronousProgress : IProgress<AgentStep>
    {
        /// <summary>Gets every step reported, in order.</summary>
        public List<AgentStep> Steps { get; } = [];

        /// <inheritdoc/>
        public void Report(AgentStep value) => Steps.Add(value);
    }
}
