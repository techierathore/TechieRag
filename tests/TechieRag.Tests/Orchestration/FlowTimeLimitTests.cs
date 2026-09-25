using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Orchestration;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// REQ-RAG-088 / BRD-135: a flow run completes or fails within a configurable time limit, and a
/// tool-node flow waiting on a host confirmation ends with a coded timeout message instead of
/// hanging.
/// </summary>
public sealed class FlowTimeLimitTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A tool node whose tool waits on a host confirmation that never arrives — and that does not
    /// observe its cancellation token — ends at the time limit with <see cref="FlowRunOutcome.TimedOut"/>
    /// and a <see cref="FlowMessageCodes.FlowTimedOut"/> message naming the limit and the node.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-088 ToolAwaitingConfirmationTimesOutWithACode")]
    public async Task ToolAwaitingConfirmationTimesOutWithACode()
    {
        var confirmation = new TaskCompletionSource<bool>();
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver())
        {
            Tools = new ConfirmationToolHandler(confirmation.Task),
            TimeLimit = Limit
        };

        var result = await RunWithinCeilingAsync(new FlowRunner(ConfirmationFlow(), runtime).RunAsync("send it"));

        Assert.Equal(FlowRunOutcome.TimedOut, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal("send", result.LastNodeId);
        Assert.NotNull(result.FailureMessage);
        Assert.Equal(FlowMessageCodes.FlowTimedOut, result.FailureMessage!.Code);
        Assert.Equal(new[] { "0.2", "Send e-mail" }, result.FailureMessage.Arguments);
        Assert.Equal(result.FailureMessage.Text, result.FailureReason);
    }

    /// <summary>
    /// The timeout is also on the trace: one <see cref="AgentStepKind.FlowTimedOut"/> step, failed,
    /// carrying the same code, so a renderer showing the run after the fact sees why it stopped.
    /// </summary>
    [Fact]
    public async Task TheTimeoutIsRecordedAsACodedTraceStep()
    {
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver())
        {
            Tools = new ConfirmationToolHandler(new TaskCompletionSource<bool>().Task),
            TimeLimit = Limit
        };
        var progress = new RecordingProgress();

        var result = await RunWithinCeilingAsync(
            new FlowRunner(ConfirmationFlow(), runtime).RunAsync("send it", progress: progress));

        var step = Assert.Single(result.Steps, candidate => candidate.Kind == AgentStepKind.FlowTimedOut);
        Assert.False(step.IsSuccess);
        Assert.Equal("send", step.NodeId);
        Assert.Equal(FlowMessageCodes.FlowTimedOut, step.FailureMessage?.Code);
        Assert.Contains(progress.Steps, reported => reported.Kind == AgentStepKind.FlowTimedOut);
    }

    /// <summary>
    /// A host egress confirmation plugged in as a ToolCall guardrail that never answers also ends at
    /// the limit, and the tool it was guarding never runs.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-088 GuardrailConfirmationThatNeverAnswersTimesOut")]
    public async Task GuardrailConfirmationThatNeverAnswersTimesOut()
    {
        var tools = new RecordingToolHandler().Register("send-mail", "Sends an e-mail", _ => "sent");
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver()) { Tools = tools, TimeLimit = Limit };
        runtime.HostGuardrails.Add(new DelegateFlowGuardrail(
            "egress-confirm", "Asks the user before anything leaves", [GuardrailStage.ToolCall],
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return GuardrailDecision.Allow();
            }));

        var result = await RunWithinCeilingAsync(new FlowRunner(ConfirmationFlow(), runtime).RunAsync("send it"));

        Assert.Equal(FlowRunOutcome.TimedOut, result.Outcome);
        Assert.Equal(FlowMessageCodes.FlowTimedOut, result.FailureMessage?.Code);
        Assert.Empty(tools.Executed);
    }

    /// <summary>
    /// The caller stopping the run is still a cancellation, not a timeout: the two are told apart
    /// by which token fired.
    /// </summary>
    [Fact]
    public async Task CallerCancellationIsNotReportedAsATimeout()
    {
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver())
        {
            Tools = new ConfirmationToolHandler(new TaskCompletionSource<bool>().Task),
            TimeLimit = TimeSpan.FromMinutes(5)
        };
        using var caller = new CancellationTokenSource(Limit);

        var result = await RunWithinCeilingAsync(
            new FlowRunner(ConfirmationFlow(), runtime).RunAsync("send it", cancellationToken: caller.Token));

        Assert.Equal(FlowRunOutcome.Cancelled, result.Outcome);
        Assert.Null(result.FailureMessage);
    }

    /// <summary>
    /// A runtime the host did not configure still carries a limit, so no run is unbounded by
    /// default; a flow that finishes inside it completes normally.
    /// </summary>
    [Fact]
    public async Task ARuntimeHasADefaultLimitAndAQuickFlowCompletes()
    {
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver())
        {
            Tools = new RecordingToolHandler().Register("send-mail", "Sends an e-mail", _ => "sent")
        };

        var result = await new FlowRunner(ConfirmationFlow(), runtime).RunAsync("send it");

        Assert.Equal(FlowRuntime.DefaultTimeLimit, runtime.TimeLimit);
        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.Equal("sent", result.Output);
    }

    private static async Task<FlowRunResult> RunWithinCeilingAsync(Task<FlowRunResult> run)
    {
        var finished = await Task.WhenAny(run, Task.Delay(Ceiling));
        Assert.Same(run, finished);
        return await run;
    }

    private static FlowDefinition ConfirmationFlow() => new()
    {
        Id = "confirm",
        Name = "Send after confirmation",
        StartNodeId = "send",
        Nodes =
        [
            new FlowNode { Id = "send", Kind = FlowNodeKind.Tool, Name = "Send e-mail", ToolName = "send-mail" },
            new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
        ],
        Edges = [new FlowEdge { Id = "e", FromNodeId = "send", ToNodeId = "end" }]
    };

    /// <summary>
    /// A tool that waits for a host confirmation before acting, and — like a careless host — never
    /// looks at its cancellation token while it waits.
    /// </summary>
    /// <param name="confirmation">Completes when the host answers; never, in these tests.</param>
    private sealed class ConfirmationToolHandler(Task<bool> confirmation) : IToolHandler
    {
        /// <inheritdoc/>
        public IReadOnlyList<ToolDefinition> ToolDefinitions { get; } =
        [
            new ToolDefinition
            {
                Name = "send-mail",
                Description = "Sends an e-mail once the user confirms",
                ParametersSchema = """{"type":"object","properties":{"input":{"type":"string"}}}"""
            }
        ];

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
        {
            var confirmed = await confirmation;
            return new ToolResult { ToolCallId = toolCall.Id, Content = confirmed ? "sent" : "declined" };
        }
    }
}
