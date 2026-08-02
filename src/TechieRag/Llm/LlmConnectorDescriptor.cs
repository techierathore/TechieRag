namespace TechieRag.Llm;

/// <summary>
/// A named LLM service and everything needed to reach it (REQ-RAG-034).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Most hosted LLM services are OpenAI-compatible and differ only in base URL
/// and model names. Describing them as <i>data</i> rather than as a class each means adding Groq or
/// Fireworks is a catalog entry, not a new provider implementation, a new enum member, and a new
/// switch arm. The three services with genuinely different wire protocols — Anthropic, Google
/// Gemini and the local runtimes — still point at their own implementations through
/// <see cref="Source"/>.</para>
/// <para><b>Model prefixes:</b> <see cref="ModelPrefixes"/> lists only prefixes that identify this
/// service <i>unambiguously</i>. Open-weight names such as <c>llama-3.3-70b</c> are served by half a
/// dozen providers, so they belong to none of them and must be selected explicitly with the
/// <c>connector/model</c> form.</para>
/// </remarks>
public sealed record LlmConnectorDescriptor
{
    /// <summary>Gets the lowercase connector key, e.g. <c>groq</c>. Also the <c>connector/model</c> prefix.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the human-readable service name, e.g. <c>Groq</c>.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the provider implementation used to talk to this service.</summary>
    public required LlmSource Source { get; init; }

    /// <summary>Gets the default base endpoint, or null when the caller must supply one.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets the model-name prefixes that identify this service beyond doubt.</summary>
    /// <remarks>Empty for aggregators and multi-vendor hosts, which can only be selected explicitly.</remarks>
    public IReadOnlyList<string> ModelPrefixes { get; init; } = [];

    /// <summary>Gets a representative model, used when a caller names the connector but no model.</summary>
    public string? DefaultModel { get; init; }

    /// <summary>Gets whether an API key is required to use this service.</summary>
    /// <remarks>False for local runtimes such as Ollama and LM Studio.</remarks>
    public bool RequiresApiKey { get; init; } = true;
}
