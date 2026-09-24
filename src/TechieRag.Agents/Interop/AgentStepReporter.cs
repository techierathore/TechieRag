using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TechieRag.Models;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace TechieRag.Agents.Interop;

/// <summary>
/// Adapter 3: Agent Framework middleware that reports a run as TechieRag <see cref="AgentStep"/>s on an
/// <see cref="IProgress{T}"/>, so an existing trace renderer shows a MAF run unchanged (REQ-RAG-017 / BRD-85).
/// </summary>
/// <remarks>
/// <para>Emits only the four loop kinds — <see cref="AgentStepKind.ToolCallRequested"/>,
/// <see cref="AgentStepKind.ToolExecuted"/>, <see cref="AgentStepKind.FinalAnswer"/> and
/// <see cref="AgentStepKind.MaxIterationsReached"/> — never a new kind (REQ-RAG-042 rule). Content the
/// four kinds cannot express (approval requests, reasoning) is not traced.</para>
/// <para>A tool served by <see cref="ToolHandlerAIFunction"/> contributes its full <see cref="ToolResult"/>:
/// success, error text and the <see cref="Orchestration.FlowMessage"/> code land in
/// <see cref="AgentStep.FailureMessage"/> exactly as the classic loop reports them.</para>
/// <para>The iteration number is the function-invoking loop's own counter, 1-based. The reporter keeps
/// one run's state at a time, so a traced agent should not run two turns concurrently.</para>
/// </remarks>
public static class AgentStepReporter
{
    /// <summary>Wraps an agent so every run reports its steps.</summary>
    /// <param name="agent">The agent.</param>
    /// <param name="progress">The trace sink.</param>
    /// <param name="maxIterations">The tool-iteration cap the agent was built with, so a run that hits it
    /// ends in <see cref="AgentStepKind.MaxIterationsReached"/>; null never reports that kind.</param>
    /// <returns>The wrapped agent.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static AIAgent WithAgentSteps(this AIAgent agent, IProgress<AgentStep> progress, int? maxIterations = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(progress);

        var run = new RunTrace(progress, maxIterations);
        return agent.AsBuilder()
            .Use(run.RunAsync, run.RunStreamingAsync)
            .Use(run.InvokeFunctionAsync)
            .Build();
    }

    /// <summary>The per-agent reporting state and the three middleware delegates.</summary>
    private sealed class RunTrace(IProgress<AgentStep> progress, int? maxIterations)
    {
        private int lastIteration;

        public async Task<AgentResponse> RunAsync(
            IEnumerable<MeaiChatMessage> messages, AgentSession? session, AgentRunOptions? options, AIAgent inner, CancellationToken cancellationToken)
        {
            lastIteration = 0;
            var response = await inner.RunAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
            ReportFinal(response.Text);
            return response;
        }

        public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
            IEnumerable<MeaiChatMessage> messages, AgentSession? session, AgentRunOptions? options, AIAgent inner,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            lastIteration = 0;
            var text = new StringBuilder();
            await foreach (var update in inner.RunStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
            {
                text.Append(update.Text);
                yield return update;
            }

            ReportFinal(text.ToString());
        }

        public async ValueTask<object?> InvokeFunctionAsync(
            AIAgent agent,
            FunctionInvocationContext context,
            Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
            CancellationToken cancellationToken)
        {
            var iteration = context.Iteration + 1;
            lastIteration = Math.Max(lastIteration, iteration);

            if (context.FunctionCallIndex == 0)
            {
                progress.Report(new AgentStep { Iteration = iteration, Kind = AgentStepKind.ToolCallRequested, ToolName = RequestedNames(context) });
            }

            var capture = ToolResultCapture.Open();
            var result = await next(context, cancellationToken).ConfigureAwait(false);
            var toolResult = capture.Value;

            progress.Report(new AgentStep
            {
                Iteration = iteration,
                Kind = AgentStepKind.ToolExecuted,
                ToolName = context.Function.Name,
                ToolArgumentsJson = ChatMessageMapper.SerializeArguments(context.Arguments),
                Content = toolResult?.Content ?? ChatMessageMapper.ResultToString(result),
                IsSuccess = toolResult?.IsSuccess ?? true,
                ErrorMessage = toolResult?.ErrorMessage,
                FailureMessage = toolResult?.Message
            });
            return result;
        }

        private void ReportFinal(string text)
        {
            var capped = maxIterations is { } cap && lastIteration >= cap;
            progress.Report(new AgentStep
            {
                Iteration = capped ? lastIteration : lastIteration + 1,
                Kind = capped ? AgentStepKind.MaxIterationsReached : AgentStepKind.FinalAnswer,
                Content = text
            });
        }

        private static string RequestedNames(FunctionInvocationContext context)
        {
            var calls = context.Messages?
                .LastOrDefault(message => message.Contents.OfType<FunctionCallContent>().Any())?
                .Contents.OfType<FunctionCallContent>()
                .Select(call => call.Name)
                .ToList();

            return calls is { Count: > 0 } ? string.Join(", ", calls) : context.Function.Name;
        }
    }
}
