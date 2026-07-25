using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
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
public class AzureAIFoundryLlmProvider : ILlmProvider
{
    private readonly HttpClient httpClient;
    private readonly string completionsPath;
    private readonly ILogger<AzureAIFoundryLlmProvider> logger;

    /// <inheritdoc/>
    public string Name => "Azure AI Foundry";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

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
    public AzureAIFoundryLlmProvider(string endpoint, string apiKey, string model, string apiVersion = "2024-12-01-preview", ILogger<AzureAIFoundryLlmProvider>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        ModelName = model;
        this.logger = logger ?? NullLogger<AzureAIFoundryLlmProvider>.Instance;
        completionsPath = $"/openai/deployments/{model}/chat/completions?api-version={apiVersion}";

        httpClient = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(120)
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
    internal AzureAIFoundryLlmProvider(HttpClient httpClient, string model, string apiVersion = "2024-12-01-preview", ILogger<AzureAIFoundryLlmProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        ModelName = model;
        completionsPath = $"/openai/deployments/{model}/chat/completions?api-version={apiVersion}";
        this.logger = logger ?? NullLogger<AzureAIFoundryLlmProvider>.Instance;
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
        var sw = Stopwatch.StartNew();
        var request = BuildRequest(messages, options, stream: false);
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(completionsPath, content, cancellationToken).ConfigureAwait(false);
        LlmHttpGuard.EnsureSuccess(response);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<OpenAIChatResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to parse Azure response");

        sw.Stop();

        var choice = result.Choices?.FirstOrDefault();
        var inputTokens = result.Usage?.PromptTokens ?? 0;
        var outputTokens = result.Usage?.CompletionTokens ?? 0;
        var toolCalls = ParseToolCalls(choice?.Message?.ToolCalls);

        var llmResponse = new LlmResponse
        {
            Content = choice?.Message?.Content,
            ToolCalls = toolCalls,
            Usage = new TokenUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                ModelName = ModelName,
                ProviderName = Name
            },
            FinishReason = choice?.FinishReason ?? "stop",
            ModelName = ModelName
        };

        RaiseCompletionEvent(inputTokens, outputTokens, sw.Elapsed, false, llmResponse.HasToolCalls);
        return llmResponse;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var request = BuildRequest(messages, options, stream: true);
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, completionsPath) { Content = content };
        var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        LlmHttpGuard.EnsureSuccess(response);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var outputText = new StringBuilder();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            var chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (chunk?.Usage is not null)
            {
                totalInputTokens = chunk.Usage.PromptTokens;
                totalOutputTokens = chunk.Usage.CompletionTokens;
            }

            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(delta))
            {
                outputText.Append(delta);
                yield return delta;
            }
        }

        sw.Stop();

        // Fallback: estimate when the server sent no usage chunk
        if (totalInputTokens == 0 && totalOutputTokens == 0)
        {
            totalInputTokens = messages.Sum(m => EstimateTokenCount(m.Content ?? string.Empty));
            totalOutputTokens = EstimateTokenCount(outputText.ToString());
        }

        RaiseCompletionEvent(totalInputTokens, totalOutputTokens, sw.Elapsed, true, false);
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
            if (m.Content is not null) msg["content"] = m.Content;
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

    private void RaiseCompletionEvent(int inputTokens, int outputTokens, TimeSpan duration, bool isStreaming, bool involvedToolCalls)
    {
        OnCompletionCompleted?.Invoke(this, new LlmCompletionEventArgs
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Duration = duration,
            ModelName = ModelName,
            ProviderName = Name,
            IsStreaming = isStreaming,
            InvolvedToolCalls = involvedToolCalls
        });
    }
}
