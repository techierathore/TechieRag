using TechieDesk.Services.Agents;
using TechieDesk.Services.Flows;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Orchestration;
using TechieRag.Services;
using Xunit;

namespace TechieDesk.Tests.Flows;

/// <summary>
/// A flow is not a route around the REQ-NFR-013 egress gate (REQ-UI-040 / BRD-92).
/// </summary>
/// <remarks>
/// <para><b>Why this is the test that matters most on this requirement.</b> REQ-NFR-013 made
/// "Ask before any skill that leaves this machine" real for a chat turn. A flow is a SECOND execution
/// path to the same tools. Without the host guardrail, composing a flow would have been a supported
/// way to reach an egress-marked tool with the switch still promising it would ask — the same class
/// of defect as REQ-FN-010 and REQ-FN-052, arriving through a new door.</para>
/// <para><b>Each test drives the real <c>FlowRunner</c>.</b> Nothing here asserts that a method was
/// called; the assertions are on the run's OUTCOME, so a guardrail that is installed but not
/// consulted fails exactly as one that is not installed.</para>
/// </remarks>
public sealed class FlowEgressTests
{
    private const string EgressToolName = "web-search";

    /// <summary>
    /// A flow whose tool step would leave the machine does NOT reach it when the user declines, and
    /// the refusal is recorded in the trace against the host's gate.
    /// </summary>
    /// <remarks>
    /// <para>The tool records whether it was invoked, so this cannot pass by refusing somewhere after
    /// the request went out. That flag is the security assertion; the trace entry is the honesty
    /// assertion.</para>
    /// <para><b>The run still COMPLETES, and that is correct.</b> The library's contract for a refused
    /// tool call is an unsuccessful <c>ToolResult</c> rather than a run-level stop, so an agent can
    /// report the tool as unavailable and finish with what it has — the same shape TechieDesk's own
    /// <c>SkillUnavailable</c> channel already uses. What must never happen is the call going out,
    /// and that is what is asserted.</para>
    /// </remarks>
    [Fact]
    public async Task AFlowCannotReachAnEgressToolWhenTheUserDeclines()
    {
        var wasCalled = false;
        var runtime = RuntimeWith(ToolsThatRecord(() => wasCalled = true), Confirmation.Denying);

        var result = await new FlowRunner(EgressFlow(), runtime).RunAsync("go");

        Assert.False(wasCalled, "The tool ran even though the egress gate refused it.");
        AssertBlockedByTheGate(result);
    }

    /// <summary>The same flow runs the tool when the user allows it, so the gate is a gate and not a wall.</summary>
    [Fact]
    public async Task AFlowReachesAnEgressToolWhenTheUserAllows()
    {
        var wasCalled = false;
        var runtime = RuntimeWith(ToolsThatRecord(() => wasCalled = true), Confirmation.Allowing);

        var result = await new FlowRunner(EgressFlow(), runtime).RunAsync("go");

        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.True(wasCalled);
    }

    /// <summary>
    /// Clearing every guardrail the flow itself names does not remove the host's egress gate.
    /// </summary>
    /// <remarks>
    /// This is the property that makes the wiring meaningful rather than decorative: the gate lives
    /// in <c>FlowRuntime.HostGuardrails</c>, which is host code supplied at run time, so there is no
    /// edit to a stored flow that turns it off. A flow author who deletes their own guardrails gets
    /// the gate anyway.
    /// </remarks>
    [Fact]
    public async Task ClearingTheFlowsOwnGuardrailsDoesNotRemoveTheEgressGate()
    {
        var wasCalled = false;
        var runtime = RuntimeWith(ToolsThatRecord(() => wasCalled = true), Confirmation.Denying);

        var flow = EgressFlow();
        foreach (var node in flow.Nodes)
        {
            node.GuardrailIds.Clear();
        }

        var result = await new FlowRunner(flow, runtime).RunAsync("go");

        Assert.False(wasCalled);
        AssertBlockedByTheGate(result);
    }

    /// <summary>
    /// The egress gate's id is not one a flow author can choose, and naming it does not bind it.
    /// </summary>
    /// <remarks>
    /// If <c>host-egress</c> were in the author's palette, a flow could carry it as an ordinary
    /// guardrail id — and an id a flow can ADD is an id a flow can REMOVE. It resolves to null here,
    /// which the library treats as a block, so guessing the name cannot weaken anything either.
    /// </remarks>
    [Fact]
    public async Task TheEgressGuardrailIsNotOfferedToFlowAuthors()
    {
        var catalogue = new FlowGuardrailCatalog();

        Assert.DoesNotContain(
            catalogue.Available,
            guardrail => guardrail.Id == FlowHostGuardrails.EgressGuardrailId);

        Assert.Null(await catalogue.ResolveGuardrailAsync(FlowHostGuardrails.EgressGuardrailId));
    }

    /// <summary>
    /// A run with no way to ask the user denies rather than assuming yes.
    /// </summary>
    /// <remarks>
    /// Fail-closed, matching <c>EgressGate</c>'s own rule for a chat turn: a host that cannot raise
    /// the prompt gets a visibly blocked step, never silent egress.
    /// </remarks>
    [Fact]
    public async Task AFlowWithNoWayToAskDeniesEgress()
    {
        var wasCalled = false;
        var runtime = RuntimeWith(ToolsThatRecord(() => wasCalled = true), confirmation: null);

        var result = await new FlowRunner(EgressFlow(), runtime).RunAsync("go");

        Assert.False(wasCalled);
        AssertBlockedByTheGate(result);
    }

    /// <summary>
    /// A guardrail the flow's AUTHOR attached stops the run outright, which is the other half of the
    /// story: author rules and host rules are both real, and only one of them is removable.
    /// </summary>
    [Fact]
    public async Task AnAuthorGuardrailStopsTheRun()
    {
        var runtime = RuntimeWith(ToolsThatRecord(() => { }), Confirmation.Allowing);

        var flow = EgressFlow();
        flow.Nodes[0].GuardrailIds.Add(FlowGuardrailCatalog.LocalToolsOnlyId);

        var result = await new FlowRunner(flow, runtime).RunAsync("go");

        Assert.Contains(
            result.Steps.OfType<FlowStep>(),
            step => step.Kind == AgentStepKind.GuardrailBlocked
                    && step.GuardrailId == FlowGuardrailCatalog.LocalToolsOnlyId);
    }

    /// <summary>
    /// An agent whose egress setting is OFF is not prompted, so the setting still means what it says
    /// in both directions.
    /// </summary>
    [Fact]
    public async Task AnAgentThatDoesNotRequireConfirmationIsNotPrompted()
    {
        var wasCalled = false;
        var wasAsked = false;
        var agent = BuiltInAgent(confirmEgress: false);
        var gate = new EgressGate(agent, new Confirmation(false, () => wasAsked = true));

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver())
        {
            Tools = ToolsThatRecord(() => wasCalled = true)
        };
        FlowHostGuardrails.InstallOn(runtime, gate);

        var result = await new FlowRunner(EgressFlow(), runtime).RunAsync("go");

        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.True(wasCalled);
        Assert.False(wasAsked, "The user was prompted even though this agent does not ask.");
    }

    /// <summary>
    /// A Tool-node run RESUMES and completes after the prompt is answered late, from another thread —
    /// which is what a real dialog does and what every test above skipped (REQ-FN-053).
    /// </summary>
    /// <remarks>
    /// <para><b>Every existing test on this requirement answered synchronously.</b>
    /// <c>Confirmation.ConfirmAsync</c> returns <c>Task.FromResult</c>, so the gate's
    /// <c>TaskCompletionSource</c> was already complete before anything awaited it and the resume path
    /// — the one the defect is on — never ran. That is precisely why a suite this thorough still let a
    /// P1 hang reach the Catalyst head, and why these two tests defer the answer instead.</para>
    /// <para>Bounded with <c>WaitAsync</c> so the failure mode is a failing test rather than a hung
    /// test run.</para>
    /// </remarks>
    /// <param name="isAllowed">The answer the user gives; the defect reproduced on both.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AToolNodeRunCompletesAfterTheEgressPromptIsAnsweredLate(bool isAllowed)
    {
        var wasCalled = false;
        var confirmation = new DeferredConfirmation();
        var runtime = RuntimeWith(ToolsThatRecord(() => wasCalled = true), confirmation);

        var run = new FlowRunner(EgressFlow(), runtime).RunAsync("go");

        // The prompt is raised and the run is genuinely parked on it, as it is on screen.
        await confirmation.Asked.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(run.IsCompleted, "The run finished without waiting for the answer.");

        confirmation.Answer(isAllowed);

        var result = await run.WaitAsync(TimeSpan.FromSeconds(10));

        // Completed either way: a refused tool call is an unsuccessful ToolResult, not a run-level
        // stop, so the flow carries on to its terminal node. Only whether the tool RAN differs.
        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.Equal(isAllowed, wasCalled);

        // The trace has to move past NodeStarted; stalling there is the reported symptom.
        Assert.Contains(
            result.Steps.OfType<FlowStep>(),
            step => step.Kind is AgentStepKind.ToolExecuted or AgentStepKind.GuardrailBlocked);
    }

    /// <summary>
    /// A prompt that is never answered stops being an unbounded hang: the run's cancellation token
    /// ends it, and the run reports an outcome the screen can render (REQ-FN-053).
    /// </summary>
    /// <remarks>
    /// The flows screen used to call <c>RunAsync</c> with no token at all, so there was nothing to
    /// end a stalled run — no exception, no result, a button stuck on "running". The service now
    /// derives one from the owning agent's time limit, exactly as a chat turn does; this test drives
    /// the same mechanism through an explicit token so it does not have to wait out a real limit.
    /// </remarks>
    [Fact]
    public async Task AnUnansweredEgressPromptEndsWithTheRunsCancellation()
    {
        var wasCalled = false;
        var confirmation = new DeferredConfirmation();
        var runtime = RuntimeWith(ToolsThatRecord(() => wasCalled = true), confirmation);

        using var deadline = new CancellationTokenSource();
        var run = new FlowRunner(EgressFlow(), runtime).RunAsync("go", cancellationToken: deadline.Token);

        await confirmation.Asked.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(run.IsCompleted);

        // Nobody ever clicks the dialog. Without a token this is where the run hangs forever.
        await deadline.CancelAsync();

        var result = await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(wasCalled, "The tool ran even though the prompt was never answered.");
        Assert.NotEqual(FlowRunOutcome.Completed, result.Outcome);
    }

    /// <summary>
    /// Asserts the run recorded a refusal by the HOST's egress gate, naming it.
    /// </summary>
    /// <param name="result">The finished run.</param>
    /// <remarks>
    /// The trace entry is asserted rather than only the "did not run" flag because a refusal nobody
    /// can see is indistinguishable from a tool that quietly returned nothing — which is how a gate
    /// becomes decorative even while it is working.
    /// </remarks>
    private static void AssertBlockedByTheGate(FlowRunResult result) =>
        Assert.Contains(
            result.Steps.OfType<FlowStep>(),
            step => step.Kind == AgentStepKind.GuardrailBlocked
                    && step.GuardrailId == FlowHostGuardrails.EgressGuardrailId);

    /// <summary>Builds a runtime wired exactly as the application wires one for a run.</summary>
    /// <param name="tools">The tools the flow's steps may call.</param>
    /// <param name="confirmation">How the user is asked, or null when this host cannot ask.</param>
    /// <returns>The runtime.</returns>
    private static FlowRuntime RuntimeWith(IToolHandler tools, IEgressConfirmation? confirmation)
    {
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver())
        {
            Tools = tools,
            Guardrails = new FlowGuardrailCatalog()
        };

        FlowHostGuardrails.InstallOn(runtime, new EgressGate(BuiltInAgent(confirmEgress: true), confirmation));
        return runtime;
    }

    /// <summary>Builds the two-step flow: call a tool that leaves the machine, then end.</summary>
    /// <returns>The flow.</returns>
    private static FlowDefinition EgressFlow()
    {
        var call = FlowNodeCatalog.CreateNode(FlowNodeKind.Tool, "step-call");
        call.ToolName = EgressToolName;
        call.ToolArgumentsJson = """{"query":"anything"}""";

        var end = FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "step-end");

        return new FlowDefinition
        {
            Id = "flow-egress",
            Name = "Egress flow",
            StartNodeId = call.Id,
            Nodes = [call, end],
            Edges = [new FlowEdge { Id = "edge-1", FromNodeId = call.Id, ToNodeId = end.Id }]
        };
    }

    /// <summary>Builds a tool registry whose one tool records that it ran.</summary>
    /// <param name="onCalled">Raised when the tool is actually invoked.</param>
    /// <returns>The registry.</returns>
    private static ToolRegistry ToolsThatRecord(Action onCalled)
    {
        var registry = new ToolRegistry();
        registry.Register(
            EgressToolName,
            "Searches the web, which sends a request off this machine.",
            """{"type":"object","properties":{"query":{"type":"string"}}}""",
            arguments =>
            {
                onCalled();
                return "results";
            });

        return registry;
    }

    /// <summary>Builds the agent whose egress setting governs the run.</summary>
    /// <param name="confirmEgress">Whether it asks before anything leaves the machine.</param>
    /// <returns>The agent.</returns>
    private static AgentDefinition BuiltInAgent(bool confirmEgress)
    {
        var agent = AgentDefinition.BuiltIn("workspace-test", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        agent.ConfirmEgress = confirmEgress;
        return agent;
    }

    /// <summary>An <see cref="IEgressConfirmation"/> with a fixed answer, recording that it was asked.</summary>
    /// <param name="answer">What the user says.</param>
    /// <param name="onAsked">Raised when the prompt would have been shown.</param>
    private sealed class Confirmation(bool answer, Action? onAsked = null) : IEgressConfirmation
    {
        /// <summary>Gets a confirmation that always denies.</summary>
        public static Confirmation Denying { get; } = new(false);

        /// <summary>Gets a confirmation that always allows.</summary>
        public static Confirmation Allowing { get; } = new(true);

        /// <inheritdoc />
        public Task<bool> ConfirmAsync(EgressConfirmationRequest request, CancellationToken cancellationToken)
        {
            onAsked?.Invoke();
            return Task.FromResult(answer);
        }
    }

    /// <summary>
    /// An <see cref="IEgressConfirmation"/> that does not answer until the test says so — a modal the
    /// user has not clicked yet (REQ-FN-053).
    /// </summary>
    private sealed class DeferredConfirmation : IEgressConfirmation
    {
        private readonly TaskCompletionSource asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> answered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets a task that completes once the prompt has been raised.</summary>
        public Task Asked => asked.Task;

        /// <summary>Answers the outstanding prompt, as clicking a dialog button does.</summary>
        /// <param name="isAllowed">The user's answer.</param>
        public void Answer(bool isAllowed) => answered.TrySetResult(isAllowed);

        /// <inheritdoc />
        public async Task<bool> ConfirmAsync(
            EgressConfirmationRequest request, CancellationToken cancellationToken)
        {
            asked.TrySetResult();
            await using var registration = cancellationToken.Register(() => answered.TrySetResult(false))
                .ConfigureAwait(false);
            return await answered.Task.ConfigureAwait(false);
        }
    }
}
