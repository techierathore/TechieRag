namespace TechieRag.Llm;

/// <summary>
/// Works out which LLM service a model name belongs to (REQ-RAG-034).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets an application store one string — "the model" — and get a working
/// provider, instead of storing a provider enum, an endpoint and a model and keeping the three in
/// agreement.</para>
/// <para><b>Two forms, in this order:</b></para>
/// <list type="number">
/// <item><description><c>connector/model</c> — explicit and always wins. Split on the <i>first</i>
/// slash only, so <c>openrouter/anthropic/claude-sonnet-4-5</c> routes to OpenRouter with the model
/// id <c>anthropic/claude-sonnet-4-5</c>, which is exactly what OpenRouter expects.</description></item>
/// <item><description>Bare model name — matched against the unambiguous prefixes in
/// <see cref="LlmConnectorCatalog"/>, longest prefix first so <c>open-mistral-nemo</c> cannot be
/// captured by a shorter competing prefix.</description></item>
/// </list>
/// <para><b>Endpoint on the route:</b> <c>connector/model@https://host/v1</c> carries the service's base URL
/// with the model, which the generic <c>openai-compatible</c> and <c>anthropic-compatible</c> connectors need
/// (REQ-RAG-130) and any other connector accepts. It wins over an endpoint the app passes separately.</para>
/// <para><b>Refusing to guess is a feature.</b> <c>llama-3.3-70b</c> is served by Groq, Together,
/// Fireworks, Cerebras and a local Ollama, and picking one would mean silently sending a user's
/// prompt — and their API key — to a service they never named. Ambiguous names resolve to null and
/// <see cref="Require"/> throws with an explanation, so the caller has to say which.</para>
/// </remarks>
public static class ModelRouter
{
    /// <summary>
    /// Resolves a model name to a service, or null when it cannot be resolved without guessing.
    /// </summary>
    /// <param name="modelName">A bare model name, <c>connector/model</c>, or <c>connector/model@https://host</c>.</param>
    /// <returns>The route, or null when the name is blank or matches no unambiguous prefix.</returns>
    /// <exception cref="ArgumentException">The endpoint after <c>@</c> is not an absolute http or https URL.</exception>
    public static ModelRoute? Resolve(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return null;

        var trimmed = modelName.Trim();

        var slash = trimmed.IndexOf('/');
        if (slash > 0)
        {
            var connector = LlmConnectorCatalog.Find(trimmed[..slash]);
            if (connector is not null)
            {
                var (remainder, endpoint) = SplitEndpoint(trimmed[(slash + 1)..]);
                var modelId = string.IsNullOrWhiteSpace(remainder) ? connector.DefaultModel : remainder;
                if (string.IsNullOrWhiteSpace(modelId)) return null;

                return endpoint is null
                    ? new ModelRoute(connector, modelId)
                    : new ModelRoute(LlmProviderFactory.WithEndpoint(connector, endpoint), modelId) { EndpointFromRoute = true };
            }
        }

        LlmConnectorDescriptor? best = null;
        var bestPrefixLength = 0;

        foreach (var connector in LlmConnectorCatalog.All)
        {
            foreach (var prefix in connector.ModelPrefixes)
            {
                if (prefix.Length <= bestPrefixLength) continue;
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                best = connector;
                bestPrefixLength = prefix.Length;
            }
        }

        return best is null ? null : new ModelRoute(best, trimmed);
    }

    /// <summary>
    /// Splits <c>model@https://host/path</c> into the model id and the endpoint (REQ-RAG-130). Only an
    /// <c>@</c> followed by <c>http://</c> or <c>https://</c> starts an endpoint, so a model id that holds
    /// an <c>@</c> of its own (<c>claude-3@20240229</c>) is left whole.
    /// </summary>
    /// <param name="remainder">The part of the route after the connector.</param>
    /// <returns>The model id and the endpoint, or null for no endpoint.</returns>
    private static (string Model, string? Endpoint) SplitEndpoint(string remainder)
    {
        var at = remainder.IndexOf("@https://", StringComparison.OrdinalIgnoreCase);
        if (at < 0) at = remainder.IndexOf("@http://", StringComparison.OrdinalIgnoreCase);

        return at < 0 ? (remainder, null) : (remainder[..at], remainder[(at + 1)..].Trim());
    }

    /// <summary>
    /// Resolves a model name, throwing when it is ambiguous or unknown.
    /// </summary>
    /// <param name="modelName">A bare model name, or <c>connector/model</c>.</param>
    /// <returns>The route.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the name matches no connector; the
    /// message explains how to disambiguate.</exception>
    public static ModelRoute Require(string modelName) =>
        Resolve(modelName)
        ?? throw new InvalidOperationException(
            $"Model '{modelName}' does not identify one LLM service. Open-weight model names are served by "
            + "several providers, so name the service explicitly as 'connector/model' "
            + $"(known connectors: {string.Join(", ", LlmConnectorCatalog.All.Select(c => c.Name))}).");
}
