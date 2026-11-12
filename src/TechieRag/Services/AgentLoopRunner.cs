using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Services;

/// <summary>
/// Runs the complete agent tool-calling loop until the LLM produces a final answer.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Manages the multi-turn tool execution cycle where the LLM
/// can call tools, receive results, and continue generating until it produces
/// a text response (not a tool call).</para>
/// <para><b>Code Flow:</b></para>
/// <list type="number">
/// <item>Send messages + tool definitions to LLM</item>
/// <item>If LLM returns tool_calls: execute each tool via IToolHandler</item>
/// <item>Add tool results to messages</item>
/// <item>Send updated messages back to LLM</item>
/// <item>Repeat until LLM returns text (no tool calls) or max iterations reached</item>
/// </list>
/// <para><b>Tool requests written as text.</b> A model that writes its tool request in the answer text (bare JSON,
/// a code fence or <c>&lt;tool_call&gt;</c> tags) naming an offered tool gets it run as a tool call, exactly as a
/// structured call (REQ-RAG-159).</para>
/// <para><b>Safety:</b> Configurable max iterations to prevent infinite loops.</para>
/// </remarks>
public class AgentLoopRunner
{
    private readonly ILlmProvider llmProvider;
    private readonly IToolHandler toolHandler;
    private readonly ILogger<AgentLoopRunner> logger;
    private readonly int maxIterations;

    /// <summary>
    /// Creates a new agent loop runner.
    /// </summary>
    /// <param name="llmProvider">The LLM provider to use for generation.</param>
    /// <param name="toolHandler">The tool handler for executing tool calls.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="maxIterations">Maximum tool-call iterations before stopping (default: 10).</param>
    public AgentLoopRunner(
        ILlmProvider llmProvider,
        IToolHandler toolHandler,
        ILogger<AgentLoopRunner>? logger = null,
        int maxIterations = 10)
    {
        ArgumentNullException.ThrowIfNull(llmProvider);
        ArgumentNullException.ThrowIfNull(toolHandler);

        this.llmProvider = llmProvider;
        this.toolHandler = toolHandler;
        this.logger = logger ?? NullLogger<AgentLoopRunner>.Instance;
        this.maxIterations = maxIterations;
    }

    /// <summary>
    /// Runs the agent loop with the given messages and returns the final response.
    /// </summary>
    /// <param name="messages">Initial conversation messages.</param>
    /// <param name="options">LLM completion options (tools are added automatically).</param>
    /// <param name="progress">
    /// Optional sink that receives an <see cref="AgentStep"/> for each tool-call request,
    /// each individual tool execution, and the final answer — letting callers render an
    /// execution trace of what the agent did. Pass null to ignore step reporting.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The final LLM response after all tool calls are resolved.</returns>
    public async Task<LlmResponse> RunAsync(
        List<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        IProgress<AgentStep>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        options ??= new LlmCompletionOptions();
        var toolOptions = BuildToolOptions(options);

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogDebug("Agent loop iteration {Iteration}/{MaxIterations}", iteration + 1, maxIterations);
            var response = await llmProvider.ChatAsync(messages, toolOptions, cancellationToken).ConfigureAwait(false);
            ReadWrittenToolCalls(response, toolOptions);

            if (!response.HasToolCalls)
            {
                logger.LogDebug("Agent loop completed after {Iterations} iteration(s)", iteration + 1);
                progress?.Report(new AgentStep
                {
                    Iteration = iteration + 1,
                    Kind = AgentStepKind.FinalAnswer,
                    Content = response.Content
                });
                return response;
            }

            progress?.Report(new AgentStep
            {
                Iteration = iteration + 1,
                Kind = AgentStepKind.ToolCallRequested,
                ToolName = string.Join(", ", response.ToolCalls!.Select(c => c.Name))
            });

            // Add assistant message with tool calls to history
            messages.Add(response.ToChatMessage());

            // Execute each tool call
            foreach (var toolCall in response.ToolCalls!)
            {
                await ExecuteToolAsync(toolCall, iteration + 1, messages, progress, cancellationToken).ConfigureAwait(false);
            }
        }

        // Max iterations reached - force a final response without tools
        logger.LogWarning("Agent loop reached max iterations ({MaxIterations}). Forcing final answer.", maxIterations);
        var finalResponse = await llmProvider.ChatAsync(messages, BuildFinalOptions(options), cancellationToken).ConfigureAwait(false);
        progress?.Report(new AgentStep
        {
            Iteration = maxIterations,
            Kind = AgentStepKind.MaxIterationsReached,
            Content = finalResponse.Content
        });
        return finalResponse;
    }

    /// <summary>
    /// Runs the agent loop and streams it: assistant text as it arrives, each tool call and its
    /// result, then the final response (REQ-RAG-068 / BRD-111).
    /// </summary>
    /// <param name="messages">Initial conversation messages; assistant and tool messages are appended as the run proceeds, exactly as <see cref="RunAsync"/> does.</param>
    /// <param name="options">LLM completion options (tools are added automatically).</param>
    /// <param name="progress">Optional trace sink; receives the same <see cref="AgentStep"/>s as <see cref="RunAsync"/>.</param>
    /// <param name="cancellationToken">Token to cancel the run.</param>
    /// <returns>
    /// <see cref="AgentStreamEventKind.TextDelta"/> events as the model writes, a
    /// <see cref="AgentStreamEventKind.ToolCallRequested"/> and a <see cref="AgentStreamEventKind.ToolExecuted"/>
    /// per tool call, and one <see cref="AgentStreamEventKind.Completed"/> last.
    /// </returns>
    /// <remarks>
    /// Every model call goes through <see cref="ILlmProvider.ChatStreamEventsAsync"/>, so text is
    /// yielded as it arrives even in an iteration that ends in tool calls. Every tool call runs
    /// through the <see cref="IToolHandler"/> given to the constructor (a <see cref="ToolRegistry"/>,
    /// a composite, a guarded handler) exactly as in the non-streaming loop, in the order the model
    /// asked for them.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is null.</exception>
    public async IAsyncEnumerable<AgentStreamEvent> RunStreamAsync(
        List<ChatMessage> messages,
        LlmCompletionOptions? options = null,
        IProgress<AgentStep>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        options ??= new LlmCompletionOptions();
        var toolOptions = BuildToolOptions(options);
        var totalUsage = new TokenUsage { ModelName = llmProvider.ModelName, ProviderName = llmProvider.Name };

        for (int iteration = 1; iteration <= maxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogDebug("Streaming agent loop iteration {Iteration}/{MaxIterations}", iteration, maxIterations);

            // Text that may be a tool request written as text is held back until the turn ends, so a request the
            // loop then runs is never shown as answer text (REQ-RAG-159, Sevak TR-RAG-062).
            var turn = new StreamedTurn();
            var held = new System.Text.StringBuilder();
            var holding = true;
            await foreach (var streamEvent in llmProvider.ChatStreamEventsAsync(messages, toolOptions, cancellationToken).ConfigureAwait(false))
            {
                var text = turn.Apply(streamEvent);
                if (text is null)
                {
                    continue;
                }

                if (holding)
                {
                    held.Append(text);
                    if (TextToolCallReader.MayBeToolCall(held.ToString()))
                    {
                        continue;
                    }

                    holding = false;
                    text = held.ToString();
                    held.Clear();
                }

                yield return new AgentStreamEvent { Kind = AgentStreamEventKind.TextDelta, Iteration = iteration, Text = text };
            }

            var response = turn.ToResponse(llmProvider);
            AddUsage(totalUsage, response.Usage);
            ReadWrittenToolCalls(response, toolOptions);
            if (held.Length > 0 && !response.HasToolCalls)
            {
                yield return new AgentStreamEvent { Kind = AgentStreamEventKind.TextDelta, Iteration = iteration, Text = held.ToString() };
            }

            if (!response.HasToolCalls)
            {
                progress?.Report(new AgentStep { Iteration = iteration, Kind = AgentStepKind.FinalAnswer, Content = response.Content });
                yield return Completed(iteration, response, totalUsage, maxIterationsReached: false);
                yield break;
            }

            progress?.Report(new AgentStep
            {
                Iteration = iteration,
                Kind = AgentStepKind.ToolCallRequested,
                ToolName = string.Join(", ", response.ToolCalls!.Select(c => c.Name))
            });
            messages.Add(response.ToChatMessage());

            foreach (var toolCall in response.ToolCalls!)
            {
                yield return new AgentStreamEvent { Kind = AgentStreamEventKind.ToolCallRequested, Iteration = iteration, ToolCall = toolCall };
                var result = await ExecuteToolAsync(toolCall, iteration, messages, progress, cancellationToken).ConfigureAwait(false);
                yield return new AgentStreamEvent { Kind = AgentStreamEventKind.ToolExecuted, Iteration = iteration, ToolCall = toolCall, ToolResult = result };
            }
        }

        logger.LogWarning("Streaming agent loop reached max iterations ({MaxIterations}). Forcing final answer.", maxIterations);
        var finalTurn = new StreamedTurn();
        await foreach (var streamEvent in llmProvider.ChatStreamEventsAsync(messages, BuildFinalOptions(options), cancellationToken).ConfigureAwait(false))
        {
            var text = finalTurn.Apply(streamEvent);
            if (text is not null)
            {
                yield return new AgentStreamEvent { Kind = AgentStreamEventKind.TextDelta, Iteration = maxIterations, Text = text };
            }
        }

        var finalResponse = finalTurn.ToResponse(llmProvider);
        AddUsage(totalUsage, finalResponse.Usage);
        progress?.Report(new AgentStep { Iteration = maxIterations, Kind = AgentStepKind.MaxIterationsReached, Content = finalResponse.Content });
        yield return Completed(maxIterations, finalResponse, totalUsage, maxIterationsReached: true);
    }

    /// <summary>
    /// Turns a tool request the model wrote as text (bare JSON, a code fence or <c>&lt;tool_call&gt;</c> tags) into
    /// structured calls, so it runs through the same handler, iteration limit and guardrails as a structured call
    /// instead of ending the loop as the answer (REQ-RAG-159, Sevak TR-RAG-062). A response that already has
    /// structured calls, or whose text names no offered tool, is left as it is.
    /// </summary>
    /// <param name="response">The model's response; its tool calls are set and its text cleared when a request is read.</param>
    /// <param name="toolOptions">The options the call was made with, which name the offered tools.</param>
    private void ReadWrittenToolCalls(LlmResponse response, LlmCompletionOptions toolOptions)
    {
        if (response.HasToolCalls || TextToolCallReader.Read(response.Content, toolOptions.Tools) is not { } written)
        {
            return;
        }

        logger.LogDebug("The model wrote {Count} tool request(s) as text; running them as tool calls: {Tools}",
            written.Count, string.Join(", ", written.Select(call => call.Name)));
        response.ToolCalls = written;
        response.Content = null;
        response.FinishReason = "tool_calls";
    }

    private LlmCompletionOptions BuildToolOptions(LlmCompletionOptions options) => new()
    {
        Temperature = options.Temperature,
        MaxTokens = options.MaxTokens,
        TopP = options.TopP,
        FrequencyPenalty = options.FrequencyPenalty,
        PresencePenalty = options.PresencePenalty,
        StopSequences = options.StopSequences,
        SystemPrompt = options.SystemPrompt,
        Seed = options.Seed,
        Tools = toolHandler.ToolDefinitions,
        ToolChoice = options.ToolChoice ?? "auto"
    };

    private static LlmCompletionOptions BuildFinalOptions(LlmCompletionOptions options) => new()
    {
        Temperature = options.Temperature,
        MaxTokens = options.MaxTokens,
        TopP = options.TopP,
        SystemPrompt = options.SystemPrompt
    };

    /// <summary>Executes one tool call through the handler, appends its result and reports the step.</summary>
    private async Task<ToolResult> ExecuteToolAsync(
        ToolCall toolCall,
        int iteration,
        List<ChatMessage> messages,
        IProgress<AgentStep>? progress,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Executing tool: {ToolName} (iteration {Iteration})", toolCall.Name, iteration);

        var result = await toolHandler.ExecuteToolAsync(toolCall, cancellationToken).ConfigureAwait(false);
        messages.Add(ChatMessage.Tool(result.ToolCallId, result.Content));

        if (!result.IsSuccess)
        {
            logger.LogWarning("Tool {ToolName} failed: {Error}", toolCall.Name, result.ErrorMessage);
        }

        progress?.Report(new AgentStep
        {
            Iteration = iteration,
            Kind = AgentStepKind.ToolExecuted,
            ToolName = toolCall.Name,
            ToolArgumentsJson = toolCall.ArgumentsJson,
            Content = result.Content,
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.ErrorMessage,
            // The handler's coded refusal, so a renderer can translate this row's detail
            // line instead of painting the English fallback (REQ-RAG-050 / REQ-RAG-051).
            FailureMessage = result.Message
        });

        return result;
    }

    private static void AddUsage(TokenUsage total, TokenUsage call)
    {
        total.InputTokens += call.InputTokens;
        total.OutputTokens += call.OutputTokens;
        total.CacheReadTokens += call.CacheReadTokens;
        total.CacheWriteTokens += call.CacheWriteTokens;
        total.EstimatedCostUsd += call.EstimatedCostUsd;
    }

    private static AgentStreamEvent Completed(int iteration, LlmResponse response, TokenUsage totalUsage, bool maxIterationsReached)
    {
        response.Usage = totalUsage;
        return new AgentStreamEvent
        {
            Kind = AgentStreamEventKind.Completed,
            Iteration = iteration,
            Response = response,
            MaxIterationsReached = maxIterationsReached
        };
    }

    /// <summary>Collects one streamed model call back into the shape the loop reasons about.</summary>
    private sealed class StreamedTurn
    {
        private readonly System.Text.StringBuilder text = new();
        private readonly System.Text.StringBuilder thinking = new();
        private readonly List<ToolCall> toolCalls = new();
        private LlmStreamEvent? completed;

        /// <summary>Folds one event in; returns the text fragment to forward, or null.</summary>
        public string? Apply(LlmStreamEvent streamEvent)
        {
            switch (streamEvent.Kind)
            {
                case LlmStreamEventKind.TextDelta when !string.IsNullOrEmpty(streamEvent.Text):
                    text.Append(streamEvent.Text);
                    return streamEvent.Text;
                case LlmStreamEventKind.ToolCall when streamEvent.ToolCall is not null:
                    toolCalls.Add(streamEvent.ToolCall);
                    return null;
                case LlmStreamEventKind.Thinking:
                    thinking.Append(streamEvent.Text);
                    return null;
                case LlmStreamEventKind.Completed:
                    completed = streamEvent;
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>Builds the response for the call, as the non-streaming loop would have received it.</summary>
        public LlmResponse ToResponse(ILlmProvider provider) => new()
        {
            Content = text.Length > 0 ? text.ToString() : null,
            Thinking = thinking.Length > 0 ? thinking.ToString() : null,
            ProviderState = completed?.ProviderState,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            Usage = completed?.Usage ?? new TokenUsage { ModelName = provider.ModelName, ProviderName = provider.Name },
            FinishReason = completed?.FinishReason ?? (toolCalls.Count > 0 ? "tool_calls" : "stop"),
            ModelName = completed?.ModelName ?? provider.ModelName
        };
    }
}
