using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Orchestration;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// Proves an agent exposed as a tool is indistinguishable from any other tool (REQ-RAG-042 /
/// BRD-123) — it goes through <see cref="IToolHandler"/>, composes with
/// <see cref="CompositeToolHandler"/>, and the existing agent loop dispatches to it unchanged.
/// </summary>
public sealed class AgentAsToolTests
{
    /// <summary>
    /// A calling agent asks for a sub-agent by tool name; the sub-agent runs its own turn and its
    /// answer comes back to the caller as an ordinary tool result.
    /// </summary>
    [Fact]
    public async Task AnAgentExposedAsAToolRoundTripsThroughTheToolHandler()
    {
        var specialist = new ScriptedLlmProvider("specialist", ScriptedLlmProvider.Says("42 is the answer"));
        var subAgent = new FlowAgent("specialist", specialist) { SystemPrompt = "You do arithmetic." };

        var handler = AgentToolHandler.ForAgent("ask-specialist", "Delegates a maths question", subAgent);

        var call = new ToolCall
        {
            Id = "call-1",
            Name = "ask-specialist",
            ArgumentsJson = """{"input":"what is six times seven"}"""
        };

        var result = await handler.ExecuteToolAsync(call);

        Assert.True(result.IsSuccess);
        Assert.Equal("42 is the answer", result.Content);
        Assert.Equal("call-1", result.ToolCallId);

        // The sub-agent really ran, with its OWN system prompt and only the supplied input.
        var conversation = Assert.Single(specialist.Conversations);
        Assert.Equal(2, conversation.Count);
        Assert.Equal("system", conversation[0].Role);
        Assert.Equal("You do arithmetic.", conversation[0].Content);
        Assert.Equal("what is six times seven", conversation[1].Content);
    }

    /// <summary>
    /// Composed with local tools on a <see cref="CompositeToolHandler"/>, the real agent loop calls
    /// the sub-agent exactly as it calls a delegate tool — nothing in the loop knows the difference.
    /// </summary>
    [Fact]
    public async Task TheAgentLoopDispatchesToASubAgentComposedAlongsideLocalTools()
    {
        var specialist = new ScriptedLlmProvider("specialist", ScriptedLlmProvider.Says("the sub-agent's finding"));
        var subAgent = AgentToolHandler.ForAgent(
            "ask-specialist", "Delegates to the specialist", new FlowAgent("specialist", specialist));

        var localTools = new RecordingToolHandler().Register("clock", "Tells the time", _ => "12:00");
        var composed = new CompositeToolHandler(localTools, subAgent);

        Assert.Equal(
            new[] { "clock", "ask-specialist" },
            composed.ToolDefinitions.Select(definition => definition.Name).ToArray());

        var caller = new ScriptedLlmProvider(
            "caller",
            ScriptedLlmProvider.CallsTool("ask-specialist", """{"input":"look into this"}"""),
            ScriptedLlmProvider.Says("done"));

        var loop = new AgentLoopRunner(caller, composed);
        var messages = new List<ChatMessage> { ChatMessage.User("investigate") };
        var response = await loop.RunAsync(messages);

        Assert.Equal("done", response.Content);
        Assert.Equal(1, specialist.TurnCount);

        // The sub-agent's answer came back through the loop's ordinary tool-result channel.
        var toolMessage = Assert.Single(messages, message => message.Role == "tool");
        Assert.Equal("the sub-agent's finding", toolMessage.Content);
    }

    /// <summary>
    /// A sub-agent does not inherit the caller's conversation. It sees its own prompt and the input
    /// the caller chose to pass, so the cost of delegating is visible at the call site.
    /// </summary>
    [Fact]
    public async Task ASubAgentNeverSeesTheCallersConversation()
    {
        var specialist = new ScriptedLlmProvider("specialist", ScriptedLlmProvider.Says("noted"));
        var subAgent = AgentToolHandler.ForAgent(
            "ask-specialist", "Delegates to the specialist", new FlowAgent("specialist", specialist));

        var caller = new ScriptedLlmProvider(
            "caller",
            ScriptedLlmProvider.CallsTool("ask-specialist", """{"input":"just this bit"}"""),
            ScriptedLlmProvider.Says("done"));

        await new AgentLoopRunner(caller, subAgent).RunAsync(
        [
            ChatMessage.System("CALLERSYSTEMPROMPT you are the coordinator"),
            ChatMessage.User("CALLERSECRET the customer's full history")
        ]);

        Assert.DoesNotContain("CALLERSYSTEMPROMPT", specialist.AllSeenText);
        Assert.DoesNotContain("CALLERSECRET", specialist.AllSeenText);
        Assert.Contains("just this bit", specialist.AllSeenText);
    }

    /// <summary>
    /// Two agents wired as each other's tools terminate: the invocation ceiling stops the recursion
    /// and reports it to the model rather than throwing or hanging.
    /// </summary>
    [Fact]
    public async Task TheInvocationCeilingBoundsAgentAsToolRecursion()
    {
        var specialist = new ScriptedLlmProvider(
            "specialist",
            Enumerable.Range(0, 10).Select(index => ScriptedLlmProvider.Says($"answer {index}")).ToArray());

        var handler = AgentToolHandler.ForAgent(
            "ask-specialist", "Delegates", new FlowAgent("specialist", specialist), maxInvocations: 2);

        var results = new List<ToolResult>();
        for (var index = 0; index < 4; index++)
        {
            results.Add(await handler.ExecuteToolAsync(new ToolCall
            {
                Id = $"call-{index}",
                Name = "ask-specialist",
                ArgumentsJson = """{"input":"again"}"""
            }));
        }

        Assert.True(results[0].IsSuccess);
        Assert.True(results[1].IsSuccess);
        Assert.False(results[2].IsSuccess);
        Assert.False(results[3].IsSuccess);
        Assert.Contains("will not run again", results[2].Content);

        // The sub-agent really stopped being invoked; the ceiling is not just a different message.
        Assert.Equal(2, specialist.TurnCount);
    }

    /// <summary>A whole flow can be exposed as one tool, so an agent can delegate to a graph.</summary>
    [Fact]
    public async Task AWholeFlowCanBeExposedAsOneTool()
    {
        var inner = new ScriptedLlmProvider("inner", ScriptedLlmProvider.Says("the sub-flow's conclusion"));

        var subFlow = new FlowDefinition
        {
            Id = "research",
            Name = "Research",
            StartNodeId = "study",
            Nodes =
            [
                new FlowNode { Id = "study", Kind = FlowNodeKind.Agent, AgentId = "inner" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
            ],
            Edges = [new FlowEdge { Id = "e", FromNodeId = "study", ToNodeId = "end" }]
        };

        var subRuntime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("inner", inner)));
        var handler = AgentToolHandler.ForFlow("run-research", "Runs the research flow", subFlow, subRuntime);

        var result = await handler.ExecuteToolAsync(new ToolCall
        {
            Id = "call-1",
            Name = "run-research",
            ArgumentsJson = """{"input":"look into widgets"}"""
        });

        Assert.True(result.IsSuccess);
        Assert.Equal("the sub-flow's conclusion", result.Content);
    }

    /// <summary>
    /// A sub-flow that a guardrail stopped comes back as an UNSUCCESSFUL result saying so, not as a
    /// plausible answer the calling agent would repeat as a finding.
    /// </summary>
    [Fact]
    public async Task ABlockedSubFlowIsReportedAsUnavailableRatherThanAsAnAnswer()
    {
        var inner = new ScriptedLlmProvider("inner", ScriptedLlmProvider.Says("never reached"));

        var subFlow = new FlowDefinition
        {
            Id = "research",
            Name = "Research",
            StartNodeId = "study",
            Nodes =
            [
                new FlowNode { Id = "study", Kind = FlowNodeKind.Agent, AgentId = "inner" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
            ],
            Edges = [new FlowEdge { Id = "e", FromNodeId = "study", ToNodeId = "end" }]
        };

        var subRuntime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("inner", inner)));
        subRuntime.HostGuardrails.Add(new DelegateFlowGuardrail(
            "host-gate", "Refuses everything", [GuardrailStage.Input],
            (_, _) => Task.FromResult(GuardrailDecision.Block("this workspace does not allow research"))));

        var handler = AgentToolHandler.ForFlow("run-research", "Runs the research flow", subFlow, subRuntime);

        var result = await handler.ExecuteToolAsync(new ToolCall
        {
            Id = "call-1",
            Name = "run-research",
            ArgumentsJson = """{"input":"look into widgets"}"""
        });

        Assert.StartsWith("unavailable:", result.Content);
        Assert.Contains("host-gate", result.Content);
        Assert.Equal(0, inner.TurnCount);
    }

    /// <summary>A malformed call is a readable tool result, not an exception that ends the turn.</summary>
    [Fact]
    public async Task AMalformedCallComesBackAsAToolResult()
    {
        var handler = AgentToolHandler.ForAgent(
            "ask-specialist", "Delegates",
            new FlowAgent("specialist", new ScriptedLlmProvider("specialist")));

        var result = await handler.ExecuteToolAsync(new ToolCall
        {
            Id = "call-1",
            Name = "ask-specialist",
            ArgumentsJson = "not json at all"
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("input", result.Content);
    }

    /// <summary>The advertised schema is the single-string contract the calling model needs.</summary>
    [Fact]
    public void TheAdvertisedSchemaTakesOneStringInput()
    {
        var handler = AgentToolHandler.ForAgent(
            "ask-specialist", "Delegates",
            new FlowAgent("specialist", new ScriptedLlmProvider("specialist")));

        var definition = Assert.Single(handler.ToolDefinitions);
        Assert.Equal("ask-specialist", definition.Name);
        Assert.Contains("\"input\"", definition.ParametersSchema);
        Assert.Contains("\"required\":[\"input\"]", definition.ParametersSchema);
    }
}
