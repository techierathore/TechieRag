using Microsoft.Extensions.Logging;
using TechieRag.Abstractions;

namespace TechieRag.Llm;

/// <summary>
/// Builds an <see cref="ILlmProvider"/> for a named connector or a routed model (REQ-RAG-034).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One place that knows which provider implementation serves which connector,
/// so callers can go from a model name straight to a working provider.</para>
/// <para><b>No new provider classes:</b> every connector in
/// <see cref="LlmConnectorCatalog"/> other than Anthropic, Gemini and the two local runtimes is
/// OpenAI-compatible, so they are all served by the existing
/// <see cref="OpenAICompatibleLlmProvider"/> pointed at a different base URL. Adding a service is a
/// catalog row; it is not a class, an enum member and a switch arm.</para>
/// <para><b>Credentials:</b> the API key is passed to the provider and never logged or included in
/// an exception message.</para>
/// </remarks>
public static class LlmProviderFactory
{
    /// <summary>
    /// Creates a provider for a resolved route.
    /// </summary>
    /// <param name="route">The connector and model to use.</param>
    /// <param name="apiKey">The API key, or null/empty for local runtimes that need none.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="maxTokens">Default max output tokens, used by providers that require it up front.</param>
    /// <returns>A provider configured for the route.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="route"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the connector needs an API key and none was supplied.</exception>
    public static ILlmProvider Create(
        ModelRoute route,
        string? apiKey,
        ILoggerFactory? loggerFactory = null,
        int maxTokens = 2048)
    {
        ArgumentNullException.ThrowIfNull(route);

        var connector = route.Connector;
        if (connector.RequiresApiKey && string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException($"An API key is required for the '{connector.Name}' connector.");
        }

        return connector.Source switch
        {
            LlmSource.Anthropic => new AnthropicLlmProvider(
                apiKey!,
                route.ModelId,
                connector.Endpoint,
                maxTokens,
                loggerFactory?.CreateLogger<AnthropicLlmProvider>()),

            LlmSource.GoogleGemini => new GoogleGeminiLlmProvider(
                apiKey!,
                route.ModelId,
                connector.Endpoint,
                loggerFactory?.CreateLogger<GoogleGeminiLlmProvider>()),

            LlmSource.Ollama => new OllamaLlmProvider(
                connector.Endpoint ?? "http://localhost:11434",
                route.ModelId,
                loggerFactory?.CreateLogger<OllamaLlmProvider>()),

            LlmSource.LmStudio => new LmStudioLlmProvider(
                connector.Endpoint ?? "http://localhost:1234",
                route.ModelId,
                loggerFactory?.CreateLogger<LmStudioLlmProvider>()),

            LlmSource.OpenAICompatible => new OpenAICompatibleLlmProvider(
                connector.Endpoint ?? throw new InvalidOperationException($"Connector '{connector.Name}' has no endpoint."),
                apiKey ?? string.Empty,
                route.ModelId,
                loggerFactory?.CreateLogger<OpenAICompatibleLlmProvider>()),

            // REQ-RAG-070: a subscription has no API key; it needs the host's sign-in callback.
            LlmSource.Subscription => throw SubscriptionNeedsSignIn(connector),

            // REQ-RAG-064: the in-process model; no endpoint, no key. Served by TechieRag.Local.
            LlmSource.Local => LocalLlmProviderRegistry.Create(route.ModelId, loggerFactory),

            _ => throw new InvalidOperationException($"Connector '{connector.Name}' has no provider implementation.")
        };
    }

    /// <summary>
    /// Routes a model name to a service and creates the provider for it.
    /// </summary>
    /// <param name="modelName">A bare model name, or <c>connector/model</c>.</param>
    /// <param name="apiKey">The API key, or null/empty for local runtimes that need none.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="maxTokens">Default max output tokens.</param>
    /// <returns>A provider for the service the model name resolves to.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the model name identifies no single service.</exception>
    public static ILlmProvider CreateForModel(
        string modelName,
        string? apiKey,
        ILoggerFactory? loggerFactory = null,
        int maxTokens = 2048) =>
        Create(ModelRouter.Require(modelName), apiKey, loggerFactory, maxTokens);

    /// <summary>
    /// Creates a provider for a <see cref="LlmSource.Subscription"/> route, signing in through the
    /// host's callback (REQ-RAG-069 / REQ-RAG-070).
    /// </summary>
    /// <param name="route">A subscription route, e.g. from <c>ModelRouter.Require("chatgpt-subscription/gpt-6-luna")</c>.</param>
    /// <param name="signInCallback">Called with the page and code when the user must sign in; the host opens the browser.</param>
    /// <param name="sessionStore">Where the session is kept; null keeps it in memory.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>A provider billed to the signed-in user's subscription.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="SubscriptionSignInException">The route's vendor does not permit subscription sign-in
    /// (<see cref="SubscriptionSignInException.CodeNotPermitted"/>); the message carries the vendor's terms.</exception>
    /// <exception cref="InvalidOperationException">The route is not a subscription route.</exception>
    public static ILlmProvider CreateSubscription(
        ModelRoute route,
        Func<Models.SubscriptionSignInPrompt, CancellationToken, Task> signInCallback,
        ISubscriptionSessionStore? sessionStore = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(signInCallback);

        var connector = route.Connector;
        if (connector.Source != LlmSource.Subscription || connector.Subscription is null)
        {
            throw new InvalidOperationException($"Connector '{connector.Name}' is not a subscription connector.");
        }

        return connector.Name switch
        {
            SubscriptionConnectorRows.ChatGptName when connector.Subscription.Permitted => new ChatGptSubscriptionLlmProvider(
                signInCallback,
                new ChatGptSubscriptionOptions { Model = route.ModelId, SessionStore = sessionStore },
                loggerFactory?.CreateLogger<ChatGptSubscriptionLlmProvider>()),

            _ => throw NotPermitted(connector)
        };
    }

    private static Exception SubscriptionNeedsSignIn(LlmConnectorDescriptor connector) =>
        connector.Subscription is { Permitted: true, BuilderMethod: { } method }
            ? new InvalidOperationException(
                $"Connector '{connector.Name}' is a subscription and needs a sign-in callback: use {method} or LlmProviderFactory.CreateSubscription.")
            : NotPermitted(connector);

    private static SubscriptionSignInException NotPermitted(LlmConnectorDescriptor connector) =>
        new(SubscriptionSignInException.CodeNotPermitted,
            $"Connector '{connector.Name}' offers no subscription sign-in (terms checked {connector.Subscription?.CheckedOn:yyyy-MM-dd}): {connector.Subscription?.Terms}");
}
