namespace TechieRag.Llm;

/// <summary>
/// The library's built-in table of named LLM services (REQ-RAG-034).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a caller say "groq" or "deepseek" instead of memorising a base URL,
/// and lets <see cref="ModelRouter"/> work out which service a model name belongs to.</para>
/// <para><b>Extensible without a release:</b> a service that is OpenAI-compatible but not listed
/// here still works — <c>UseOpenAICompatibleLlm(endpoint, apiKey, model)</c> has always taken an
/// arbitrary endpoint. The catalog is a convenience over that, not a gate in front of it.</para>
/// <para><b>What is deliberately absent:</b> AWS Bedrock. Its API is not OpenAI-compatible and
/// every request must be SigV4-signed against the caller's AWS credentials, which means an AWS SDK
/// dependency. That conflicts with the library's dependency-light rule, so Bedrock is left to a
/// custom <c>ILlmProvider</c> supplied through <c>UseCustomLlmProvider</c>.</para>
/// </remarks>
public static class LlmConnectorCatalog
{
    private static readonly LlmConnectorDescriptor[] Connectors =
    [
        new()
        {
            Name = "openai",
            DisplayName = "OpenAI",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.openai.com/v1",
            ModelPrefixes = ["gpt-", "o1", "o3", "o4-", "chatgpt-"],
            DefaultModel = "gpt-4o"
        },
        new()
        {
            Name = "anthropic",
            DisplayName = "Anthropic",
            Source = LlmSource.Anthropic,
            Endpoint = "https://api.anthropic.com",
            ModelPrefixes = ["claude-"],
            DefaultModel = "claude-sonnet-4-5-20250929"
        },
        new()
        {
            Name = "gemini",
            DisplayName = "Google Gemini",
            Source = LlmSource.GoogleGemini,
            Endpoint = "https://generativelanguage.googleapis.com",
            ModelPrefixes = ["gemini-"],
            DefaultModel = "gemini-2.0-flash"
        },
        new()
        {
            Name = "mistral",
            DisplayName = "Mistral AI",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.mistral.ai/v1",
            ModelPrefixes = ["mistral-", "ministral-", "magistral-", "codestral-", "devstral-", "pixtral-", "open-mistral-", "open-mixtral-"],
            DefaultModel = "mistral-large-latest"
        },
        new()
        {
            Name = "deepseek",
            DisplayName = "DeepSeek",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.deepseek.com/v1",
            ModelPrefixes = ["deepseek-"],
            DefaultModel = "deepseek-chat"
        },
        new()
        {
            Name = "xai",
            DisplayName = "xAI",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.x.ai/v1",
            ModelPrefixes = ["grok-"],
            DefaultModel = "grok-4"
        },
        new()
        {
            Name = "cohere",
            DisplayName = "Cohere",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.cohere.ai/compatibility/v1",
            ModelPrefixes = ["command-"],
            DefaultModel = "command-r-plus"
        },
        new()
        {
            Name = "perplexity",
            DisplayName = "Perplexity",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.perplexity.ai",
            ModelPrefixes = ["sonar"],
            DefaultModel = "sonar"
        },
        // Multi-vendor hosts: they serve open-weight models whose names belong to no one service,
        // so they carry no prefixes and can only be selected as "groq/<model>" and so on.
        new()
        {
            Name = "groq",
            DisplayName = "Groq",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.groq.com/openai/v1"
        },
        new()
        {
            Name = "together",
            DisplayName = "Together AI",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.together.xyz/v1"
        },
        new()
        {
            Name = "openrouter",
            DisplayName = "OpenRouter",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://openrouter.ai/api/v1"
        },
        new()
        {
            Name = "fireworks",
            DisplayName = "Fireworks AI",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.fireworks.ai/inference/v1"
        },
        new()
        {
            Name = "cerebras",
            DisplayName = "Cerebras",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.cerebras.ai/v1"
        },
        new()
        {
            Name = "ollama",
            DisplayName = "Ollama (local)",
            Source = LlmSource.Ollama,
            Endpoint = "http://localhost:11434",
            RequiresApiKey = false
        },
        new()
        {
            Name = "lmstudio",
            DisplayName = "LM Studio (local)",
            Source = LlmSource.LmStudio,
            Endpoint = "http://localhost:1234",
            RequiresApiKey = false
        }
    ];

    /// <summary>Gets every connector the library knows by name.</summary>
    public static IReadOnlyList<LlmConnectorDescriptor> All => Connectors;

    /// <summary>
    /// Finds a connector by its name.
    /// </summary>
    /// <param name="name">The connector key, matched case-insensitively.</param>
    /// <returns>The descriptor, or null when no connector has that name.</returns>
    public static LlmConnectorDescriptor? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        foreach (var connector in Connectors)
        {
            if (string.Equals(connector.Name, name, StringComparison.OrdinalIgnoreCase)) return connector;
        }

        return null;
    }

    /// <summary>
    /// Finds a connector by name, throwing when it is unknown.
    /// </summary>
    /// <param name="name">The connector key.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no connector has that name; the message
    /// lists the ones that exist.</exception>
    public static LlmConnectorDescriptor Require(string name) =>
        Find(name)
        ?? throw new InvalidOperationException(
            $"Unknown LLM connector '{name}'. Known connectors: {string.Join(", ", Connectors.Select(c => c.Name))}.");
}
