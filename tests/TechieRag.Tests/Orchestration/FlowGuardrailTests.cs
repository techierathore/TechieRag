using TechieRag.Models;
using TechieRag.Orchestration;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// Proves guardrails actually stop work rather than merely disapproving of it (REQ-RAG-042 /
/// BRD-123), including the seam TechieDesk's <c>EgressGate</c> plugs into (REQ-NFR-013).
/// </summary>
/// <remarks>
/// Every block assertion here is paired with evidence that the guarded thing did NOT run — the
/// scripted model was never asked, or the tool handler recorded no execution. "The result says
/// blocked" is a claim about a field; "the tool never executed" is a claim about the world.
/// </remarks>
public sealed class FlowGuardrailTests
{
    /// <summary>An input guardrail stops the run before the node's model is ever asked.</summary>
    [Fact]
    public async Task AnInputGuardrailBlocksTheNodeAndStopsTheRun()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("should never be reached"));
        var guardrail = Blocking("no-secrets", "Refuses payloads containing a secret", GuardrailStage.Input);

        var result = await RunGuardedFlow(provider, guardrail, guardrailIds: ["no-secrets"]);

        Assert.Equal(FlowRunOutcome.Blocked, result.Outcome);
        Assert.Equal("no-secrets", result.BlockedByGuardrailId);
        Assert.Contains("refused by the test guardrail", result.BlockReason);

        // The model was never asked. A "blocked" flag on a node that already ran is not a block.
        Assert.Equal(0, provider.TurnCount);

        var blocked = Assert.Single(result.Steps, step => step.Kind == AgentStepKind.GuardrailBlocked);
        Assert.Equal(GuardrailStage.Input, blocked.GuardrailStage);
        Assert.Equal("work", blocked.NodeId);
        Assert.False(blocked.IsSuccess);
    }

    /// <summary>An output guardrail lets the node run and then refuses what it produced.</summary>
    [Fact]
    public async Task AnOutputGuardrailBlocksAfterTheNodeRan()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("the answer"));
        var guardrail = Blocking("no-answers", "Refuses this node's output", GuardrailStage.Output);

        var result = await RunGuardedFlow(provider, guardrail, guardrailIds: ["no-answers"]);

        Assert.Equal(FlowRunOutcome.Blocked, result.Outcome);
        Assert.Equal(1, provider.TurnCount);
        Assert.Equal(
            GuardrailStage.Output,
            result.Steps.Single(step => step.Kind == AgentStepKind.GuardrailBlocked).GuardrailStage);
    }

    /// <summary>
    /// A guardrail that throws denies. A check that cannot run is not a check that passed, and a
    /// broken guardrail must never be the cheapest way past it.
    /// </summary>
    [Fact]
    public async Task AGuardrailThatThrowsDenies()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("should never be reached"));
        var guardrail = new DelegateFlowGuardrail(
            "faulty", "Throws on every call", null,
            (_, _) => throw new InvalidOperationException("the policy service is down"));

        var result = await RunGuardedFlow(provider, guardrail, guardrailIds: ["faulty"]);

        Assert.Equal(FlowRunOutcome.Blocked, result.Outcome);
        Assert.Equal("faulty", result.BlockedByGuardrailId);
        Assert.Contains("the policy service is down", result.BlockReason);
        Assert.Equal(0, provider.TurnCount);
    }

    /// <summary>A guardrail the host cannot resolve denies, naming the id that could not be loaded.</summary>
    [Fact]
    public async Task AnUnresolvableGuardrailDenies()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("should never be reached"));

        // The resolver exists and knows a DIFFERENT guardrail, so this is a genuine lookup miss
        // rather than the resolver being absent.
        var result = await RunGuardedFlow(
            provider,
            Blocking("some-other-check", "An unrelated check", GuardrailStage.Input),
            guardrailIds: ["compliance-check"]);

        Assert.Equal(FlowRunOutcome.Blocked, result.Outcome);
        Assert.Equal("compliance-check", result.BlockedByGuardrailId);
        Assert.Contains("could not be loaded", result.BlockReason);
        Assert.Equal(0, provider.TurnCount);
    }

    /// <summary>A node naming a guardrail on a host with no resolver at all denies.</summary>
    [Fact]
    public async Task ANodeNamingAGuardrailWithNoResolverConfiguredDenies()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("should never be reached"));

        var flow = GuardedFlow(["compliance-check"]);
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("agent", provider)));

        var result = await new FlowRunner(flow, runtime).RunAsync("hello");

        Assert.Equal(FlowRunOutcome.Blocked, result.Outcome);
        Assert.Contains("no guardrail resolver", result.BlockReason);
        Assert.Equal(0, provider.TurnCount);
    }

    /// <summary>
    /// The seam that matters: a HOST guardrail applies to a node that names none, and cannot be
    /// removed by editing the flow. This is how <c>EgressGate</c> stays unavoidable.
    /// </summary>
    [Fact]
    public async Task AHostGuardrailAppliesToANodeThatNamesNoGuardrails()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("should never be reached"));

        var flow = GuardedFlow([]);
        Assert.Empty(flow.Nodes.Single(node => node.Id == "work").GuardrailIds);

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("agent", provider)));
        runtime.HostGuardrails.Add(Blocking("host-egress-gate", "The host's own gate", GuardrailStage.Input));

        var result = await new FlowRunner(flow, runtime).RunAsync("hello");

        Assert.Equal(FlowRunOutcome.Blocked, result.Outcome);
        Assert.Equal("host-egress-gate", result.BlockedByGuardrailId);
        Assert.Equal(0, provider.TurnCount);
    }

    /// <summary>
    /// The egress-gate shape, end to end: a host guardrail refuses one tool at the
    /// <see cref="GuardrailStage.ToolCall"/> stage. The tool never executes, the block is traced,
    /// and the AGENT still finishes its turn — the same "unavailable, carry on" contract the app's
    /// gate already keeps.
    /// </summary>
    [Fact]
    public async Task AHostToolCallGuardrailStopsTheCallAndLetsTheAgentCarryOn()
    {
        var provider = new ScriptedLlmProvider(
            "agent",
            ScriptedLlmProvider.CallsTool("web-search", """{"query":"anything"}"""),
            ScriptedLlmProvider.Says("I could not search, so here is what I know locally."));

        var tools = new RecordingToolHandler().Register("web-search", "Searches the web", _ => "REMOTE RESULT");

        var flow = GuardedFlow([]);
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("agent", provider, tools)));
        runtime.HostGuardrails.Add(new DelegateFlowGuardrail(
            "egress-gate", "Asks before anything leaves this machine", [GuardrailStage.ToolCall],
            (context, _) => Task.FromResult(
                context.ToolName == "web-search"
                    ? GuardrailDecision.Block("'web-search' sends a request off this machine and approval was declined.")
                    : GuardrailDecision.Allow())));

        var result = await new FlowRunner(flow, runtime).RunAsync("what is the weather");

        // The call did not happen.
        Assert.Empty(tools.Executed);

        // The run did not stop, and the model got a readable refusal rather than an exception.
        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.Contains("could not search", result.Output);

        var blocked = Assert.Single(result.Steps, step => step.Kind == AgentStepKind.GuardrailBlocked);
        Assert.Equal(GuardrailStage.ToolCall, blocked.GuardrailStage);
        Assert.Equal("web-search", blocked.ToolName);
        Assert.Equal("egress-gate", blocked.GuardrailId);
    }

    /// <summary>
    /// The tool-call gate covers a deterministic tool node too, so a flow cannot reach a gated tool
    /// by dropping a Tool node on the canvas instead of asking an agent to call it.
    /// </summary>
    [Fact]
    public async Task AHostToolCallGuardrailAlsoCoversADeterministicToolNode()
    {
        var tools = new RecordingToolHandler().Register("web-search", "Searches the web", _ => "REMOTE RESULT");

        var flow = new FlowDefinition
        {
            Id = "direct",
            Name = "Direct tool call",
            StartNodeId = "call",
            Nodes =
            [
                new FlowNode { Id = "call", Kind = FlowNodeKind.Tool, ToolName = "web-search" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
            ],
            Edges = [new FlowEdge { Id = "e", FromNodeId = "call", ToNodeId = "end" }]
        };

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver()) { Tools = tools };
        runtime.HostGuardrails.Add(new DelegateFlowGuardrail(
            "egress-gate", "Asks before anything leaves this machine", [GuardrailStage.ToolCall],
            (_, _) => Task.FromResult(GuardrailDecision.Block("approval was declined"))));

        var result = await new FlowRunner(flow, runtime).RunAsync("go");

        Assert.Empty(tools.Executed);
        Assert.DoesNotContain("REMOTE RESULT", result.Output);
        Assert.Contains(result.Steps, step => step.Kind == AgentStepKind.GuardrailBlocked && step.ToolName == "web-search");
    }

    /// <summary>A guardrail that allows changes nothing: the node runs and the flow completes.</summary>
    [Fact]
    public async Task AnAllowingGuardrailLetsTheNodeRun()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("the answer"));
        var guardrail = new DelegateFlowGuardrail(
            "permissive", "Allows everything", null, (_, _) => Task.FromResult(GuardrailDecision.Allow()));

        var result = await RunGuardedFlow(provider, guardrail, guardrailIds: ["permissive"]);

        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.Equal("the answer", result.Output);
        Assert.DoesNotContain(result.Steps, step => step.Kind == AgentStepKind.GuardrailBlocked);
    }

    /// <summary>Host guardrails run before the flow's own, so a flow check cannot pre-empt a host one.</summary>
    [Fact]
    public async Task HostGuardrailsAreEvaluatedBeforeTheFlowsOwn()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("unreachable"));

        var flow = GuardedFlow(["flow-check"]);
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("agent", provider)))
        {
            Guardrails = new InMemoryFlowGuardrailResolver(
                Blocking("flow-check", "The flow author's check", GuardrailStage.Input))
        };
        runtime.HostGuardrails.Add(Blocking("host-check", "The host's check", GuardrailStage.Input));

        var result = await new FlowRunner(flow, runtime).RunAsync("hello");

        Assert.Equal("host-check", result.BlockedByGuardrailId);
    }

    /// <summary>Builds a guardrail that always refuses, at one stage.</summary>
    /// <param name="id">The guardrail id.</param>
    /// <param name="description">Its description.</param>
    /// <param name="stage">The stage it watches.</param>
    /// <returns>The guardrail.</returns>
    private static IFlowGuardrail Blocking(string id, string description, GuardrailStage stage) =>
        new DelegateFlowGuardrail(id, description, [stage],
            (_, _) => Task.FromResult(GuardrailDecision.Block("refused by the test guardrail")));

    /// <summary>Builds the one-agent flow the guardrail tests share.</summary>
    /// <param name="guardrailIds">The guardrails the node names.</param>
    /// <returns>The flow.</returns>
    private static FlowDefinition GuardedFlow(string[] guardrailIds) => new()
    {
        Id = "guarded",
        Name = "Guarded flow",
        StartNodeId = "work",
        Nodes =
        [
            new FlowNode
            {
                Id = "work",
                Kind = FlowNodeKind.Agent,
                Name = "Work",
                AgentId = "agent",
                GuardrailIds = [.. guardrailIds]
            },
            new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
        ],
        Edges = [new FlowEdge { Id = "e", FromNodeId = "work", ToNodeId = "end" }]
    };

    /// <summary>Runs the shared flow with one resolvable guardrail.</summary>
    /// <param name="provider">The agent's scripted model.</param>
    /// <param name="guardrail">The guardrail the resolver knows about.</param>
    /// <param name="guardrailIds">The ids the node names, which may not match.</param>
    /// <returns>The run result.</returns>
    private static Task<FlowRunResult> RunGuardedFlow(
        ScriptedLlmProvider provider, IFlowGuardrail guardrail, string[] guardrailIds)
    {
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("agent", provider)))
        {
            Guardrails = new InMemoryFlowGuardrailResolver(guardrail)
        };

        return new FlowRunner(GuardedFlow(guardrailIds), runtime).RunAsync("hello");
    }
}
