namespace TechieRag.Llm;

/// <summary>
/// The service a model name resolves to, and the model id to send it (REQ-RAG-034).
/// </summary>
/// <param name="Connector">The service that will serve the request.</param>
/// <param name="ModelId">The model id as that service expects it, with any connector prefix stripped.</param>
/// <remarks>
/// <see cref="ModelId"/> is not always the string the caller passed: <c>groq/llama-3.3-70b</c>
/// routes to Groq with the model id <c>llama-3.3-70b</c>, because the prefix was addressing, not
/// part of the model's name.
/// </remarks>
public sealed record ModelRoute(LlmConnectorDescriptor Connector, string ModelId)
{
    /// <summary>Gets the endpoint the request will be sent to.</summary>
    public string? Endpoint => Connector.Endpoint;

    /// <summary>Gets the provider implementation that will serve this route.</summary>
    public LlmSource Source => Connector.Source;
}
