namespace TechieRag.Llm;

/// <summary>
/// The library's built-in table of named LLM services (REQ-RAG-034).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a caller say "groq" or "deepseek" instead of memorising a base URL,
/// and lets <see cref="ModelRouter"/> work out which service a model name belongs to.</para>
/// <para><b>Extensible without a release:</b> a service that is not listed here still works by route —
/// <c>openai-compatible/&lt;model&gt;</c> or <c>anthropic-compatible/&lt;model&gt;</c> with the service's
/// endpoint as the app's Base URL or as an <c>@https://</c> suffix (REQ-RAG-130). The catalog is a
/// convenience over that, not a gate in front of it.</para>
/// <para><b>What is deliberately absent:</b> AWS Bedrock. Its API is not OpenAI-compatible and
/// every request must be SigV4-signed against the caller's AWS credentials, which means an AWS SDK
/// dependency. That conflicts with the library's dependency-light rule, so Bedrock is left to a
/// custom <c>ILlmProvider</c> supplied through <c>UseCustomLlmProvider</c>.</para>
/// </remarks>
public static class LlmConnectorCatalog
{
    /// <summary>The ChatGPT subscription connector's key, for <see cref="Find"/> and <c>connector/model</c> names.</summary>
    public const string ChatGptSubscriptionName = SubscriptionConnectorRows.ChatGptName;

    /// <summary>The OpenCode Go connector's key (REQ-RAG-109).</summary>
    public const string OpenCodeGoName = "opencode-go";

    /// <summary>The Venice AI connector's key (REQ-RAG-131).</summary>
    public const string VeniceName = "venice";

    /// <summary>The generic OpenAI-compatible connector's key; it needs an endpoint (REQ-RAG-130).</summary>
    public const string OpenAICompatibleName = "openai-compatible";

    /// <summary>The generic Anthropic-compatible connector's key; it needs an endpoint (REQ-RAG-130).</summary>
    public const string AnthropicCompatibleName = "anthropic-compatible";

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
            Endpoint = "https://api.groq.com/openai/v1",
            ExampleModel = "llama-3.3-70b-versatile"
        },
        new()
        {
            Name = "together",
            DisplayName = "Together AI",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.together.xyz/v1",
            ExampleModel = "meta-llama/Llama-3.3-70B-Instruct-Turbo"
        },
        new()
        {
            Name = "openrouter",
            DisplayName = "OpenRouter",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            ExampleModel = "anthropic/claude-sonnet-4.5"
        },
        new()
        {
            Name = "fireworks",
            DisplayName = "Fireworks AI",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.fireworks.ai/inference/v1",
            ExampleModel = "accounts/fireworks/models/llama-v3p3-70b-instruct"
        },
        new()
        {
            Name = "cerebras",
            DisplayName = "Cerebras",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.cerebras.ai/v1",
            ExampleModel = "llama-3.3-70b"
        },
        // REQ-RAG-131: Venice is OpenAI-compatible and multi-vendor. What earns it a name is its own
        // system prompt, which it adds to every request unless venice_parameters switches it off
        // (https://docs.venice.ai/api-reference/endpoint/chat/completions, checked 2026-10-11). The library
        // writes its own system prompts, so this connector switches Venice's off; a caller who wants it
        // uses openai-compatible/<model>@https://api.venice.ai/api/v1 instead.
        new()
        {
            Name = VeniceName,
            DisplayName = "Venice AI",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://api.venice.ai/api/v1",
            ExampleModel = "zai-org-glm-5-1",
            RequestBodyFields = new Dictionary<string, object>
            {
                ["venice_parameters"] = new Dictionary<string, object> { ["include_venice_system_prompt"] = false }
            }
        },
        // REQ-RAG-130: any service that speaks one of the two wire formats, at an endpoint the caller gives
        // (the app's Base URL, or "@https://..." on the route). A service the catalog does not list then works
        // without a release, e.g. GLM through https://api.z.ai/api/anthropic.
        new()
        {
            Name = OpenAICompatibleName,
            DisplayName = "Any OpenAI-compatible service",
            Source = LlmSource.OpenAICompatible,
            RequiresEndpoint = true,
            // A key is required, as hosted OpenAI-compatible services (OpenRouter, Azure AI Foundry, the Gemini
            // API) all refuse without one; a self-hosted server that ignores keys takes any placeholder.
            ExampleModel = "glm-4.6",
            ExampleEndpoint = "https://api.z.ai/api/paas/v4"
        },
        new()
        {
            Name = AnthropicCompatibleName,
            DisplayName = "Any Anthropic-compatible service",
            Source = LlmSource.Anthropic,
            RequiresEndpoint = true,
            ExampleModel = "glm-4.6",
            ExampleEndpoint = "https://api.z.ai/api/anthropic"
        },
        // REQ-RAG-109: OpenCode Go refuses a request without a session id in x-opencode-session
        // (https://opencode.ai/docs/go/#where-can-i-use-it). Only its chat/completions models work
        // here; the models it serves on /responses or /messages need those wire formats.
        new()
        {
            Name = OpenCodeGoName,
            DisplayName = "OpenCode Go",
            Source = LlmSource.OpenAICompatible,
            Endpoint = "https://opencode.ai/zen/go/v1",
            DefaultModel = "kimi-k2.7-code",
            SessionHeader = "x-opencode-session"
        },
        new()
        {
            Name = "ollama",
            DisplayName = "Ollama (local)",
            Source = LlmSource.Ollama,
            Endpoint = "http://localhost:11434",
            RequiresApiKey = false,
            IsLocal = true,
            ExampleModel = "llama3.2"
        },
        new()
        {
            Name = "lmstudio",
            DisplayName = "LM Studio (local)",
            Source = LlmSource.LmStudio,
            Endpoint = "http://localhost:1234",
            RequiresApiKey = false,
            IsLocal = true,
            ExampleModel = "qwen3-8b"
        },
        // REQ-RAG-064 / BRD-104: the in-process model of TechieRag.Local. No endpoint and no key;
        // no prefixes, because open-weight names are not one service's, so it is "local/<model>" only.
        new()
        {
            Name = "local",
            DisplayName = "Local model (in-process)",
            Source = LlmSource.Local,
            RequiresApiKey = false,
            IsLocal = true,
            ExampleModel = "qwen2.5-0.5b-instruct"
        },
        // Consumer-subscription sign-in, one row per vendor researched (REQ-RAG-070 / BRD-113).
        .. SubscriptionConnectorRows.All
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
    /// Describes every connector an app can offer, for a provider page's hint and fields (REQ-RAG-132).
    /// </summary>
    /// <returns>One row per usable connector, in catalog order: its display name, whether it needs a key, an
    /// endpoint or a sign-in, local or hosted, its default endpoint and one example route that
    /// <see cref="ModelRouter"/> resolves to it. Subscription connectors whose vendor does not permit
    /// sign-in are left out, because no route to them can work.</returns>
    /// <example>
    /// <code>foreach (var c in LlmConnectorCatalog.Describe()) hint.Add($"{c.DisplayName}: {c.ExampleRoute}");</code>
    /// </example>
    public static IReadOnlyList<LlmConnectorInfo> Describe() =>
        Connectors
            .Where(c => c.Subscription is null || c.Subscription.Permitted)
            .Select(c => new LlmConnectorInfo
            {
                Name = c.Name,
                DisplayName = c.DisplayName,
                RequiresApiKey = c.RequiresApiKey,
                RequiresEndpoint = c.RequiresEndpoint,
                RequiresSignIn = c.Source == LlmSource.Subscription,
                IsLocal = c.IsLocal,
                DefaultEndpoint = c.Endpoint,
                ExampleRoute = ExampleRoute(c)
            })
            .ToList();

    private static string ExampleRoute(LlmConnectorDescriptor connector)
    {
        var route = $"{connector.Name}/{connector.DefaultModel ?? connector.ExampleModel ?? "<model>"}";
        return connector.RequiresEndpoint && connector.ExampleEndpoint is not null
            ? $"{route}@{connector.ExampleEndpoint}"
            : route;
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
