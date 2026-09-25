using TechieRag.Abstractions;
using TechieRag.Agents.Tests.TestDoubles;
using TechieRag.Models;
using TechieRag.Orchestration;
using Xunit;

namespace TechieRag.Agents.Tests.Interop;

/// <summary>
/// Tests for adapter 3, <see cref="Agents.Interop.AgentStepReporter"/>: Agent Framework middleware
/// reported as TechieRag <see cref="AgentStep"/>s (REQ-RAG-017 / BRD-85).
/// </summary>
public class AgentStepReporterTests
{
    /// <summary>A tool-using run is traced with the existing loop kinds, in order, with 1-based iterations.</summary>
    [Fact]
    public async Task TraceReportsLoopKindsInOrder()
    {
        var steps = new List<AgentStep>();
        var model = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("c1", "search_knowledge_base", new() { ["query"] = "refund" }),
            ScriptedChatClient.Answer("30 days [S1]."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create()).UseCustomChatClient(() => model).WithTrace(new SyncProgress(steps.Add)).Build();

        await agent.AskAsync("Return window?");

        Assert.Equal([AgentStepKind.ToolCallRequested, AgentStepKind.ToolExecuted, AgentStepKind.FinalAnswer], steps.Select(s => s.Kind));
        Assert.Equal("search_knowledge_base", steps[1].ToolName);
        Assert.Contains("\"status\":\"strong\"", steps[1].Content);
        Assert.Equal([1, 1, 2], steps.Select(s => s.Iteration));
        Assert.Equal("30 days [S1].", steps[2].Content);
    }

    /// <summary>
    /// A handler's coded refusal reaches the trace: success flag, English error and the FlowMessage code
    /// all flow from the <see cref="ToolResult"/> into the step, as in the classic loop.
    /// </summary>
    [Fact]
    public async Task TraceCarriesHandlerFailureCode()
    {
        var steps = new List<AgentStep>();
        var model = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("c1", "web_search", new() { ["q"] = "x" }),
            ScriptedChatClient.Answer("I could not search the web."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create())
            .UseCustomChatClient(() => model)
            .WithToolHandler(new RefusingHandler())
            .WithTrace(new SyncProgress(steps.Add))
            .Build();

        await agent.AskAsync("Search the web");

        var executed = Assert.Single(steps, s => s.Kind == AgentStepKind.ToolExecuted);
        Assert.False(executed.IsSuccess);
        Assert.Equal("Egress declined", executed.ErrorMessage);
        Assert.Equal("EgressDeclined", executed.FailureMessage!.Code);
    }

    /// <summary>A run that hits the iteration cap ends in MaxIterationsReached, not FinalAnswer.</summary>
    [Fact]
    public async Task TraceReportsIterationCap()
    {
        var steps = new List<AgentStep>();
        var model = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("c1", "list_documents", new()),
            ScriptedChatClient.Answer("Stopped."),
            ScriptedChatClient.Answer("Stopped."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create())
            .UseCustomChatClient(() => model)
            .WithMaxToolIterations(1)
            .WithTrace(new SyncProgress(steps.Add))
            .Build();

        await agent.AskAsync("loop");

        Assert.Equal(AgentStepKind.MaxIterationsReached, steps[^1].Kind);
    }

    private sealed class RefusingHandler : IToolHandler
    {
        public IReadOnlyList<ToolDefinition> ToolDefinitions { get; } =
            [new ToolDefinition { Name = "web_search", Description = "Searches the web.", ParametersSchema = """{"type":"object","properties":{"q":{"type":"string"}}}""" }];

        public Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = "The user declined this web request.",
                IsSuccess = false,
                ErrorMessage = "Egress declined",
                Message = FlowMessage.Create("EgressDeclined", "Egress declined")
            });
    }

    private sealed class SyncProgress(Action<AgentStep> report) : IProgress<AgentStep>
    {
        public void Report(AgentStep value) => report(value);
    }
}
