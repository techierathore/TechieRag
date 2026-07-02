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
/// LLM provider implementation for OpenAI-compatible REST APIs.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Works with OpenAI, vLLM, LocalAI, Together.ai, Groq,
/// and other OpenAI-compatible APIs.</para>
/// <para><b>Code Flow:</b> Created by TechieRagBuilder when LlmSource.OpenAICompatible is configured.
/// Uses standard OpenAI chat completions API format.</para>
/// </remarks>
public class OpenAICompatibleLlmProvider : ILlmProvider
{
    private readonly HttpClient httpClient;
    private readonly string completionsPath;
    private readonly ILogger<OpenAICompatibleLlmProvider> logger;

    /// <inheritdoc/>
    public string Name => "OpenAI-Compatible";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

    /// <summary>
    /// Creates a new OpenAI-compatible LLM provider instance.
    /// </summary>
    /// <param name="endpoint">API endpoint (e.g., "https://api.openai.com/v1").</param>
    /// <param name="apiKey">API key for authentication.</param>
    /// <param name="model">Model name (e.g., "gpt-4o").</param>
    /// <param name="logger">Logger instance.</param>
    public OpenAICompatibleLlmProvider(string endpoint, string apiKey, string model, ILogger<OpenAICompatibleLlmProvider>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(model);

        ModelName = model;
        this.logger = logger ?? NullLogger<OpenAICompatibleLlmProvider>.Instance;

        var baseUri = endpoint.TrimEnd('/');
        completionsPath = baseUri.EndsWith("/v1") ? "/v1/chat/completions" : "/chat/completions";

        httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUri.EndsWith("/v1") ? baseUri[..^3] : baseUri),
            Timeout = TimeSpan.FromSeconds(120)
        };

        if (!string.IsNullOrEmpty(apiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
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
            ?? throw new InvalidOperationException("Failed to parse response");

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
            ModelName = result.Model ?? ModelName
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

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            var chunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(delta))
            {
                yield return delta;
            }
        }

        sw.Stop();
        RaiseCompletionEvent(0, 0, sw.Elapsed, true, false);
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
            ["model"] = ModelName,
            ["messages"] = apiMessages,
            ["stream"] = stream
        };

        if (options?.Temperature is not null) request["temperature"] = options.Temperature.Value;
        if (options?.MaxTokens is not null) request["max_tokens"] = options.MaxTokens.Value;
        if (options?.TopP is not null) request["top_p"] = options.TopP.Value;
        if (options?.FrequencyPenalty is not null) request["frequency_penalty"] = options.FrequencyPenalty.Value;
        if (options?.PresencePenalty is not null) request["presence_penalty"] = options.PresencePenalty.Value;
        if (options?.StopSequences is not null) request["stop"] = options.StopSequences;
        if (options?.Seed is not null) request["seed"] = options.Seed.Value;

        if (options?.JsonMode == true)
            request["response_format"] = new { type = "json_object" };

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
