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
    /// <param name="modelName">A bare model name, or <c>connector/model</c>.</param>
    /// <returns>The route, or null when the name is blank or matches no unambiguous prefix.</returns>
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
                var remainder = trimmed[(slash + 1)..];
                var modelId = string.IsNullOrWhiteSpace(remainder) ? connector.DefaultModel : remainder;
                if (!string.IsNullOrWhiteSpace(modelId)) return new ModelRoute(connector, modelId);
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
