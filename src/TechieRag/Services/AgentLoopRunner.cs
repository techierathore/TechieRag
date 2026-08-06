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
        var toolOptions = new LlmCompletionOptions
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

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogDebug("Agent loop iteration {Iteration}/{MaxIterations}", iteration + 1, maxIterations);
            var response = await llmProvider.ChatAsync(messages, toolOptions, cancellationToken).ConfigureAwait(false);

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
                logger.LogInformation("Executing tool: {ToolName} (iteration {Iteration})",
                    toolCall.Name, iteration + 1);

                var result = await toolHandler.ExecuteToolAsync(toolCall, cancellationToken).ConfigureAwait(false);
                messages.Add(ChatMessage.Tool(result.ToolCallId, result.Content));

                if (!result.IsSuccess)
                {
                    logger.LogWarning("Tool {ToolName} failed: {Error}", toolCall.Name, result.ErrorMessage);
                }

                progress?.Report(new AgentStep
                {
                    Iteration = iteration + 1,
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
            }
        }

        // Max iterations reached - force a final response without tools
        logger.LogWarning("Agent loop reached max iterations ({MaxIterations}). Forcing final answer.", maxIterations);
        var finalOptions = new LlmCompletionOptions
        {
            Temperature = options.Temperature,
            MaxTokens = options.MaxTokens,
            TopP = options.TopP,
            SystemPrompt = options.SystemPrompt
        };

        var finalResponse = await llmProvider.ChatAsync(messages, finalOptions, cancellationToken).ConfigureAwait(false);
        progress?.Report(new AgentStep
        {
            Iteration = maxIterations,
            Kind = AgentStepKind.MaxIterationsReached,
            Content = finalResponse.Content
        });
        return finalResponse;
    }
}
