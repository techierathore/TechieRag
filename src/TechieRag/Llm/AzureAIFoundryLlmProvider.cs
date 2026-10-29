using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Diagnostics;
using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// LLM provider implementation for Azure AI Foundry (formerly Azure OpenAI).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enables LLM interactions using Azure AI Foundry deployments
/// with api-key header authentication.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when LlmSource.AzureAIFoundry is configured.
/// Uses OpenAI-compatible format with Azure-specific endpoint and auth patterns.</para>
/// </remarks>
public class AzureAIFoundryLlmProvider : ILlmProvider, IMultimodalLlmProvider
{
    private readonly HttpClient httpClient;
    private readonly string completionsPath;
    private readonly ILogger<AzureAIFoundryLlmProvider> logger;
    private readonly TimeSpan? configuredTimeout;

    /// <inheritdoc/>
    public string Name => "Azure AI Foundry";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public string? Route { get; init; }

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    /// <remarks>Images are encoded as <c>image_url</c> content parts, inline as a data URI or by URL (REQ-RAG-039).</remarks>
    public bool SupportsInput(ChatContentKind kind) =>
        kind is ChatContentKind.Text or ChatContentKind.Image;

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

    /// <summary>
    /// Creates a new Azure AI Foundry LLM provider instance.
    /// </summary>
    /// <param name="endpoint">Azure AI Foundry endpoint URL.</param>
    /// <param name="apiKey">API key for authentication.</param>
    /// <param name="model">Deployment/model name.</param>
    /// <param name="apiVersion">API version.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="requestTimeout">Optional default timeout for calls from this provider; null uses 120 seconds.</param>
    public AzureAIFoundryLlmProvider(string endpoint, string apiKey, string model, string apiVersion = "2024-12-01-preview", ILogger<AzureAIFoundryLlmProvider>? logger = null, TimeSpan? requestTimeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        ModelName = model;
        this.logger = logger ?? NullLogger<AzureAIFoundryLlmProvider>.Instance;
        configuredTimeout = requestTimeout;
        completionsPath = $"/openai/deployments/{model}/chat/completions?api-version={apiVersion}";

        httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/')),
            // The effective timeout is enforced per-call through a linked cancellation token, so the
            // HttpClient timeout must never truncate a host token that allows more time (TR-RAG-010).
            Timeout = Timeout.InfiniteTimeSpan
        };
        httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
    }

    /// <summary>
    /// Creates an Azure AI Foundry provider with a caller-supplied <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>Test seam: allows a stubbed <see cref="HttpMessageHandler"/> to intercept requests.</remarks>
    /// <param name="httpClient">Pre-configured HTTP client (BaseAddress must be set).</param>
    /// <param name="model">Deployment/model name.</param>
    /// <param name="apiVersion">API version.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="requestTimeout">Optional default timeout for calls from this provider; null uses 120 seconds.</param>
    internal AzureAIFoundryLlmProvider(HttpClient httpClient, string model, string apiVersion = "2024-12-01-preview", ILogger<AzureAIFoundryLlmProvider>? logger = null, TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        ModelName = model;
        completionsPath = $"/openai/deployments/{model}/chat/completions?api-version={apiVersion}";
        this.logger = logger ?? NullLogger<AzureAIFoundryLlmProvider>.Instance;
        configuredTimeout = requestTimeout;
    }

    /// <inheritdoc/>
    public async Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(options?.SystemPrompt))
            messages.Add(ChatMessage.System(options.SystemPrompt));
        messages.Add(ChatMessage.User(prompt));

        return await ChatAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(options?.SystemPrompt))
            messages.Add(ChatMessage.System(options.SystemPrompt));
        messages.Add(ChatMessage.User(prompt));

        await foreach (var token in ChatStreamAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    /// <inheritdoc/>
    public async Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = LlmTimeoutHelper.CreateLinkedTokenSource(options, configuredTimeout, cancellationToken);
        var callToken = timeoutCts?.Token ?? cancellationToken;

        var sw = Stopwatch.StartNew();
        var request = BuildRequest(messages, options, stream: false);
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(completionsPath, content, callToken).ConfigureAwait(false);
        await LlmHttpGuard.EnsureSuccessAsync(response, callToken).ConfigureAwait(false);

        var responseJson = await response.Content.ReadAsStringAsync(callToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<OpenAIChatResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to parse Azure response");

        sw.Stop();

        var choice = result.Choices?.FirstOrDefault();
        var inputTokens = result.Usage?.PromptTokens ?? 0;
        var outputTokens = result.Usage?.CompletionTokens ?? 0;
        var reasoningTokens = result.Usage?.ReasoningTokens ?? 0;
        var cacheReadTokens = result.Usage?.CachedTokens ?? 0;
        var toolCalls = ParseToolCalls(choice?.Message?.ToolCalls);

        var usage = new TokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ReasoningTokens = reasoningTokens,
            IsEstimated = result.Usage is null,
            Route = Route,
            CallerTag = options?.CallerTag,
            CacheReadTokens = cacheReadTokens,
            ModelName = ModelName,
            ProviderName = Name
        };

        var (thinking, answer) = ThinkBlockSplitter.Split(choice?.Message?.Content, choice?.Message?.ReasoningContent ?? choice?.Message?.Reasoning);

        var llmResponse = new LlmResponse
        {
            Content = answer,
            Thinking = thinking,
            ToolCalls = toolCalls,
            Usage = usage,
            FinishReason = choice?.FinishReason ?? "stop",
            ModelName = ModelName
        };

        RaiseCompletionEvent(usage, sw.Elapsed, false, llmResponse.HasToolCalls);
        return llmResponse;
    }

    /// <inheritdoc/>
    /// <remarks>The text-only projection of <see cref="ChatStreamEventsAsync"/> (REQ-RAG-067).</remarks>
    public IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        ChatStreamEventsAsync(messages, options, cancellationToken).ToTextStreamAsync(cancellationToken);

    /// <inheritdoc/>
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamEventsAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timeoutCts = LlmTimeoutHelper.CreateLinkedTokenSource(options, configuredTimeout, cancellationToken);
        var callToken = timeoutCts?.Token ?? cancellationToken;

        var sw = Stopwatch.StartNew();
        var request = BuildRequest(messages, options, stream: true);
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, completionsPath) { Content = content };
        var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, callToken).ConfigureAwait(false);
        await LlmHttpGuard.EnsureSuccessAsync(response, callToken).ConfigureAwait(false);

        var stream = await response.Content.ReadAsStreamAsync(callToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var context = new StreamReadContext(Name, ModelName, messages, EstimateTokenCount, (usage, involvedToolCalls) =>
            RaiseCompletionEvent(usage, sw.Elapsed, true, involvedToolCalls), Route, options?.CallerTag);

        await foreach (var streamEvent in OpenAIStreamReader.ReadAsync(reader, context, callToken).ConfigureAwait(false))
        {
            yield return streamEvent;
        }
    }

    /// <inheritdoc/>
    public async Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        var jsonPrompt = $"{prompt}\n\nRespond with valid JSON only.";
        var response = await CompleteAsync(jsonPrompt, new LlmCompletionOptions
        {
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxTokens,
            SystemPrompt = options?.SystemPrompt ?? "You are a helpful assistant that responds only with valid JSON.",
            JsonMode = true
        }, cancellationToken).ConfigureAwait(false);

        var content = response.Content?.Trim() ?? throw new InvalidOperationException("LLM returned empty response");
        if (content.StartsWith("```"))
        {
            var firstNewline = content.IndexOf('\n');
            if (firstNewline > 0) content = content[(firstNewline + 1)..];
            if (content.EndsWith("```")) content = content[..^3];
            content = content.Trim();
        }

        return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Failed to deserialize response to {typeof(T).Name}");
    }

    /// <inheritdoc/>
    public int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    private object BuildRequest(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options, bool stream)
    {
        var apiMessages = messages.Select(m =>
        {
            var msg = new Dictionary<string, object> { ["role"] = m.Role };
            if (m.Content is not null || m.Parts is { Count: > 0 })
            {
                msg["content"] = OpenAIMessageMapper.BuildContent(m);
            }
            if (m.ToolCallId is not null) msg["tool_call_id"] = m.ToolCallId;
            if (m.ToolCalls is { Count: > 0 })
            {
                msg["tool_calls"] = m.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.ArgumentsJson }
                }).ToList();
            }
            return msg;
        }).ToList();

        var request = new Dictionary<string, object>
        {
            ["messages"] = apiMessages,
            ["stream"] = stream
        };

        if (stream)
        {
            // Ask the service to append a final usage chunk to the stream (TR-RAG-002)
            request["stream_options"] = new Dictionary<string, object> { ["include_usage"] = true };
        }

        // REQ-RAG-043: prefix caching on these services is automatic; only the routing key is
        // expressible on the wire. See PromptCacheOptions for what is intentionally not sent.
        OpenAIMessageMapper.ApplyPromptCache(request, options?.PromptCache);

        if (options?.Temperature is not null) request["temperature"] = options.Temperature.Value;
        if (options?.MaxTokens is not null) request["max_tokens"] = options.MaxTokens.Value;
        if (options?.TopP is not null) request["top_p"] = options.TopP.Value;
        if (options?.JsonMode == true) request["response_format"] = new { type = "json_object" };

        if (options?.Tools is { Count: > 0 })
        {
            request["tools"] = options.Tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersSchema)
                }
            }).ToList();

            if (options.ToolChoice is not null)
                request["tool_choice"] = options.ToolChoice;
        }

        return request;
    }

    private static List<ToolCall>? ParseToolCalls(List<OpenAIToolCall>? apiToolCalls)
    {
        if (apiToolCalls is not { Count: > 0 }) return null;
        return apiToolCalls.Select(tc => new ToolCall
        {
            Id = tc.Id ?? Guid.NewGuid().ToString(),
            Name = tc.Function?.Name ?? string.Empty,
            ArgumentsJson = tc.Function?.Arguments ?? "{}"
        }).ToList();
    }

    private void RaiseCompletionEvent(TokenUsage usage, TimeSpan duration, bool isStreaming, bool involvedToolCalls)
    {
        TechieRagTelemetry.RecordLlmCompletion(
            Name, ModelName, usage.InputTokens, usage.OutputTokens, duration, isStreaming, usage.CacheReadTokens);

        OnCompletionCompleted?.Invoke(this, new LlmCompletionEventArgs
        {
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            ReasoningTokens = usage.ReasoningTokens,
            IsEstimated = usage.IsEstimated,
            Duration = duration,
            ModelName = ModelName,
            ProviderName = Name,
            Route = Route,
            CallerTag = usage.CallerTag,
            IsStreaming = isStreaming,
            InvolvedToolCalls = involvedToolCalls
        });
    }
}
