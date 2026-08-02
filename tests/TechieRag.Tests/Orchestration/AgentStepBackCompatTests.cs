using TechieRag.Models;
using TechieRag.Orchestration;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// Guards the existing single-agent trace against the orchestration work (REQ-RAG-042 vs
/// REQ-RAG-021 / BRD-85).
/// </summary>
/// <remarks>
/// <para>The app renders an execution trace by switching on <see cref="AgentStepKind"/> and reading
/// <see cref="AgentStep"/>'s properties. Orchestration extends both. These tests pin down what
/// extending them was not allowed to change: the original four kinds keep their names AND their
/// ordinals, the shipped <see cref="AgentLoopRunner"/> still emits only those four, and it still
/// emits plain <see cref="AgentStep"/> values rather than something a consumer must down-cast.</para>
/// </remarks>
public sealed class AgentStepBackCompatTests
{
    /// <summary>
    /// The four original kinds keep their names and their ordinal positions, so neither a switch on
    /// the name nor anything that persisted the number changes meaning.
    /// </summary>
    [Fact]
    public void TheOriginalStepKindsKeepTheirNamesAndOrdinals()
    {
        Assert.Equal(0, (int)AgentStepKind.ToolCallRequested);
        Assert.Equal(1, (int)AgentStepKind.ToolExecuted);
        Assert.Equal(2, (int)AgentStepKind.FinalAnswer);
        Assert.Equal(3, (int)AgentStepKind.MaxIterationsReached);
    }

    /// <summary>
    /// A full single-agent run with tool calls emits exactly the kinds it always did — no new kind
    /// appears on a path an existing consumer already drives.
    /// </summary>
    [Fact]
    public async Task TheSingleAgentLoopStillEmitsOnlyTheOriginalKinds()
    {
        var provider = new ScriptedLlmProvider(
            "agent",
            ScriptedLlmProvider.CallsTool("search", """{"query":"x"}"""),
            ScriptedLlmProvider.Says("the answer"));

        var tools = new RecordingToolHandler().Register("search", "Searches", _ => "a result");
        var progress = new RecordingProgress();

        var response = await new AgentLoopRunner(provider, tools).RunAsync(
            [ChatMessage.User("find x")], null, progress);

        Assert.Equal("the answer", response.Content);
        Assert.Equal(
            new[] { AgentStepKind.ToolCallRequested, AgentStepKind.ToolExecuted, AgentStepKind.FinalAnswer },
            progress.Steps.Select(step => step.Kind).ToArray());
    }

    /// <summary>
    /// The single-agent loop reports plain <see cref="AgentStep"/> values, not flow steps. An
    /// existing consumer is never handed something it has to know about to render.
    /// </summary>
    [Fact]
    public async Task TheSingleAgentLoopStillReportsPlainAgentSteps()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("done"));
        var progress = new RecordingProgress();

        await new AgentLoopRunner(provider, new RecordingToolHandler()).RunAsync(
            [ChatMessage.User("hello")], null, progress);

        var step = Assert.Single(progress.Steps);
        Assert.Equal(typeof(AgentStep), step.GetType());
        Assert.IsNotType<FlowStep>(step);
    }

    /// <summary>
    /// The iteration ceiling still forces a final answer and still reports
    /// <see cref="AgentStepKind.MaxIterationsReached"/>, which the app renders as its own row.
    /// </summary>
    [Fact]
    public async Task TheIterationCeilingStillReportsMaxIterationsReached()
    {
        var provider = new ScriptedLlmProvider(
            "agent",
            ScriptedLlmProvider.CallsTool("search", """{"query":"x"}"""),
            ScriptedLlmProvider.CallsTool("search", """{"query":"x"}"""),
            ScriptedLlmProvider.Says("forced answer"));

        var tools = new RecordingToolHandler().Register("search", "Searches", _ => "a result");
        var progress = new RecordingProgress();

        var response = await new AgentLoopRunner(provider, tools, maxIterations: 2).RunAsync(
            [ChatMessage.User("find x")], null, progress);

        Assert.Equal("forced answer", response.Content);
        Assert.Equal(AgentStepKind.MaxIterationsReached, progress.Steps[^1].Kind);
    }

    /// <summary>
    /// A flow step IS an agent step, so a sink typed to the existing channel accepts one and can
    /// read every property it already reads.
    /// </summary>
    [Fact]
    public void AFlowStepIsUsableWhereverAnAgentStepIs()
    {
        var progress = new RecordingProgress();
        IProgress<AgentStep> sink = progress;

        sink.Report(new FlowStep
        {
            RunId = "run-1",
            Iteration = 3,
            Kind = AgentStepKind.ToolExecuted,
            ToolName = "search",
            ToolArgumentsJson = """{"q":"x"}""",
            Content = "a result",
            NodeId = "work",
            NodeName = "Work",
            NodeKind = FlowNodeKind.Agent
        });

        var step = Assert.Single(progress.Steps);
        Assert.Equal(3, step.Iteration);
        Assert.Equal(AgentStepKind.ToolExecuted, step.Kind);
        Assert.Equal("search", step.ToolName);
        Assert.Equal("a result", step.Content);
        Assert.True(step.IsSuccess);
    }

    /// <summary>
    /// Flow-only kinds are additive: they exist, and they are none of the four the single-agent loop
    /// emits, so an existing renderer's arms are untouched.
    /// </summary>
    [Fact]
    public void TheFlowOnlyKindsAreAllNewMembers()
    {
        AgentStepKind[] original =
        [
            AgentStepKind.ToolCallRequested,
            AgentStepKind.ToolExecuted,
            AgentStepKind.FinalAnswer,
            AgentStepKind.MaxIterationsReached
        ];

        AgentStepKind[] added =
        [
            AgentStepKind.NodeStarted,
            AgentStepKind.NodeCompleted,
            AgentStepKind.RouteTaken,
            AgentStepKind.HandoffPerformed,
            AgentStepKind.GuardrailBlocked,
            AgentStepKind.StepBudgetExhausted,
            AgentStepKind.FlowCompleted
        ];

        Assert.Empty(original.Intersect(added));
        Assert.Equal(original.Length + added.Length, Enum.GetValues<AgentStepKind>().Length);
    }
}
