using TechieRag.Models;
using TechieRag.Orchestration;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// Drives real multi-step graphs end to end through <see cref="FlowRunner"/> (REQ-RAG-042 /
/// BRD-123): the routing, the tool dispatch and the agent loop are all the shipping code paths, and
/// only the model's answers are scripted so the assertions can be exact.
/// </summary>
public sealed class FlowGraphExecutionTests
{
    /// <summary>
    /// A three-node graph — triage agent, tool, terminal — runs in order and produces a trace whose
    /// steps name each node, in the order they executed, ending in a flow completion.
    /// </summary>
    [Fact]
    public async Task MultiStepGraphRunsEveryNodeAndTracesThemInOrder()
    {
        var triage = new ScriptedLlmProvider("triage", ScriptedLlmProvider.Says("billing question"));
        var tools = new RecordingToolHandler().Register("lookup", "Looks an account up", args => $"account for {args}");

        var flow = new FlowDefinition
        {
            Id = "support",
            Name = "Support triage",
            StartNodeId = "classify",
            Nodes =
            [
                new FlowNode { Id = "classify", Kind = FlowNodeKind.Agent, Name = "Classify", AgentId = "triage" },
                new FlowNode { Id = "lookup", Kind = FlowNodeKind.Tool, Name = "Look up", ToolName = "lookup" },
                new FlowNode { Id = "done", Kind = FlowNodeKind.Terminal, Name = "Done", TerminalStatus = "resolved" }
            ],
            Edges =
            [
                new FlowEdge { Id = "e1", FromNodeId = "classify", ToNodeId = "lookup" },
                new FlowEdge { Id = "e2", FromNodeId = "lookup", ToNodeId = "done" }
            ]
        };

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("triage", triage))) { Tools = tools };
        var progress = new RecordingProgress();

        var result = await new FlowRunner(flow, runtime).RunAsync("my invoice is wrong", null, progress);

        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.Equal(new[] { "classify", "lookup", "done" }, result.VisitedNodeIds);
        Assert.Equal("resolved", result.TerminalStatus);
        Assert.Equal(3, result.StepsExecuted);

        // The tool really ran, through the real handler, with the previous node's output.
        Assert.Equal(new[] { "lookup" }, tools.Executed);
        Assert.Contains("billing question", tools.ExecutedArguments.Single());

        // The trace is the live progress channel, not a reconstruction.
        Assert.Equal(result.Steps.Count, progress.Steps.Count);
        Assert.Equal(
            new[] { "classify", "classify", "classify", "classify", "lookup", "lookup", "lookup", "lookup", "done", "done", "done" },
            progress.Steps.Cast<FlowStep>().Select(step => step.NodeId).ToArray());
        Assert.Equal(AgentStepKind.FlowCompleted, progress.Steps[^1].Kind);
    }

    /// <summary>
    /// The same graph routes to the escalation branch or the automatic branch purely on what the
    /// classifier said: flipping the model's one word flips the path, and nothing else changes.
    /// </summary>
    [Theory]
    [InlineData("refund", "escalate", "human")]
    [InlineData("password reset", "auto", "robot")]
    public async Task ConditionalRoutingFollowsTheBranchTheOutputSatisfies(
        string classification, string expectedNodeId, string expectedOutput)
    {
        var classifier = new ScriptedLlmProvider("classifier", ScriptedLlmProvider.Says(classification));
        var flow = BranchingFlow();

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("classifier", classifier)))
        {
            Tools = new RecordingToolHandler()
                .Register("escalate", "Raises a ticket", _ => "human")
                .Register("autoresolve", "Resolves automatically", _ => "robot")
        };

        var result = await new FlowRunner(flow, runtime).RunAsync("help me");

        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.Contains(expectedNodeId, result.VisitedNodeIds);
        Assert.Equal(expectedOutput, result.Output);

        var route = result.Steps.First(step => step.Kind == AgentStepKind.RouteTaken && step.FromNodeId == "branch");
        Assert.Equal(expectedNodeId, route.ToNodeId);
    }

    /// <summary>
    /// The branch NOT taken is genuinely not taken: the other tool never executes, so the routing
    /// assertion above is about control flow rather than about which result got returned.
    /// </summary>
    [Fact]
    public async Task TheUntakenBranchNeverExecutes()
    {
        var classifier = new ScriptedLlmProvider("classifier", ScriptedLlmProvider.Says("refund please"));
        var tools = new RecordingToolHandler()
            .Register("escalate", "Raises a ticket", _ => "human")
            .Register("autoresolve", "Resolves automatically", _ => "robot");

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("classifier", classifier)))
        {
            Tools = tools
        };

        await new FlowRunner(BranchingFlow(), runtime).RunAsync("help me");

        Assert.Equal(new[] { "escalate" }, tools.Executed);
        Assert.DoesNotContain("autoresolve", tools.Executed);
    }

    /// <summary>
    /// A deliberate loop terminates at the step budget instead of running forever, reports the
    /// exhaustion in the trace, and returns rather than throwing.
    /// </summary>
    [Fact]
    public async Task ACycleStopsAtTheStepBudgetRatherThanLooping()
    {
        var flow = new FlowDefinition
        {
            Id = "loop",
            Name = "Deliberate loop",
            StartNodeId = "a",
            AllowCycles = true,
            MaxSteps = 6,
            Nodes =
            [
                new FlowNode { Id = "a", Kind = FlowNodeKind.Condition, Name = "A" },
                new FlowNode { Id = "b", Kind = FlowNodeKind.Condition, Name = "B" }
            ],
            Edges =
            [
                new FlowEdge { Id = "ab", FromNodeId = "a", ToNodeId = "b" },
                new FlowEdge { Id = "ba", FromNodeId = "b", ToNodeId = "a" }
            ]
        };

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver());

        var run = new FlowRunner(flow, runtime).RunAsync("go");
        var completed = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(run, completed);

        var result = await run;
        Assert.Equal(FlowRunOutcome.StepBudgetExhausted, result.Outcome);
        Assert.Equal(6, result.StepsExecuted);
        Assert.Equal(new[] { "a", "b", "a", "b", "a", "b" }, result.VisitedNodeIds);
        Assert.Contains(result.Steps, step => step.Kind == AgentStepKind.StepBudgetExhausted);
    }

    /// <summary>
    /// A cyclic flow that has NOT opted into cycles never starts: the validator rejects it and the
    /// runner reports the errors instead of relying on the budget to notice.
    /// </summary>
    [Fact]
    public async Task ACycleIsRefusedBeforeRunningUnlessTheFlowAllowsIt()
    {
        var flow = new FlowDefinition
        {
            Id = "loop",
            Name = "Accidental loop",
            StartNodeId = "a",
            Nodes =
            [
                new FlowNode { Id = "a", Kind = FlowNodeKind.Condition, Name = "A" },
                new FlowNode { Id = "b", Kind = FlowNodeKind.Condition, Name = "B" }
            ],
            Edges =
            [
                new FlowEdge { Id = "ab", FromNodeId = "a", ToNodeId = "b" },
                new FlowEdge { Id = "ba", FromNodeId = "b", ToNodeId = "a" }
            ]
        };

        var result = await new FlowRunner(flow, new FlowRuntime(new InMemoryFlowAgentResolver())).RunAsync("go");

        Assert.Equal(FlowRunOutcome.Failed, result.Outcome);
        Assert.Equal(0, result.StepsExecuted);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == FlowValidationCodes.CycleDetected);
    }

    /// <summary>
    /// A node's output is stored in the variable it names, and a later condition branches on that
    /// variable rather than on the running output.
    /// </summary>
    [Fact]
    public async Task ANodeOutputVariableIsReadableByALaterCondition()
    {
        var agent = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("HIGH"), ScriptedLlmProvider.Says("acknowledged"));

        var flow = new FlowDefinition
        {
            Id = "priority",
            Name = "Priority routing",
            StartNodeId = "assess",
            Nodes =
            [
                new FlowNode { Id = "assess", Kind = FlowNodeKind.Agent, AgentId = "agent", OutputVariable = "priority" },
                new FlowNode { Id = "ack", Kind = FlowNodeKind.Agent, AgentId = "agent" },
                new FlowNode { Id = "urgent", Kind = FlowNodeKind.Terminal, TerminalStatus = "urgent" },
                new FlowNode { Id = "normal", Kind = FlowNodeKind.Terminal, TerminalStatus = "normal" }
            ],
            Edges =
            [
                new FlowEdge { Id = "e0", FromNodeId = "assess", ToNodeId = "ack" },
                new FlowEdge
                {
                    Id = "e1", FromNodeId = "ack", ToNodeId = "urgent", Order = 0,
                    Condition = new FlowCondition
                    {
                        Kind = FlowConditionKind.EqualsText,
                        Source = FlowConditionSource.Variable,
                        SourceKey = "priority",
                        Operand = "HIGH"
                    }
                },
                new FlowEdge { Id = "e2", FromNodeId = "ack", ToNodeId = "normal", Order = 1 }
            ]
        };

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("agent", agent)));
        var result = await new FlowRunner(flow, runtime).RunAsync("something broke");

        Assert.Equal("urgent", result.TerminalStatus);
        Assert.Equal("HIGH", result.Variables["priority"]);
    }

    /// <summary>Token usage from every agent turn is summed onto the run result.</summary>
    [Fact]
    public async Task TheRunAggregatesTokenUsageAcrossAgentTurns()
    {
        var agent = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("one"), ScriptedLlmProvider.Says("two"));

        var flow = new FlowDefinition
        {
            Id = "two-turns",
            Name = "Two turns",
            StartNodeId = "first",
            Nodes =
            [
                new FlowNode { Id = "first", Kind = FlowNodeKind.Agent, AgentId = "agent" },
                new FlowNode { Id = "second", Kind = FlowNodeKind.Agent, AgentId = "agent" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
            ],
            Edges =
            [
                new FlowEdge { Id = "e1", FromNodeId = "first", ToNodeId = "second" },
                new FlowEdge { Id = "e2", FromNodeId = "second", ToNodeId = "end" }
            ]
        };

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("agent", agent)));
        var result = await new FlowRunner(flow, runtime).RunAsync("start");

        Assert.Equal(20, result.Usage.InputTokens);
        Assert.Equal(10, result.Usage.OutputTokens);
    }

    /// <summary>
    /// An agent node's tool calls run through the real agent loop, and their steps appear in the
    /// flow trace attributed to the node they happened in.
    /// </summary>
    [Fact]
    public async Task InnerAgentToolStepsAreAttributedToTheirNode()
    {
        var agent = new ScriptedLlmProvider(
            "agent",
            ScriptedLlmProvider.CallsTool("search", """{"query":"invoices"}"""),
            ScriptedLlmProvider.Says("found it"));

        var tools = new RecordingToolHandler().Register("search", "Searches", _ => "three invoices");

        var flow = new FlowDefinition
        {
            Id = "one-agent",
            Name = "One agent",
            StartNodeId = "work",
            Nodes =
            [
                new FlowNode { Id = "work", Kind = FlowNodeKind.Agent, Name = "Worker", AgentId = "agent" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
            ],
            Edges = [new FlowEdge { Id = "e", FromNodeId = "work", ToNodeId = "end" }]
        };

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(new FlowAgent("agent", agent, tools)));
        var result = await new FlowRunner(flow, runtime).RunAsync("find my invoices");

        Assert.Equal(new[] { "search" }, tools.Executed);

        var executed = Assert.Single(result.Steps, step => step.Kind == AgentStepKind.ToolExecuted);
        Assert.Equal("work", executed.NodeId);
        Assert.Equal("Worker", executed.NodeName);
        Assert.Equal("search", executed.ToolName);
        Assert.Equal("three invoices", executed.Content);
    }

    /// <summary>Builds the classifier-plus-branch flow both routing tests share.</summary>
    /// <returns>A flow that escalates on "refund" and otherwise resolves automatically.</returns>
    private static FlowDefinition BranchingFlow() => new()
    {
        Id = "branching",
        Name = "Branching support",
        StartNodeId = "classify",
        Nodes =
        [
            new FlowNode { Id = "classify", Kind = FlowNodeKind.Agent, AgentId = "classifier" },
            new FlowNode { Id = "branch", Kind = FlowNodeKind.Condition, Name = "Refund?" },
            new FlowNode { Id = "escalate", Kind = FlowNodeKind.Tool, ToolName = "escalate" },
            new FlowNode { Id = "auto", Kind = FlowNodeKind.Tool, ToolName = "autoresolve" },
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
            new FlowEdge { Id = "e2", FromNodeId = "branch", ToNodeId = "auto", Order = 1, Label = "everything else" },
            new FlowEdge { Id = "e3", FromNodeId = "escalate", ToNodeId = "end" },
            new FlowEdge { Id = "e4", FromNodeId = "auto", ToNodeId = "end" }
        ]
    };
}
