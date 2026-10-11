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

    /// <summary>
    /// Gets whether the caller must give the endpoint, because the connector stands for any service that speaks
    /// one wire format (<c>openai-compatible</c>, <c>anthropic-compatible</c>) rather than for one service
    /// (REQ-RAG-130). The endpoint comes from the app (its Base URL) or from the route
    /// (<c>openai-compatible/&lt;model&gt;@https://host/v1</c>); without one, creating a provider is refused.
    /// </summary>
    public bool RequiresEndpoint { get; init; }

    /// <summary>Gets whether the service runs on the user's own machine or network (Ollama, LM Studio, the in-process model).</summary>
    public bool IsLocal { get; init; }

    /// <summary>
    /// Gets a model to show in an example route when the connector has no <see cref="DefaultModel"/>, e.g.
    /// <c>llama-3.3-70b-versatile</c> for Groq (REQ-RAG-132). Display only; never sent unless the caller names it.
    /// </summary>
    public string? ExampleModel { get; init; }

    /// <summary>Gets an endpoint to show in the example route of a connector that <see cref="RequiresEndpoint"/>.</summary>
    public string? ExampleEndpoint { get; init; }

    /// <summary>
    /// Gets top-level fields added to every request body sent to this service, or null for none (REQ-RAG-131).
    /// Venice uses it to switch its own system prompt off:
    /// <c>{"venice_parameters":{"include_venice_system_prompt":false}}</c>. A field the library sets itself wins.
    /// </summary>
    public IReadOnlyDictionary<string, object>? RequestBodyFields { get; init; }

    /// <summary>
    /// Gets the header this service reads a per-conversation session id from, or null when it reads none
    /// (REQ-RAG-109). The id is <c>LlmCompletionOptions.SessionId</c>.
    /// </summary>
    public string? SessionHeader { get; init; }

    /// <summary>
    /// Gets the vendor's stated terms for subscription sign-in and the date they were checked, or null
    /// for a connector that is not a <see cref="LlmSource.Subscription"/> connector (REQ-RAG-070 / BRD-113).
    /// </summary>
    public SubscriptionTerms? Subscription { get; init; }
}
