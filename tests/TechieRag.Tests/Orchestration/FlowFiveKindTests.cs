using TechieRag.Models;
using TechieRag.Orchestration;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// REQ-RAG-042 / BRD-130 acceptance in one flow: all five node kinds (Agent, Condition, Tool,
/// Handoff, Terminal), a <see cref="FlowCondition"/> that routes on the data, a handoff, and the
/// <see cref="FlowDefinition.MaxSteps"/> bound.
/// </summary>
public sealed class FlowFiveKindTests
{
    /// <summary>
    /// When the classifier's answer satisfies the condition, the run takes the refund branch: the
    /// escalation tool runs, the handoff passes control to the closer agent, and the terminal node
    /// ends the run. The other branch's tool never runs.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-042 AFiveKindFlowRoutesOnTheDataThroughTheHandoff")]
    public async Task AFiveKindFlowRoutesOnTheDataThroughTheHandoff()
    {
        var (runtime, tools, closer) = Runtime("refund please");

        var result = await new FlowRunner(FiveKindFlow(), runtime).RunAsync("I want my money back");

        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.Equal(new[] { "classify", "branch", "escalate", "handoff", "closer", "end" }, result.VisitedNodeIds);
        Assert.Equal(new[] { "escalate" }, tools.Executed);
        Assert.Equal(1, closer.TurnCount);
        Assert.Contains(result.Steps, step => step.Kind == AgentStepKind.HandoffPerformed && step.ToNodeId == "closer");
        Assert.Equal(
            Enum.GetValues<FlowNodeKind>().OrderBy(kind => kind),
            FiveKindFlow().Nodes.Select(node => node.Kind).Distinct().OrderBy(kind => kind));
    }

    /// <summary>
    /// The same flow with an answer that does not satisfy the condition takes the default branch:
    /// the routing follows the data, not the graph's declaration order.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-042 AFiveKindFlowTakesTheDefaultBranchWhenTheConditionFails")]
    public async Task AFiveKindFlowTakesTheDefaultBranchWhenTheConditionFails()
    {
        var (runtime, tools, closer) = Runtime("just a question");

        var result = await new FlowRunner(FiveKindFlow(), runtime).RunAsync("what are your hours?");

        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.Equal(new[] { "classify", "branch", "answer", "end" }, result.VisitedNodeIds);
        Assert.Equal(new[] { "faq" }, tools.Executed);
        Assert.Equal(0, closer.TurnCount);
    }

    /// <summary>
    /// With <see cref="FlowDefinition.MaxSteps"/> set below the path length, the run stops at the
    /// budget with <see cref="FlowRunOutcome.StepBudgetExhausted"/> and the node it would have run next.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-042 AFiveKindFlowStopsAtMaxSteps")]
    public async Task AFiveKindFlowStopsAtMaxSteps()
    {
        var (runtime, tools, closer) = Runtime("refund please");
        var flow = FiveKindFlow();
        flow.MaxSteps = 3;

        var result = await new FlowRunner(flow, runtime).RunAsync("I want my money back");

        Assert.Equal(FlowRunOutcome.StepBudgetExhausted, result.Outcome);
        Assert.Equal(3, result.StepsExecuted);
        Assert.Equal("handoff", result.LastNodeId);
        Assert.Equal(0, closer.TurnCount);
        Assert.Equal(new[] { "escalate" }, tools.Executed);
    }

    private static (FlowRuntime Runtime, RecordingToolHandler Tools, ScriptedLlmProvider Closer) Runtime(string classification)
    {
        var classifier = new ScriptedLlmProvider("classifier", ScriptedLlmProvider.Says(classification));
        var closer = new ScriptedLlmProvider("closer", ScriptedLlmProvider.Says("refund issued"));
        var tools = new RecordingToolHandler()
            .Register("escalate", "Raises a refund ticket", _ => "ticket 42")
            .Register("faq", "Answers from the FAQ", _ => "we open at nine");

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(
            new FlowAgent("classifier", classifier),
            new FlowAgent("closer", closer)))
        {
            Tools = tools
        };

        return (runtime, tools, closer);
    }

    private static FlowDefinition FiveKindFlow() => new()
    {
        Id = "five-kinds",
        Name = "Support with refund handoff",
        StartNodeId = "classify",
        Nodes =
        [
            new FlowNode { Id = "classify", Kind = FlowNodeKind.Agent, AgentId = "classifier" },
            new FlowNode { Id = "branch", Kind = FlowNodeKind.Condition, Name = "Refund?" },
            new FlowNode { Id = "escalate", Kind = FlowNodeKind.Tool, ToolName = "escalate" },
            new FlowNode { Id = "answer", Kind = FlowNodeKind.Tool, ToolName = "faq" },
            new FlowNode
            {
                Id = "handoff",
                Kind = FlowNodeKind.Handoff,
                Name = "Transfer to closer",
                Handoff = new FlowHandoff { TargetNodeId = "closer", Reason = "a refund was requested" }
            },
            new FlowNode { Id = "closer", Kind = FlowNodeKind.Agent, AgentId = "closer" },
            new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
        ],
        Edges =
        [
            new FlowEdge { Id = "e0", FromNodeId = "classify", ToNodeId = "branch" },
            new FlowEdge
            {
                Id = "e1", FromNodeId = "branch", ToNodeId = "escalate", Order = 0, Label = "refund",
                Condition = new FlowCondition { Kind = FlowConditionKind.Contains, Operand = "refund" }
            },
            new FlowEdge { Id = "e2", FromNodeId = "branch", ToNodeId = "answer", Order = 1, Label = "everything else" },
            new FlowEdge { Id = "e3", FromNodeId = "escalate", ToNodeId = "handoff" },
            new FlowEdge { Id = "e4", FromNodeId = "closer", ToNodeId = "end" },
            new FlowEdge { Id = "e5", FromNodeId = "answer", ToNodeId = "end" }
        ]
    };
}
