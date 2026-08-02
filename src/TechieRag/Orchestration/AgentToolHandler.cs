using System.Text.Json;
using Microsoft.Extensions.Logging;
using TechieRag.Abstractions;
using TechieRag.Models;
using TechieRag.Services;

namespace TechieRag.Orchestration;

/// <summary>
/// Exposes an agent — or a whole flow — to another agent as one callable tool (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>It is an ordinary <see cref="IToolHandler"/>, and that is the whole design.</b> A
/// calling agent cannot tell the difference between this and a delegate on <c>ToolRegistry</c> or a
/// tool from an MCP server. So it composes with <see cref="CompositeToolHandler"/> like anything
/// else — <c>new CompositeToolHandler(localTools, AgentToolHandler.ForAgent(...))</c> — the agent
/// loop needed no change, <see cref="IToolHandler"/> was not widened, and every existing behaviour
/// that applies to tools applies to this: the guardrail stage in <see cref="GuardedToolHandler"/>
/// sees it, a host's egress wrapping sees it, and a failure comes back as an unsuccessful
/// <see cref="ToolResult"/> the caller can read rather than an exception that ends its turn.</para>
/// <para><b>Recursion is bounded three ways.</b> <see cref="MaxInvocations"/> caps how many times
/// one handler may run within its lifetime — the lifetime being one turn, since a host builds these
/// per turn. A nested flow is additionally bounded by its own
/// <see cref="FlowDefinition.MaxSteps"/>, and a nested agent by the agent loop's iteration ceiling.
/// Two agents exposed to each other as tools therefore terminate; they do not have to be prevented
/// from being wired that way.</para>
/// <para><b>One string in, one string out.</b> The schema is deliberately a single <c>input</c>
/// property. A sub-agent that took a structured object would need its caller to know its internals,
/// which is the coupling agent-as-tool exists to avoid.</para>
/// </remarks>
public sealed class AgentToolHandler : IToolHandler
{
    /// <summary>The default ceiling on how many times one handler may be invoked.</summary>
    public const int DefaultMaxInvocations = 8;

    private readonly Func<string, IProgress<AgentStep>?, CancellationToken, Task<string>> invoke;
    private readonly ToolDefinition definition;
    private int invocations;

    private AgentToolHandler(
        ToolDefinition definition,
        int maxInvocations,
        Func<string, IProgress<AgentStep>?, CancellationToken, Task<string>> invoke)
    {
        this.definition = definition;
        this.invoke = invoke;
        MaxInvocations = maxInvocations;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ToolDefinition> ToolDefinitions => [definition];

    /// <summary>Gets the ceiling on how many times this handler may run.</summary>
    public int MaxInvocations { get; }

    /// <summary>Gets how many times it has run.</summary>
    public int Invocations => Volatile.Read(ref invocations);

    /// <summary>
    /// Gets or sets a live sink for the sub-agent's own steps, so a caller can render what the
    /// nested agent did as part of one trace. Null discards the inner steps.
    /// </summary>
    public IProgress<AgentStep>? InnerProgress { get; set; }

    /// <summary>
    /// Builds the JSON Schema this handler advertises.
    /// </summary>
    /// <param name="inputDescription">What the calling model should put in the <c>input</c> property.</param>
    /// <returns>A one-property object schema.</returns>
    public static string BuildSchema(string inputDescription) =>
        JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                input = new
                {
                    type = "string",
                    description = string.IsNullOrWhiteSpace(inputDescription)
                        ? "The request to hand to this agent."
                        : inputDescription
                }
            },
            required = new[] { "input" }
        });

    /// <summary>
    /// Exposes a single agent as a tool.
    /// </summary>
    /// <param name="toolName">The name the calling model sees. Must match the providers' <c>[A-Za-z0-9_-]{1,64}</c> shape.</param>
    /// <param name="description">What this agent is for — the calling model's only basis for choosing it.</param>
    /// <param name="agent">The agent to run.</param>
    /// <param name="inputDescription">What to put in the tool's <c>input</c> property.</param>
    /// <param name="maxInvocations">The ceiling on invocations; defaults to <see cref="DefaultMaxInvocations"/>.</param>
    /// <param name="loggerFactory">Optional logger factory for the inner agent loop.</param>
    /// <returns>A handler exposing exactly one tool.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="toolName"/> or <paramref name="description"/> is blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="agent"/> is null.</exception>
    /// <remarks>
    /// The sub-agent runs the same <see cref="AgentLoopRunner"/> a top-level turn runs, with its own
    /// system prompt and its own tools. It does not inherit the caller's conversation: a sub-agent
    /// that could see everything its caller had said would be a handoff wearing a tool's clothes,
    /// and the token cost would be invisible at the call site.
    /// </remarks>
    public static AgentToolHandler ForAgent(
        string toolName,
        string description,
        FlowAgent agent,
        string? inputDescription = null,
        int maxInvocations = DefaultMaxInvocations,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(agent);

        var definition = new ToolDefinition
        {
            Name = toolName,
            Description = description,
            ParametersSchema = BuildSchema(inputDescription ?? $"The request to hand to {agent.DisplayName}.")
        };

        return new AgentToolHandler(definition, maxInvocations, async (input, progress, cancellationToken) =>
        {
            var messages = new List<ChatMessage>();
            if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            {
                messages.Add(ChatMessage.System(agent.SystemPrompt));
            }

            messages.Add(ChatMessage.User(input));

            var loop = new AgentLoopRunner(
                agent.LlmProvider,
                agent.Tools,
                loggerFactory?.CreateLogger<AgentLoopRunner>(),
                agent.MaxToolCalls);

            var response = await loop.RunAsync(
                messages,
                new LlmCompletionOptions { Temperature = agent.Temperature, MaxTokens = agent.MaxTokens },
                progress,
                cancellationToken).ConfigureAwait(false);

            return response.Content ?? string.Empty;
        });
    }

    /// <summary>
    /// Exposes a whole flow as a tool, so an agent can delegate to a multi-step graph.
    /// </summary>
    /// <param name="toolName">The name the calling model sees.</param>
    /// <param name="description">What the flow does.</param>
    /// <param name="flow">The flow to run.</param>
    /// <param name="runtime">The bindings the flow runs on.</param>
    /// <param name="inputDescription">What to put in the tool's <c>input</c> property.</param>
    /// <param name="maxInvocations">The ceiling on invocations; defaults to <see cref="DefaultMaxInvocations"/>.</param>
    /// <param name="depth">The nesting depth stamped on the inner flow's steps.</param>
    /// <param name="loggerFactory">Optional logger factory for the inner runner.</param>
    /// <returns>A handler exposing exactly one tool.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="toolName"/> or <paramref name="description"/> is blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <remarks>
    /// A blocked or budget-exhausted inner run comes back as an UNSUCCESSFUL tool result naming what
    /// happened, not as a plausible-looking answer. A calling agent that cannot tell "the sub-flow
    /// was refused" from "the sub-flow replied" will confidently report the refusal as a finding.
    /// </remarks>
    public static AgentToolHandler ForFlow(
        string toolName,
        string description,
        FlowDefinition flow,
        FlowRuntime runtime,
        string? inputDescription = null,
        int maxInvocations = DefaultMaxInvocations,
        int depth = 1,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(runtime);

        var definition = new ToolDefinition
        {
            Name = toolName,
            Description = description,
            ParametersSchema = BuildSchema(inputDescription ?? $"The request to hand to the '{flow.Name}' flow.")
        };

        return new AgentToolHandler(definition, maxInvocations, async (input, progress, cancellationToken) =>
        {
            var runner = new FlowRunner(flow, runtime, loggerFactory?.CreateLogger<FlowRunner>(), depth);
            var result = await runner.RunAsync(input, null, progress, cancellationToken).ConfigureAwait(false);

            return result.Outcome switch
            {
                FlowRunOutcome.Completed => result.Output ?? string.Empty,
                FlowRunOutcome.Blocked =>
                    $"unavailable: the '{flow.Name}' flow was stopped by guardrail '{result.BlockedByGuardrailId}'. {result.BlockReason}",
                FlowRunOutcome.StepBudgetExhausted =>
                    $"unavailable: the '{flow.Name}' flow reached its {flow.MaxSteps}-step budget without finishing.",
                _ => $"unavailable: the '{flow.Name}' flow did not complete. {result.FailureReason}"
            };
        });
    }

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        if (!string.Equals(toolCall.Name, definition.Name, StringComparison.OrdinalIgnoreCase))
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error: Unknown tool '{toolCall.Name}'",
                IsSuccess = false,
                ErrorMessage = $"This handler exposes only '{definition.Name}'"
            };
        }

        if (Interlocked.Increment(ref invocations) > MaxInvocations)
        {
            // The recursion bound. Reported to the model rather than thrown so the calling agent can
            // say it ran out of delegations and answer with what it has.
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"unavailable: '{definition.Name}' has already been called {MaxInvocations} times this turn and will not run again.",
                IsSuccess = false,
                ErrorMessage = $"Invocation limit of {MaxInvocations} reached for '{definition.Name}'"
            };
        }

        var input = ReadInput(toolCall.ArgumentsJson);
        if (input is null)
        {
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = "Error: the call must supply a string 'input' property.",
                IsSuccess = false,
                ErrorMessage = "Missing or non-string 'input' argument"
            };
        }

        try
        {
            var answer = await invoke(input, InnerProgress, cancellationToken).ConfigureAwait(false);
            return new ToolResult { ToolCallId = toolCall.Id, Content = answer };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Same contract as every other tool handler: a failure is a message the model can read.
            return new ToolResult
            {
                ToolCallId = toolCall.Id,
                Content = $"Error executing tool: {ex.Message}",
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>Reads the <c>input</c> property out of a call's arguments.</summary>
    /// <param name="argumentsJson">The arguments the model produced.</param>
    /// <returns>The input text, or null when the arguments do not carry a string <c>input</c>.</returns>
    private static string? ReadInput(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            return document.RootElement.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.String
                ? input.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
