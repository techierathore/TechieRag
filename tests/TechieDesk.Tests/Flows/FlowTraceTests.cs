using TechieDesk.Services.Agents;
using TechieDesk.Tests.Support;
using TechieRag.Models;
using TechieRag.Orchestration;
using Xunit;

namespace TechieDesk.Tests.Flows;

/// <summary>
/// A flow run's trace says what actually happened, and never labels a flow step as the agent's final
/// answer (REQ-UI-040 / REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>The defect being guarded.</b> <c>AgentTrace.Add</c> maps a step KIND to a display row and
/// ends in a <c>_ =&gt;</c> arm that renders anything unrecognised as "Final answer". REQ-RAG-042
/// appended seven new kinds. Without an explicit arm for each, a flow run would have rendered every
/// node, every branch and every guardrail refusal as the agent's answer — a trace that lies about
/// what ran, which is the exact defect class REQ-FN-010, REQ-NFR-013 and REQ-FN-052 each were.</para>
/// <para><b>Why the fallback is asserted against by NAME.</b> Checking only that the title "looks
/// right" would pass if the mapping were changed to something else wrong. Asserting the row is not
/// the fallback string is what makes a deleted arm fail here rather than in a screenshot.</para>
/// </remarks>
public sealed class FlowTraceTests
{
    private const string Fallback = "Final answer";

    /// <summary>
    /// The resource key the fallback arm uses. REQ-UI-051 turned the row titles into keys; WHICH
    /// kind maps to WHICH label is unchanged, and that is what these tests still pin.
    /// </summary>
    private const string FallbackKey = AgentTrace.FinalAnswerTitleKey;

    /// <summary>
    /// Every flow step kind renders as itself rather than falling through to "Final answer".
    /// </summary>
    /// <param name="kind">The flow step kind under test.</param>
    /// <param name="expected">Text the rendered title must contain.</param>
    [Theory]
    [InlineData(AgentStepKind.NodeStarted, "Started")]
    [InlineData(AgentStepKind.NodeCompleted, "Finished")]
    [InlineData(AgentStepKind.RouteTaken, "Routed to")]
    [InlineData(AgentStepKind.HandoffPerformed, "Handed off to")]
    [InlineData(AgentStepKind.GuardrailBlocked, "Blocked by")]
    [InlineData(AgentStepKind.StepBudgetExhausted, "Step budget exhausted")]
    [InlineData(AgentStepKind.FlowCompleted, "Flow completed")]
    public void EveryFlowStepKindRendersAsItself(AgentStepKind kind, string expected)
    {
        var trace = new AgentTrace(() => 0);

        trace.Add(new FlowStep
        {
            RunId = "run-1",
            Iteration = 1,
            Kind = kind,
            NodeId = "step-triage",
            NodeName = "Triage",
            NodeKind = FlowNodeKind.Agent,
            ToNodeId = "step-end",
            EdgeId = "edge-1",
            GuardrailId = "host-egress",
            Content = "something happened"
        });

        using var resources = new ResourceHarness("en");
        var entry = Assert.Single(trace.Entries);

        Assert.NotEqual(FallbackKey, entry.TitleKey);
        Assert.Contains(expected, entry.Title(resources.Localize), StringComparison.Ordinal);
    }

    /// <summary>The four single-agent kinds keep rendering exactly as they did.</summary>
    /// <remarks>
    /// The seven new arms sit above the fallback that <c>FinalAnswer</c> still uses, so this is the
    /// other half: adding them must not have moved the chat trace's existing rows.
    /// </remarks>
    [Fact]
    public void TheSingleAgentKindsAreUnchanged()
    {
        var trace = new AgentTrace(() => 0);

        trace.Add(new AgentStep { Iteration = 1, Kind = AgentStepKind.ToolCallRequested, ToolName = "rag-search" });
        trace.Add(new AgentStep { Iteration = 1, Kind = AgentStepKind.ToolExecuted, ToolName = "rag-search", ToolArgumentsJson = "{}", Content = "hits", IsSuccess = true });
        trace.Add(new AgentStep { Iteration = 2, Kind = AgentStepKind.MaxIterationsReached });
        trace.Add(new AgentStep { Iteration = 2, Kind = AgentStepKind.FinalAnswer, Content = "the answer" });

        using var resources = new ResourceHarness("en");

        Assert.Equal("Requested rag-search", trace.Entries[0].Title(resources.Localize));
        Assert.Equal("rag-search", trace.Entries[1].Title(resources.Localize));
        Assert.Equal("Tool-call limit reached", trace.Entries[2].Title(resources.Localize));
        Assert.Equal(Fallback, trace.Entries[3].Title(resources.Localize));
        Assert.Equal(FallbackKey, trace.Entries[3].TitleKey);
    }

    /// <summary>A guardrail refusal renders as a failed row, so a blocked step cannot read as a green one.</summary>
    [Fact]
    public void AGuardrailRefusalRendersAsAFailure()
    {
        var trace = new AgentTrace(() => 0);

        trace.Add(new FlowStep
        {
            RunId = "run-1",
            Iteration = 1,
            Kind = AgentStepKind.GuardrailBlocked,
            NodeId = "step-call",
            NodeName = "Search the web",
            GuardrailId = "host-egress",
            IsSuccess = false,
            ErrorMessage = "approval was not given"
        });

        using var resources = new ResourceHarness("en");
        var entry = Assert.Single(trace.Entries);

        Assert.False(entry.IsSuccess);

        // The guardrail id is an ARGUMENT, not part of the translated sentence: it is the flow
        // author's own identifier and must read the same in every language.
        Assert.Contains("host-egress", entry.TitleArguments);
        Assert.Contains("host-egress", entry.Title(resources.Localize), StringComparison.Ordinal);
        Assert.Contains(
            "approval was not given", entry.DetailText(resources.Localize), StringComparison.Ordinal);
    }

    /// <summary>A nested flow's steps are visibly nested rather than flattened into the outer run.</summary>
    [Fact]
    public void ANestedFlowStepIsMarkedAsNested()
    {
        var trace = new AgentTrace(() => 0);

        trace.Add(new FlowStep
        {
            RunId = "run-1",
            Iteration = 1,
            Kind = AgentStepKind.NodeStarted,
            NodeId = "inner",
            NodeName = "Inner step",
            Depth = 1
        });

        using var resources = new ResourceHarness("en");
        var entry = Assert.Single(trace.Entries);

        Assert.StartsWith("›", entry.DepthPrefix, StringComparison.Ordinal);
        Assert.StartsWith("›", entry.Title(resources.Localize), StringComparison.Ordinal);
    }

    /// <summary>
    /// A real flow run, rendered end to end, produces no row labelled as the final answer.
    /// </summary>
    /// <remarks>
    /// The per-kind theory above proves the mapping. This proves the WIRING: the flow is executed by
    /// the real <c>FlowRunner</c>, the trace is fed by the same <c>IProgress</c> channel the chat
    /// surface uses, and the assertion is over what a user would actually see. The flow deliberately
    /// contains no agent step, so every row in it is a flow row and there is no legitimate
    /// "Final answer" to confuse the assertion.
    /// </remarks>
    [Fact]
    public async Task ARealFlowRunNeverRendersAStepAsTheFinalAnswer()
    {
        var branch = FlowNodeCatalog.CreateNode(FlowNodeKind.Condition, "step-branch");
        var end = FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "step-end");

        var flow = new FlowDefinition
        {
            Id = "flow-trace",
            Name = "Trace flow",
            StartNodeId = branch.Id,
            Nodes = [branch, end],
            Edges = [new FlowEdge { Id = "edge-1", FromNodeId = branch.Id, ToNodeId = end.Id }]
        };

        var trace = new AgentTrace(() => 0);
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver());

        var result = await new FlowRunner(flow, runtime).RunAsync("hello", null, trace.AsProgress());

        using var resources = new ResourceHarness("en");

        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.NotEmpty(trace.Entries);
        Assert.DoesNotContain(trace.Entries, entry => entry.TitleKey == FallbackKey);
        Assert.Contains(
            trace.Entries,
            entry => entry.Title(resources.Localize).Contains("Routed to", StringComparison.Ordinal));
        Assert.Contains(
            trace.Entries,
            entry => entry.Title(resources.Localize).Contains("Flow completed", StringComparison.Ordinal));
    }
}
