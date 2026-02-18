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
}
