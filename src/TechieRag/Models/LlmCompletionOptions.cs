using System;

namespace TechieRag.Models;

/// <summary>Configuration options for LLM completion requests.</summary>
public class LlmCompletionOptions
{
    /// <summary>Gets or sets the sampling temperature (0.0 - 2.0).</summary>
    public float? Temperature { get; set; }

    /// <summary>Gets or sets the maximum number of output tokens.</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Gets or sets the top-p (nucleus) sampling parameter.</summary>
    public float? TopP { get; set; }

    /// <summary>Gets or sets the frequency penalty (-2.0 to 2.0).</summary>
    public float? FrequencyPenalty { get; set; }

    /// <summary>Gets or sets the presence penalty (-2.0 to 2.0).</summary>
    public float? PresencePenalty { get; set; }

    /// <summary>Gets or sets stop sequences that halt generation.</summary>
    public IReadOnlyList<string>? StopSequences { get; set; }

    /// <summary>Gets or sets the system prompt.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Gets or sets whether to force JSON output mode.</summary>
    public bool JsonMode { get; set; }

    /// <summary>Gets or sets the JSON schema for structured output.</summary>
    public string? JsonSchema { get; set; }

    /// <summary>Gets or sets tool definitions for function calling.</summary>
    public IReadOnlyList<ToolDefinition>? Tools { get; set; }

    /// <summary>Gets or sets how the LLM should handle tools.</summary>
    public string? ToolChoice { get; set; }

    /// <summary>Gets or sets a seed for reproducible generation.</summary>
    public int? Seed { get; set; }

    /// <summary>Gets or sets a per-request model override; null uses the provider's configured model.</summary>
    /// <remarks>Used by workspace LLM overrides. Honored by all built-in providers.</remarks>
    public string? Model { get; set; }

    /// <summary>Gets or sets prompt-caching controls passed through to the provider (REQ-RAG-043 / BRD-124).</summary>
    /// <remarks>Null means "say nothing about caching", which leaves each provider on its own default —
    /// automatic prefix caching for OpenAI-style services, none for Anthropic. See
    /// <see cref="PromptCacheOptions"/> for what each provider does with it.</remarks>
    public PromptCacheOptions? PromptCache { get; set; }

    /// <summary>Gets or sets a stable id for the conversation this call belongs to (REQ-RAG-109).</summary>
    /// <remarks>Sent in the service's session header when the provider was configured with one, such as
    /// <c>x-opencode-session</c> for the <c>opencode-go</c> connector; keep it the same for every turn of one
    /// conversation so the service can route and cache it. Null uses one id per provider instance.
    /// Providers without a session header ignore it.</remarks>
    public string? SessionId { get; set; }

    /// <summary>Gets or sets a caller-supplied tag that names the agent or component making this call.</summary>
    /// <remarks>The tag is copied into <see cref="TokenUsage.CallerTag"/> and the telemetry event so a
    /// host can attribute token spend to its own agents. It is never sent to the provider.</remarks>
    public string? CallerTag { get; set; }

    /// <summary>Gets or sets whether to ask the provider to disable reasoning for this call.</summary>
    /// <remarks>Mapped to the provider's own switch where one exists (e.g. Venice's
    /// <c>venice_parameters.disable_thinking</c> or OpenAI-style <c>reasoning_effort</c>). Ignored by
    /// providers that offer no reasoning control.</remarks>
    public bool DisableReasoning { get; set; }

    /// <summary>Gets or sets a cap on the number of reasoning tokens the provider may use.</summary>
    /// <remarks>Mapped to the provider's own budget field where one exists. Null means no cap.</remarks>
    public int? MaxReasoningTokens { get; set; }

    /// <summary>Gets or sets the reasoning effort level for providers that support it.</summary>
    /// <remarks>Standard values are <c>low</c>, <c>medium</c> and <c>high</c>. Mapped to the provider's
    /// own parameter where one exists; ignored otherwise.</remarks>
    public string? ReasoningEffort { get; set; }

    /// <summary>Gets or sets a per-call request timeout.</summary>
    /// <remarks>When set, the provider allows the call to run for up to this duration unless the caller's
    /// own cancellation token cancels sooner. This overrides the provider's default 120-second timeout
    /// (TR-RAG-010).</remarks>
    public TimeSpan? RequestTimeout { get; set; }
}
