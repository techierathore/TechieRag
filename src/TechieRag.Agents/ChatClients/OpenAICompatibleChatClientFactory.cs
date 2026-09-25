using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace TechieRag.Agents.ChatClients;

/// <summary>
/// Builds a Chat Completions <see cref="IChatClient"/> for LM Studio, Ollama's <c>/v1</c>, OpenAI and any
/// OpenAI-compatible server through the OpenAI SDK (REQ-RAG-016 / BRD-84).
/// </summary>
/// <remarks>
/// Chat Completions rather than Responses: it is the API every local server implements with tool
/// calling. Local servers ignore the key, but the SDK requires one, so a placeholder is sent.
/// </remarks>
internal static class OpenAICompatibleChatClientFactory
{
    /// <summary>The placeholder key sent to servers that need none.</summary>
    public const string PlaceholderKey = "not-needed";

    /// <summary>Creates the chat client.</summary>
    /// <param name="endpointWithV1">The API root including <c>/v1</c>, for example <c>http://localhost:1234/v1</c>.</param>
    /// <param name="apiKey">The API key, or null for a local server.</param>
    /// <param name="model">The model id.</param>
    /// <returns>The chat client.</returns>
    /// <exception cref="ArgumentException">The endpoint or model is empty, or the endpoint is not an absolute URI.</exception>
    public static IChatClient Create(string endpointWithV1, string? apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointWithV1);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (!Uri.TryCreate(endpointWithV1.TrimEnd('/'), UriKind.Absolute, out var endpoint))
        {
            throw new ArgumentException($"'{endpointWithV1}' is not an absolute URI.", nameof(endpointWithV1));
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? PlaceholderKey : apiKey),
            new OpenAIClientOptions { Endpoint = endpoint });
        return client.GetChatClient(model).AsIChatClient();
    }

    /// <summary>Appends <c>/v1</c> to a server root unless it already ends with it.</summary>
    /// <param name="endpoint">The server root.</param>
    /// <returns>The API root.</returns>
    public static string WithV1(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        var trimmed = endpoint.TrimEnd('/');
        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + "/v1";
    }
}
