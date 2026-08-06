using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Services;

namespace TechieRag.Orchestration;

/// <summary>
/// Executes a <see cref="FlowDefinition"/>: walks the graph, runs each node, applies the
/// guardrails, and records what happened (REQ-RAG-042 / BRD-123).
/// </summary>
/// <remarks>
/// <para><b>Built on the single-agent loop, not beside it.</b> An agent node runs the shipped
/// <see cref="AgentLoopRunner"/> — same tool dispatch, same iteration ceiling, same
/// <see cref="AgentStep"/> reports. There is exactly one agent loop in this library; orchestration
/// is the thing that decides which agent runs next and what it is given, and nothing more. That is
/// also why an existing consumer of <see cref="AgentLoopRunner"/> is unaffected by any of this.</para>
/// <para><b>Termination is guaranteed by construction.</b> Three independent bounds, and no run
/// depends on only one of them: <see cref="FlowValidator"/> refuses a cyclic flow unless
/// <see cref="FlowDefinition.AllowCycles"/> was set deliberately; every run is capped at
/// <see cref="FlowDefinition.MaxSteps"/> node executions regardless of shape; and each agent node's
/// own tool loop is capped by <see cref="AgentLoopRunner"/>'s iteration limit. Exhausting the step
/// budget ends the run with <see cref="FlowRunOutcome.StepBudgetExhausted"/> and a trace entry — it
/// never hangs and never throws.</para>
/// <para><b>Guardrails are not optional and not bypassable.</b> Every node's input and output pass
/// through <see cref="FlowGuardrailChain"/>, and every tool call an agent node makes passes through
/// <see cref="GuardedToolHandler"/>. The chain always begins with
/// <see cref="FlowRuntime.HostGuardrails"/>, which the flow document cannot name, disable or
/// reorder. A blocked input or output STOPS the run
/// (<see cref="FlowRunOutcome.Blocked"/>); a blocked tool call is reported to the model as an
/// unavailable tool so the agent can adapt, exactly as the app's egress gate already behaves. Both
/// appear in the trace as <see cref="AgentStepKind.GuardrailBlocked"/>; neither is ever a silent
/// skip.</para>
/// <para><b>Zero egress by default.</b> This type opens no socket, holds no
/// <c>HttpClient</c>, and reads no endpoint. The only outbound capability a flow can have is the one
/// the host handed it in <see cref="FlowAgent.LlmProvider"/> or <see cref="FlowAgent.Tools"/>. A
/// flow whose nodes do not route to a remote agent therefore contacts nothing, which
/// <c>FlowOrchestrationEgressTests</c> proves by counting bytes at a loopback listener rather than
/// by reading a flag (REQ-NFR-008).</para>
/// </remarks>
public sealed class FlowRunner
{
    private readonly FlowDefinition flow;
    private readonly FlowRuntime runtime;
    private readonly ILogger<FlowRunner> logger;
    private readonly int depth;

    /// <summary>
    /// Creates a runner for one flow on one runtime.
    /// </summary>
    /// <param name="flow">The graph to execute.</param>
    /// <param name="runtime">The host bindings: agents, guardrails, tools.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="depth">
    /// The nesting depth, stamped onto every <see cref="FlowStep"/>. Zero for a top-level run; a
    /// flow invoked as a tool by another agent passes one more than its caller's.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    public FlowRunner(FlowDefinition flow, FlowRuntime runtime, ILogger<FlowRunner>? logger = null, int depth = 0)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(runtime);

        this.flow = flow;
        this.runtime = runtime;
        this.logger = logger ?? NullLogger<FlowRunner>.Instance;
        this.depth = depth;
    }

    /// <summary>
    /// Runs the flow.
    /// </summary>
    /// <param name="input">The text the flow starts from.</param>
    /// <param name="variables">Initial flow variables, or null for none.</param>
    /// <param name="progress">
    /// Optional live sink for <see cref="FlowStep"/> reports — the same
    /// <c>IProgress&lt;AgentStep&gt;</c> channel the single-agent loop uses, so an existing trace
    /// renderer can be passed straight in. Every step is recorded in
    /// <see cref="FlowRunResult.Steps"/> whether or not a sink is supplied.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the run.</param>
    /// <returns>What the flow produced, how it got there, and why it stopped.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
    /// <remarks>
    /// Never throws for a flow-level problem. An invalid flow, an unresolvable agent, a blocked
    /// step and an exhausted budget are all outcomes on <see cref="FlowRunResult"/>, because a
    /// caller rendering a run needs to SHOW what happened, and an exception would leave it with a
    /// stack trace and no trace.
    /// </remarks>
    public async Task<FlowRunResult> RunAsync(
        string input,
        IReadOnlyDictionary<string, string>? variables = null,
        IProgress<AgentStep>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var run = new RunAccumulator(Guid.NewGuid().ToString("N"), flow.Id, progress);
        var validation = FlowValidator.Validate(flow);

        if (!validation.IsValid)
        {
            // The argument is the joined ENGLISH issue text, but a consumer never has to use it:
            // every error is also on FlowRunResult.ValidationIssues with its own stable
            // FlowValidationCodes value, which is the better thing to translate and join (REQ-RAG-050).
            return run.Fail(
                FlowMessage.Create(
                    FlowMessageCodes.FlowNotValidated,
                    "The flow did not validate, so nothing was run: {0}",
                    string.Join("; ", validation.Errors.Select(issue => issue.Message))),
                new FlowState(input),
                validation.Errors);
        }

        var state = new FlowState(input, variables);
        var current = flow.ResolveStartNode();
        PendingHandoff? pending = null;

        try
        {
            while (current is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (run.StepsExecuted >= flow.MaxSteps)
                {
                    var budgetContent = FlowMessage.Create(
                        FlowMessageCodes.StepBudgetExhausted,
                        "The flow reached its budget of {0} steps and stopped before running '{1}'.",
                        flow.MaxSteps.ToString(CultureInfo.InvariantCulture),
                        current.DisplayName);

                    var budgetFailure = FlowMessage.Create(
                        FlowMessageCodes.StepBudgetReached,
                        "Step budget of {0} exhausted.",
                        flow.MaxSteps.ToString(CultureInfo.InvariantCulture));

                    run.Report(new FlowStep
                    {
                        RunId = run.RunId,
                        Iteration = run.StepsExecuted,
                        Kind = AgentStepKind.StepBudgetExhausted,
                        NodeId = current.Id,
                        NodeName = current.DisplayName,
                        NodeKind = current.Kind,
                        Depth = depth,
                        IsSuccess = false,
                        Content = budgetContent.Text,
                        ContentMessage = budgetContent,
                        ErrorMessage = budgetFailure.Text,
                        FailureMessage = budgetFailure
                    });

                    return run.Finish(FlowRunOutcome.StepBudgetExhausted, state, current.Id, null);
                }

                var step = await ExecuteNodeAsync(current, state, pending, run, cancellationToken).ConfigureAwait(false);
                pending = null;

                if (step.Blocked is not null)
                {
                    return run.Block(state, current.Id, step.Blocked);
                }

                state.LastOutput = step.Output;
                state.IsLastStepSuccess = step.IsSuccess;
                state.NodeOutputs[current.Id] = step.Output;

                if (!string.IsNullOrWhiteSpace(current.OutputVariable))
                {
                    state.Variables[current.OutputVariable] = step.Output;
                }

                run.Report(new FlowStep
                {
                    RunId = run.RunId,
                    Iteration = run.StepsExecuted,
                    Kind = AgentStepKind.NodeCompleted,
                    NodeId = current.Id,
                    NodeName = current.DisplayName,
                    NodeKind = current.Kind,
                    Depth = depth,
                    Content = step.Output,
                    ContentMessage = step.OutputMessage,
                    IsSuccess = step.IsSuccess,
                    ErrorMessage = step.FailureReason,
                    FailureMessage = step.FailureMessage
                });

                if (current.Kind == FlowNodeKind.Terminal)
                {
                    run.Report(new FlowStep
                    {
                        RunId = run.RunId,
                        Iteration = run.StepsExecuted,
                        Kind = AgentStepKind.FlowCompleted,
                        NodeId = current.Id,
                        NodeName = current.DisplayName,
                        NodeKind = current.Kind,
                        Depth = depth,
                        Content = step.Output
                    });

                    return run.Finish(FlowRunOutcome.Completed, state, current.Id,
                        current.TerminalStatus ?? current.DisplayName);
                }

                if (current.Kind == FlowNodeKind.Handoff)
                {
                    pending = BuildHandoff(current, state, run);
                    current = flow.FindNode(current.Handoff!.TargetNodeId);
                    continue;
                }

                current = SelectNext(current, state, run);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return run.Finish(FlowRunOutcome.Cancelled, state, null, null);
        }

        // Nothing satisfied an outgoing edge: the run is over with whatever it produced. Reported as
        // a completion rather than a failure — the validator already warns about branch sets with no
        // default, so this is a shape the author was told about.
        return run.Finish(FlowRunOutcome.Completed, state, null, null);
    }

    /// <summary>Runs one node through its guardrails and produces its output.</summary>
    /// <param name="node">The node to execute.</param>
    /// <param name="state">The run state.</param>
    /// <param name="pending">A handoff whose payload this node must consume, or null.</param>
    /// <param name="run">The accumulator collecting the trace.</param>
    /// <param name="cancellationToken">Token to cancel the run.</param>
    /// <returns>The node's outcome, or a block.</returns>
    private async Task<NodeOutcome> ExecuteNodeAsync(
        FlowNode node, FlowState state, PendingHandoff? pending, RunAccumulator run, CancellationToken cancellationToken)
    {
        run.CountStep();
        run.Visit(node.Id);

        run.Report(new FlowStep
        {
            RunId = run.RunId,
            Iteration = run.StepsExecuted,
            Kind = AgentStepKind.NodeStarted,
            NodeId = node.Id,
            NodeName = node.DisplayName,
            NodeKind = node.Kind,
            Depth = depth,
            Content = pending is null ? state.LastOutput : pending.Input
        });

        var chain = await FlowGuardrailChain.BuildAsync(runtime, node, cancellationToken).ConfigureAwait(false);
        var nodeInput = pending?.Input ?? state.LastOutput;

        var inputVerdict = await chain.EvaluateAsync(
            new GuardrailContext(
                GuardrailStage.Input, node.Id, node.DisplayName, nodeInput,
                AgentId: node.AgentId, Variables: state.Variables),
            cancellationToken).ConfigureAwait(false);

        if (!inputVerdict.IsAllowed)
        {
            ReportBlock(node, inputVerdict, run);
            return NodeOutcome.Block(inputVerdict);
        }

        var produced = node.Kind switch
        {
            FlowNodeKind.Agent => await RunAgentNodeAsync(node, state, pending, chain, run, cancellationToken).ConfigureAwait(false),
            FlowNodeKind.Tool => await RunToolNodeAsync(node, state, chain, run, cancellationToken).ConfigureAwait(false),
            FlowNodeKind.Terminal => new NodeOutcome(node.Instruction ?? state.LastOutput, true, null, null),
            _ => new NodeOutcome(state.LastOutput, state.IsLastStepSuccess, null, null)
        };

        if (produced.Blocked is not null) return produced;

        var outputVerdict = await chain.EvaluateAsync(
            new GuardrailContext(
                GuardrailStage.Output, node.Id, node.DisplayName, produced.Output,
                AgentId: node.AgentId, Variables: state.Variables),
            cancellationToken).ConfigureAwait(false);

        if (outputVerdict.IsAllowed) return produced;

        ReportBlock(node, outputVerdict, run);
        return NodeOutcome.Block(outputVerdict);
    }

    /// <summary>Runs one agent turn through the library's existing agent loop.</summary>
    /// <param name="node">The agent node.</param>
    /// <param name="state">The run state.</param>
    /// <param name="pending">The handoff whose payload seeds this turn, or null.</param>
    /// <param name="chain">The node's guardrails, used to guard every tool call.</param>
    /// <param name="run">The accumulator collecting the trace.</param>
    /// <param name="cancellationToken">Token to cancel the run.</param>
    /// <returns>The agent's answer.</returns>
    private async Task<NodeOutcome> RunAgentNodeAsync(
        FlowNode node, FlowState state, PendingHandoff? pending, FlowGuardrailChain chain,
        RunAccumulator run, CancellationToken cancellationToken)
    {
        var agent = await runtime.Agents.ResolveAgentAsync(node.AgentId!, cancellationToken).ConfigureAwait(false);
        if (agent is null)
        {
            var unavailable = FlowMessage.Create(
                FlowMessageCodes.AgentUnavailable,
                "Agent '{0}' is not available on this host.",
                node.AgentId ?? string.Empty);

            var unresolvable = FlowMessage.Create(
                FlowMessageCodes.AgentUnresolvable,
                "Agent '{0}' could not be resolved.",
                node.AgentId ?? string.Empty);

            return new NodeOutcome(
                unavailable.Text, false, unresolvable.Text, null, unavailable, unresolvable);
        }

        var messages = BuildConversation(node, agent, state, pending);

        var guardedTools = new GuardedToolHandler(
            agent.Tools, chain, node, state.Variables,
            (verdict, toolCall) => ReportToolBlock(node, verdict, toolCall, run));

        var loop = new AgentLoopRunner(
            agent.LlmProvider,
            guardedTools,
            null,
            node.MaxToolCalls ?? agent.MaxToolCalls);

        var options = new LlmCompletionOptions
        {
            Temperature = agent.Temperature,
            MaxTokens = agent.MaxTokens
        };

        var inner = new NodeScopedProgress(run, node, depth);

        try
        {
            var response = await loop.RunAsync(messages, options, inner, cancellationToken).ConfigureAwait(false);
            run.AddUsage(response.Usage);
            run.RecordTranscript(node.Id, messages);

            return new NodeOutcome(response.Content ?? string.Empty, true, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent node {NodeId} failed", node.Id);

            // The failure reason stays the raw exception text — a runtime detail no resource file
            // can hold — but the sentence AROUND it is the library's, so it carries a code.
            var failed = FlowMessage.Create(
                FlowMessageCodes.AgentStepFailed, "The agent step failed: {0}", ex.Message);

            return new NodeOutcome(failed.Text, false, ex.Message, null, failed, null);
        }
    }

    /// <summary>Runs one deterministic tool node.</summary>
    /// <param name="node">The tool node.</param>
    /// <param name="state">The run state, used to expand the argument placeholders.</param>
    /// <param name="chain">The node's guardrails, applied to the call.</param>
    /// <param name="run">The accumulator collecting the trace.</param>
    /// <param name="cancellationToken">Token to cancel the run.</param>
    /// <returns>The tool's result.</returns>
    private async Task<NodeOutcome> RunToolNodeAsync(
        FlowNode node, FlowState state, FlowGuardrailChain chain, RunAccumulator run, CancellationToken cancellationToken)
    {
        if (runtime.Tools is null)
        {
            var unavailable = FlowMessage.Create(
                FlowMessageCodes.ToolHandlerMissing,
                "Tool '{0}' is not available: this runtime has no tool handler.",
                node.ToolName ?? string.Empty);

            var noHandler = FlowMessage.Create(
                FlowMessageCodes.NoToolHandlerConfigured, "No tool handler is configured.");

            return new NodeOutcome(
                unavailable.Text, false, noHandler.Text, null, unavailable, noHandler);
        }

        var arguments = state.Expand(node.ToolArgumentsJson)
            ?? System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["input"] = state.LastOutput });

        var call = new ToolCall
        {
            Id = $"{node.Id}-{run.StepsExecuted}",
            Name = node.ToolName!,
            ArgumentsJson = arguments
        };

        // The refusal is captured here rather than read back off the handler because the tool result
        // itself has nowhere to carry a code: ToolResult.ErrorMessage is a string, and it is the
        // very text a host paints under "Blocked by …" (REQ-RAG-050). One call, one callback, so the
        // capture is sequential and cannot race.
        GuardrailVerdict? refusal = null;

        var guarded = new GuardedToolHandler(
            runtime.Tools, chain, node, state.Variables,
            (verdict, toolCall) =>
            {
                refusal = verdict;
                ReportToolBlock(node, verdict, toolCall, run);
            });

        var result = await guarded.ExecuteToolAsync(call, cancellationToken).ConfigureAwait(false);
        var failure = refusal is null ? null : GuardedToolHandler.RefusalMessage(refusal);

        run.Report(new FlowStep
        {
            RunId = run.RunId,
            Iteration = run.StepsExecuted,
            Kind = AgentStepKind.ToolExecuted,
            NodeId = node.Id,
            NodeName = node.DisplayName,
            NodeKind = node.Kind,
            Depth = depth,
            ToolName = node.ToolName,
            ToolArgumentsJson = arguments,
            Content = result.Content,
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.ErrorMessage,
            FailureMessage = failure
        });

        return new NodeOutcome(result.Content, result.IsSuccess, result.ErrorMessage, null, null, failure);
    }

    /// <summary>
    /// Builds the conversation the receiving agent sees, applying the handoff contract exactly.
    /// </summary>
    /// <param name="node">The agent node being run.</param>
    /// <param name="agent">The resolved agent.</param>
    /// <param name="state">The run state.</param>
    /// <param name="pending">The handoff seeding this turn, or null for an ordinary step.</param>
    /// <returns>The messages to send.</returns>
    /// <remarks>
    /// <para><b>The receiver starts from its OWN prompt.</b> Its system prompt is always first, and
    /// the sender's is never included, so an agent behaves the same wherever a flow puts it. Only
    /// <see cref="HandoffContextMode.FullTranscript"/> adds the sender's messages, and it says so in
    /// its name.</para>
    /// <para><b>Carried variables are rendered explicitly.</b> They appear in one system note,
    /// named, so a reader of the trace can see precisely what the receiver was told. A variable not
    /// on the handoff's allowlist appears nowhere in this conversation.</para>
    /// </remarks>
    private List<ChatMessage> BuildConversation(FlowNode node, FlowAgent agent, FlowState state, PendingHandoff? pending)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(runtime.SystemPreamble))
        {
            messages.Add(ChatMessage.System(runtime.SystemPreamble));
        }

        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
        {
            messages.Add(ChatMessage.System(agent.SystemPrompt));
        }

        if (pending is not null)
        {
            if (pending.Handoff.ContextMode == HandoffContextMode.FullTranscript)
            {
                messages.AddRange(pending.Transcript);
            }

            messages.Add(ChatMessage.System(pending.Note));
            messages.Add(ChatMessage.User(pending.Input));
            return messages;
        }

        var userText = string.IsNullOrWhiteSpace(node.Instruction)
            ? state.LastOutput
            : $"{node.Instruction}\n\n{state.LastOutput}";

        messages.Add(ChatMessage.User(userText));
        return messages;
    }

    /// <summary>Assembles what crosses a handoff boundary, and records the transfer in the trace.</summary>
    /// <param name="node">The handoff node.</param>
    /// <param name="state">The run state.</param>
    /// <param name="run">The accumulator collecting the trace.</param>
    /// <returns>The payload the receiving agent will consume.</returns>
    private PendingHandoff BuildHandoff(FlowNode node, FlowState state, RunAccumulator run)
    {
        var handoff = node.Handoff!;

        var input = handoff.ContextMode switch
        {
            HandoffContextMode.OriginalInputAndLastOutput =>
                $"Original request:\n{state.OriginalInput}\n\nWhat the previous agent produced:\n{state.LastOutput}",
            _ => state.LastOutput
        };

        var carried = handoff.CarryVariables
            .Where(name => !string.IsNullOrWhiteSpace(name) && state.Variables.ContainsKey(name))
            .ToDictionary(name => name, name => state.Variables[name], StringComparer.Ordinal);

        var note = BuildHandoffNote(node, handoff, carried);

        // The transcript is only read for FullTranscript; it is looked up rather than always
        // assembled so the narrow modes cost nothing.
        IReadOnlyList<ChatMessage> transcript = handoff.ContextMode == HandoffContextMode.FullTranscript
            ? run.TranscriptBefore(node.Id, flow)
            : Array.Empty<ChatMessage>();

        var summary = carried.Count == 0
            ? FlowMessage.Create(
                FlowMessageCodes.HandoffNoVariables,
                "{0}: {1} characters, no variables carried",
                handoff.ContextMode.ToString(),
                input.Length.ToString(CultureInfo.InvariantCulture))
            : FlowMessage.Create(
                FlowMessageCodes.HandoffCarryingVariables,
                "{0}: {1} characters, carrying {2}",
                handoff.ContextMode.ToString(),
                input.Length.ToString(CultureInfo.InvariantCulture),
                string.Join(", ", carried.Keys));

        run.Report(new FlowStep
        {
            RunId = run.RunId,
            Iteration = run.StepsExecuted,
            Kind = AgentStepKind.HandoffPerformed,
            NodeId = node.Id,
            NodeName = node.DisplayName,
            NodeKind = node.Kind,
            FromNodeId = node.Id,
            ToNodeId = handoff.TargetNodeId,
            Depth = depth,
            Content = summary.Text,
            ContentMessage = summary
        });

        return new PendingHandoff(handoff, input, note, transcript);
    }

    /// <summary>Renders the system note that tells the receiver what it has been handed and why.</summary>
    /// <param name="node">The handoff node.</param>
    /// <param name="handoff">The transfer being performed.</param>
    /// <param name="carried">The variables crossing, already filtered to the allowlist.</param>
    /// <returns>The note text.</returns>
    private static string BuildHandoffNote(FlowNode node, FlowHandoff handoff, IReadOnlyDictionary<string, string> carried)
    {
        var note = $"Control has been handed to you by '{node.DisplayName}'.";

        if (!string.IsNullOrWhiteSpace(handoff.Reason))
        {
            note += $" Reason: {handoff.Reason}";
        }

        if (carried.Count > 0)
        {
            note += "\nContext carried with the handoff:\n"
                + string.Join("\n", carried.Select(pair => $"- {pair.Key}: {pair.Value}"));
        }

        return note;
    }

    /// <summary>Picks the outgoing edge to follow and records the choice.</summary>
    /// <param name="node">The node being left.</param>
    /// <param name="state">The run state the conditions read.</param>
    /// <param name="run">The accumulator collecting the trace.</param>
    /// <returns>The next node, or null when nothing matched.</returns>
    private FlowNode? SelectNext(FlowNode node, FlowState state, RunAccumulator run)
    {
        foreach (var edge in flow.EdgesFrom(node.Id))
        {
            if (edge.Condition is not null && !edge.Condition.IsSatisfiedBy(state)) continue;

            var next = flow.FindNode(edge.ToNodeId);
            if (next is null) continue;

            var routeLabel = FlowMessage.Create(
                FlowMessageCodes.RouteToNode, "{0} to {1}", node.DisplayName, next.DisplayName);

            run.Report(new FlowStep
            {
                RunId = run.RunId,
                Iteration = run.StepsExecuted,
                Kind = AgentStepKind.RouteTaken,
                NodeId = node.Id,
                NodeName = node.DisplayName,
                NodeKind = node.Kind,
                FromNodeId = node.Id,
                ToNodeId = next.Id,
                EdgeId = edge.Id,
                Depth = depth,
                Content = edge.Label ?? routeLabel.Text,

                // Only the generated label carries a code. An author's own edge label is their text
                // in their language and must not be treated as a string this library owns.
                ContentMessage = edge.Label is null ? routeLabel : null
            });

            return next;
        }

        return null;
    }

    /// <summary>Records a guardrail refusal of a node's input or output.</summary>
    /// <param name="node">The node that was refused.</param>
    /// <param name="verdict">The refusal.</param>
    /// <param name="run">The accumulator collecting the trace.</param>
    private void ReportBlock(FlowNode node, GuardrailVerdict verdict, RunAccumulator run)
    {
        var framing = FlowMessage.Create(
            FlowMessageCodes.NodeBlockedByGuardrail,
            "Blocked by guardrail '{0}' at {1}.",
            verdict.GuardrailId ?? string.Empty,
            verdict.Stage.ToString());

        run.Report(new FlowStep
        {
            RunId = run.RunId,
            Iteration = run.StepsExecuted,
            Kind = AgentStepKind.GuardrailBlocked,
            NodeId = node.Id,
            NodeName = node.DisplayName,
            NodeKind = node.Kind,
            GuardrailId = verdict.GuardrailId,
            GuardrailStage = verdict.Stage,
            Depth = depth,
            IsSuccess = false,
            Content = verdict.Reason,
            ContentMessage = verdict.Message,
            ErrorMessage = framing.Text,
            FailureMessage = framing
        });
    }

    /// <summary>Records a guardrail refusal of one tool call, which does not stop the run.</summary>
    /// <param name="node">The node whose agent asked for the tool.</param>
    /// <param name="verdict">The refusal.</param>
    /// <param name="toolCall">The call that was refused.</param>
    /// <param name="run">The accumulator collecting the trace.</param>
    private void ReportToolBlock(FlowNode node, GuardrailVerdict verdict, ToolCall toolCall, RunAccumulator run)
    {
        // The row a user actually reads when a flow's guardrail refuses a tool call: the title comes
        // from the guardrail id, and THIS is the detail under it (REQ-RAG-050 / BRD-91).
        var framing = FlowMessage.Create(
            FlowMessageCodes.ToolCallBlockedByGuardrail,
            "Blocked by guardrail '{0}' before '{1}' ran.",
            verdict.GuardrailId ?? string.Empty,
            toolCall.Name);

        run.Report(new FlowStep
        {
            RunId = run.RunId,
            Iteration = run.StepsExecuted,
            Kind = AgentStepKind.GuardrailBlocked,
            NodeId = node.Id,
            NodeName = node.DisplayName,
            NodeKind = node.Kind,
            GuardrailId = verdict.GuardrailId,
            GuardrailStage = verdict.Stage,
            ToolName = toolCall.Name,
            ToolArgumentsJson = toolCall.ArgumentsJson,
            Depth = depth,
            IsSuccess = false,
            Content = verdict.Reason,
            ContentMessage = verdict.Message,
            ErrorMessage = framing.Text,
            FailureMessage = framing
        });
    }

    /// <summary>What one node produced, or the guardrail that refused it.</summary>
    /// <param name="Output">The node's output text.</param>
    /// <param name="IsSuccess">Whether the node did what it was asked.</param>
    /// <param name="FailureReason">Why it did not, when it did not.</param>
    /// <param name="Blocked">The refusal, when a guardrail stopped it.</param>
    /// <param name="OutputMessage">The localizable form of <paramref name="Output"/>, when the runner wrote it (REQ-RAG-050).</param>
    /// <param name="FailureMessage">The localizable form of <paramref name="FailureReason"/> (REQ-RAG-050).</param>
    private sealed record NodeOutcome(
        string Output,
        bool IsSuccess,
        string? FailureReason,
        GuardrailVerdict? Blocked,
        FlowMessage? OutputMessage = null,
        FlowMessage? FailureMessage = null)
    {
        /// <summary>The stand-in output for a refusal that named no reason.</summary>
        private static readonly FlowMessage NoReasonGiven = FlowMessage.Create(
            FlowMessageCodes.BlockedByGuardrail, "Blocked by a guardrail.");

        /// <summary>Builds an outcome representing a guardrail refusal.</summary>
        /// <param name="verdict">The refusal.</param>
        /// <returns>The blocked outcome.</returns>
        public static NodeOutcome Block(GuardrailVerdict verdict) =>
            new(verdict.Reason ?? NoReasonGiven.Text,
                false,
                verdict.Reason,
                verdict,
                verdict.Message ?? (verdict.Reason is null ? NoReasonGiven : null),
                verdict.Message);
    }

    /// <summary>The context a handoff hands to the receiving agent node.</summary>
    /// <param name="Handoff">The transfer's declared terms.</param>
    /// <param name="Input">The text the receiver is given as its user message.</param>
    /// <param name="Note">The system note naming the sender, the reason, and the carried variables.</param>
    /// <param name="Transcript">The sender's messages, populated only for <see cref="HandoffContextMode.FullTranscript"/>.</param>
    private sealed record PendingHandoff(
        FlowHandoff Handoff, string Input, string Note, IReadOnlyList<ChatMessage> Transcript);

    /// <summary>
    /// Re-emits the single-agent loop's reports as flow steps attributed to a node.
    /// </summary>
    /// <param name="run">The accumulator collecting the trace.</param>
    /// <param name="node">The node whose turn is running.</param>
    /// <param name="depth">The nesting depth to stamp.</param>
    /// <remarks>
    /// Deliberately not <see cref="Progress{T}"/>: that posts to a captured synchronization context,
    /// so reports would be applied out of order and the recorded trace would describe a run that did
    /// not happen in that sequence.
    /// </remarks>
    private sealed class NodeScopedProgress(RunAccumulator run, FlowNode node, int depth) : IProgress<AgentStep>
    {
        /// <inheritdoc/>
        public void Report(AgentStep value) =>
            run.Report(FlowStep.FromAgentStep(value, run.RunId, node, depth));
    }

    /// <summary>Collects the trace, the visits, the transcripts and the token usage for one run.</summary>
    /// <param name="runId">This run's identifier.</param>
    /// <param name="flowId">The flow being run.</param>
    /// <param name="progress">The caller's live sink, or null.</param>
    private sealed class RunAccumulator(string runId, string flowId, IProgress<AgentStep>? progress)
    {
        private readonly List<FlowStep> steps = [];
        private readonly List<string> visited = [];
        private readonly Dictionary<string, List<ChatMessage>> transcripts = new(StringComparer.Ordinal);
        private readonly TokenUsage usage = new();

        /// <summary>Gets this run's identifier.</summary>
        public string RunId { get; } = runId;

        /// <summary>Gets how many nodes have been executed.</summary>
        public int StepsExecuted { get; private set; }

        /// <summary>Counts one node execution against the budget.</summary>
        public void CountStep() => StepsExecuted++;

        /// <summary>Records that a node ran.</summary>
        /// <param name="nodeId">The node that ran.</param>
        public void Visit(string nodeId) => visited.Add(nodeId);

        /// <summary>Records a step in the trace and forwards it to the caller's sink.</summary>
        /// <param name="step">The step to record.</param>
        public void Report(FlowStep step)
        {
            steps.Add(step);
            progress?.Report(step);
        }

        /// <summary>Adds one agent turn's token usage to the run total.</summary>
        /// <param name="turn">The turn's usage.</param>
        public void AddUsage(TokenUsage? turn)
        {
            if (turn is null) return;

            usage.InputTokens += turn.InputTokens;
            usage.OutputTokens += turn.OutputTokens;
            usage.CacheReadTokens += turn.CacheReadTokens;
            usage.CacheWriteTokens += turn.CacheWriteTokens;
            usage.EstimatedCostUsd += turn.EstimatedCostUsd;
            usage.ModelName = string.IsNullOrEmpty(turn.ModelName) ? usage.ModelName : turn.ModelName;
            usage.ProviderName = string.IsNullOrEmpty(turn.ProviderName) ? usage.ProviderName : turn.ProviderName;
        }

        /// <summary>Keeps one agent node's conversation, in case a later handoff carries it.</summary>
        /// <param name="nodeId">The node whose turn it was.</param>
        /// <param name="messages">The conversation as it ended.</param>
        public void RecordTranscript(string nodeId, List<ChatMessage> messages) =>
            transcripts[nodeId] = [.. messages];

        /// <summary>Finds the transcript a handoff node should carry — the most recent agent node before it.</summary>
        /// <param name="handoffNodeId">The handoff node.</param>
        /// <param name="flow">The flow, used to walk back through the visits.</param>
        /// <returns>The sender's messages, or empty when no agent has run.</returns>
        public IReadOnlyList<ChatMessage> TranscriptBefore(string handoffNodeId, FlowDefinition flow)
        {
            for (var index = visited.Count - 1; index >= 0; index--)
            {
                var candidate = visited[index];
                if (string.Equals(candidate, handoffNodeId, StringComparison.Ordinal)) continue;
                if (flow.FindNode(candidate)?.Kind != FlowNodeKind.Agent) continue;
                if (transcripts.TryGetValue(candidate, out var messages)) return messages;
            }

            return [];
        }

        /// <summary>Builds the result for a run that ended normally, or ran out of budget.</summary>
        /// <param name="outcome">How it ended.</param>
        /// <param name="state">The final state.</param>
        /// <param name="lastNodeId">The node it stopped at.</param>
        /// <param name="terminalStatus">The terminal node's outcome label, when one was reached.</param>
        /// <returns>The run result.</returns>
        public FlowRunResult Finish(FlowRunOutcome outcome, FlowState state, string? lastNodeId, string? terminalStatus) => new()
        {
            RunId = RunId,
            FlowId = flowId,
            Outcome = outcome,
            Output = state.LastOutput,
            TerminalStatus = terminalStatus,
            LastNodeId = lastNodeId ?? visited.LastOrDefault(),
            Steps = steps,
            VisitedNodeIds = visited,
            Variables = new Dictionary<string, string>(state.Variables, StringComparer.Ordinal),
            StepsExecuted = StepsExecuted,
            Usage = usage
        };

        /// <summary>Builds the result for a run a guardrail stopped.</summary>
        /// <param name="state">The final state.</param>
        /// <param name="nodeId">The node that was refused.</param>
        /// <param name="verdict">The refusal.</param>
        /// <returns>The run result.</returns>
        public FlowRunResult Block(FlowState state, string nodeId, GuardrailVerdict verdict) => new()
        {
            RunId = RunId,
            FlowId = flowId,
            Outcome = FlowRunOutcome.Blocked,
            Output = state.LastOutput,
            LastNodeId = nodeId,
            Steps = steps,
            VisitedNodeIds = visited,
            Variables = new Dictionary<string, string>(state.Variables, StringComparer.Ordinal),
            StepsExecuted = StepsExecuted,
            BlockedByGuardrailId = verdict.GuardrailId,
            BlockReason = verdict.Reason,
            BlockMessage = verdict.Message,
            Usage = usage
        };

        /// <summary>Builds the result for a flow that never started.</summary>
        /// <param name="reason">Why it could not run, as a code plus its arguments.</param>
        /// <param name="state">The state it would have started from.</param>
        /// <param name="issues">The validation errors.</param>
        /// <returns>The run result.</returns>
        public FlowRunResult Fail(FlowMessage reason, FlowState state, IReadOnlyList<FlowValidationIssue> issues) => new()
        {
            RunId = RunId,
            FlowId = flowId,
            Outcome = FlowRunOutcome.Failed,
            Output = null,
            Steps = steps,
            VisitedNodeIds = visited,
            Variables = new Dictionary<string, string>(state.Variables, StringComparer.Ordinal),
            StepsExecuted = StepsExecuted,
            FailureReason = reason.Text,
            FailureMessage = reason,
            ValidationIssues = issues,
            Usage = usage
        };
    }
}
