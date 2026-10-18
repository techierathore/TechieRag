namespace TechieRag.Models;

/// <summary>How a provider maps the provider-neutral reasoning controls in
/// <see cref="LlmCompletionOptions"/> onto its own request body.</summary>
public enum ReasoningControlStyle
{
    /// <summary>No provider-specific mapping; send standard OpenAI-style fields where applicable.</summary>
    Default,

    /// <summary>Venice AI's <c>venice_parameters</c> object.</summary>
    Venice
}
