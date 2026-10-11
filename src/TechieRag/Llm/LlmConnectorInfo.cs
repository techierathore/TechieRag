namespace TechieRag.Llm;

/// <summary>
/// What an app screen needs to show about one connector: its name, what the user must supply and an example
/// route (REQ-RAG-132). Returned by <see cref="LlmConnectorCatalog.Describe"/>.
/// </summary>
/// <remarks>
/// Lets a provider page build its hint and its fields from the library instead of a hard-coded list that
/// falls behind each release (Lekhak's page showed lmstudio/, ollama/ and local/, and nothing for Venice or
/// OpenRouter).
/// </remarks>
public sealed record LlmConnectorInfo
{
    /// <summary>Gets the connector key, the part of a route before the first slash, e.g. <c>venice</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the service's name for display, e.g. <c>Venice AI</c>.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets whether the user must give an API key.</summary>
    public required bool RequiresApiKey { get; init; }

    /// <summary>Gets whether the user must give the endpoint (a Base URL field, or an <c>@https://</c> suffix on the route).</summary>
    public required bool RequiresEndpoint { get; init; }

    /// <summary>Gets whether the user signs in to a consumer subscription instead of giving a key.</summary>
    public required bool RequiresSignIn { get; init; }

    /// <summary>Gets whether the service runs on the user's machine or network rather than as a hosted service.</summary>
    public required bool IsLocal { get; init; }

    /// <summary>Gets the endpoint used when the app gives none, or null when there is none (in-process model, generic routes).</summary>
    public string? DefaultEndpoint { get; init; }

    /// <summary>Gets one route that <see cref="ModelRouter"/> resolves to this connector, e.g. <c>venice/zai-org-glm-5-1</c>.</summary>
    public required string ExampleRoute { get; init; }
}
