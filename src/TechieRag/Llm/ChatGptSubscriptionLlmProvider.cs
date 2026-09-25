using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieRag.Abstractions;
using TechieRag.Diagnostics;
using TechieRag.Models;
using TechieRag.Web;

namespace TechieRag.Llm;

/// <summary>
/// An <see cref="ILlmProvider"/> billed to the user's ChatGPT subscription through OpenAI's sign-in,
/// not an API key (REQ-RAG-069 / BRD-112).
/// </summary>
/// <remarks>
/// <para><b>Who does what:</b> the library asks OpenAI for a device code and hands the page and code to
/// the host's sign-in callback; the host opens the browser (or shows the code); the library waits for the
/// authorisation, keeps the session in the <see cref="ISubscriptionSessionStore"/>, refreshes it before it
/// expires and signs in again only when the vendor refuses it.</para>
/// <para><b>When sign-in happens:</b> on <see cref="SignInAsync"/>, or lazily on the first model call when
/// the store holds no session. Concurrent first calls share one sign-in.</para>
/// <para><b>Terms:</b> OpenAI permits this for the account that signs in (DECISIONS.md, 2026-09-24); the
/// terms text is in <see cref="LlmConnectorCatalog"/> under <c>chatgpt-subscription</c> for a host to show
/// before sign-in.</para>
/// </remarks>
public sealed class ChatGptSubscriptionLlmProvider : ILlmProvider, IDisposable
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly Func<SubscriptionSignInPrompt, CancellationToken, Task> signInCallback;
    private readonly ChatGptSubscriptionOptions options;
    private readonly ISubscriptionSessionStore sessionStore;
    private readonly HttpClient httpClient;
    private readonly ChatGptDeviceSignIn signIn;
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private readonly ILogger<ChatGptSubscriptionLlmProvider> logger;

    /// <summary>Creates the provider.</summary>
    /// <param name="signInCallback">Called with the page and code when the user must sign in; the host opens the browser.</param>
    /// <param name="options">Model, session store and endpoints; null uses the defaults.</param>
    /// <param name="logger">Logger; never receives a token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="signInCallback"/> is null.</exception>
    public ChatGptSubscriptionLlmProvider(
        Func<SubscriptionSignInPrompt, CancellationToken, Task> signInCallback,
        ChatGptSubscriptionOptions? options = null,
        ILogger<ChatGptSubscriptionLlmProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(signInCallback);

        this.signInCallback = signInCallback;
        this.options = options ?? new ChatGptSubscriptionOptions();
        this.logger = logger ?? NullLogger<ChatGptSubscriptionLlmProvider>.Instance;
        sessionStore = this.options.SessionStore ?? new InMemorySubscriptionSessionStore();
        httpClient = new HttpClient(this.options.Handler ?? HttpWebContentFetcher.CreateGuardedHandler(), disposeHandler: this.options.Handler is null)
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        signIn = new ChatGptDeviceSignIn(httpClient, this.options);
        ModelName = this.options.Model;
    }

    /// <inheritdoc/>
    public string Name => "ChatGPT-Subscription";

    /// <inheritdoc/>
    public string ModelName { get; }

    /// <inheritdoc/>
    public bool SupportsToolCalling => true;

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    public event EventHandler<LlmCompletionEventArgs>? OnCompletionCompleted;

    /// <summary>
    /// Makes sure a usable session exists: loads it, refreshes it, or runs the sign-in through the callback.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token; cancelling abandons a sign-in in progress.</param>
    /// <returns>A task that completes when the provider is signed in.</returns>
    /// <exception cref="SubscriptionSignInException">The vendor refused, or the user did not authorise in time.</exception>
    public async Task SignInAsync(CancellationToken cancellationToken = default) =>
        await GetSessionAsync(forceRenewal: false, cancellationToken).ConfigureAwait(false);

    /// <summary>Forgets the session, so the next call signs in again.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the session is cleared from the store.</returns>
    public Task SignOutAsync(CancellationToken cancellationToken = default) =>
        sessionStore.ClearAsync(SubscriptionConnectorRows.ChatGptName, cancellationToken);

    /// <inheritdoc/>
    public Task<LlmResponse> CompleteAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        ChatAsync(PromptMessages(prompt, options), options, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        ChatStreamAsync(PromptMessages(prompt, options), options, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>The subscription backend only streams, so this reads the stream to its end.</remarks>
    public async Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        var toolCalls = new List<ToolCall>();
        LlmStreamEvent? completed = null;

        await foreach (var streamEvent in StreamAsync(messages, options, isStreaming: false, cancellationToken).ConfigureAwait(false))
        {
            switch (streamEvent.Kind)
            {
                case LlmStreamEventKind.TextDelta: text.Append(streamEvent.Text); break;
                case LlmStreamEventKind.ToolCall: toolCalls.Add(streamEvent.ToolCall!); break;
                default: completed = streamEvent; break;
            }
        }

        return new LlmResponse
        {
            Content = text.ToString(),
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            Usage = completed?.Usage ?? new TokenUsage { ModelName = ModelName, ProviderName = Name },
            FinishReason = completed?.FinishReason ?? "stop",
            ModelName = completed?.ModelName ?? ModelName
        };
    }

    /// <inheritdoc/>
    /// <remarks>The text-only projection of <see cref="ChatStreamEventsAsync"/> (REQ-RAG-067).</remarks>
    public IAsyncEnumerable<string> ChatStreamAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        ChatStreamEventsAsync(messages, options, cancellationToken).ToTextStreamAsync(cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<LlmStreamEvent> ChatStreamEventsAsync(IReadOnlyList<ChatMessage> messages, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) =>
        StreamAsync(messages, options, isStreaming: true, cancellationToken);

    /// <inheritdoc/>
    public async Task<T> CompleteAsync<T>(string prompt, LlmCompletionOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        var response = await CompleteAsync($"{prompt}\n\nRespond with valid JSON only.", new LlmCompletionOptions
        {
            SystemPrompt = options?.SystemPrompt ?? "You are a helpful assistant that responds only with valid JSON.",
            Model = options?.Model
        }, cancellationToken).ConfigureAwait(false);

        var content = StripCodeFence(response.Content?.Trim() ?? throw new InvalidOperationException("LLM returned empty response"));
        return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Failed to deserialize response to {typeof(T).Name}");
    }

    /// <inheritdoc/>
    public int EstimateTokenCount(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

    /// <inheritdoc/>
    public void Dispose()
    {
        httpClient.Dispose();
        sessionGate.Dispose();
    }

    private async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        LlmCompletionOptions? options,
        bool isStreaming,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var body = JsonSerializer.Serialize(OpenAIResponsesRequest.Build(messages, options, ModelName, this.options.DefaultInstructions));
        using var response = await SendWithSessionAsync(body, cancellationToken).ConfigureAwait(false);

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var context = new StreamReadContext(Name, options?.Model ?? ModelName, messages, EstimateTokenCount, (usage, involvedToolCalls) =>
            RaiseCompletionEvent(usage, stopwatch.Elapsed, isStreaming, involvedToolCalls));

        await foreach (var streamEvent in OpenAIResponsesStreamReader.ReadAsync(reader, context, cancellationToken).ConfigureAwait(false))
        {
            yield return streamEvent;
        }
    }

    private async Task<HttpResponseMessage> SendWithSessionAsync(string body, CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(forceRenewal: false, cancellationToken).ConfigureAwait(false);
        var response = await SendAsync(body, session, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            LlmHttpGuard.EnsureSuccess(response);
            return response;
        }

        // A 401 means the access token was revoked or expired early: renew once, then give up.
        response.Dispose();
        logger.LogInformation("The ChatGPT subscription session was refused; renewing it once.");
        session = await GetSessionAsync(forceRenewal: true, cancellationToken).ConfigureAwait(false);
        response = await SendAsync(body, session, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            await sessionStore.ClearAsync(SubscriptionConnectorRows.ChatGptName, cancellationToken).ConfigureAwait(false);
            throw new SubscriptionSignInException(SubscriptionSignInException.CodeSessionRejected, "The ChatGPT subscription session was refused after renewal.");
        }

        LlmHttpGuard.EnsureSuccess(response);
        return response;
    }

    private async Task<HttpResponseMessage> SendAsync(string body, SubscriptionSession session, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.BackendEndpoint.ToString().TrimEnd('/') + "/responses"))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("OpenAI-Beta", "responses=experimental");
        request.Headers.TryAddWithoutValidation("originator", "techierag");
        if (!string.IsNullOrEmpty(session.AccountId)) request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", session.AccountId);

        using (request)
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<SubscriptionSession> GetSessionAsync(bool forceRenewal, CancellationToken cancellationToken)
    {
        await sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = await sessionStore.LoadAsync(SubscriptionConnectorRows.ChatGptName, cancellationToken).ConfigureAwait(false);
            var needsRenewal = forceRenewal || session?.IsExpired(options.Clock(), RefreshMargin) == true;
            if (session is not null && !needsRenewal) return session;

            session = await RenewAsync(session, cancellationToken).ConfigureAwait(false);
            await sessionStore.SaveAsync(session, cancellationToken).ConfigureAwait(false);
            return session;
        }
        finally
        {
            sessionGate.Release();
        }
    }

    private async Task<SubscriptionSession> RenewAsync(SubscriptionSession? session, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(session?.RefreshToken))
        {
            try
            {
                return await signIn.RefreshAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (SubscriptionSignInException)
            {
                logger.LogInformation("The ChatGPT refresh token was refused; signing in again.");
            }
        }

        logger.LogInformation("Starting ChatGPT subscription sign-in.");
        return await signIn.SignInAsync(signInCallback, cancellationToken).ConfigureAwait(false);
    }

    private static List<ChatMessage> PromptMessages(string prompt, LlmCompletionOptions? options)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(options?.SystemPrompt)) messages.Add(ChatMessage.System(options.SystemPrompt));
        messages.Add(ChatMessage.User(prompt));
        return messages;
    }

    private static string StripCodeFence(string content)
    {
        if (!content.StartsWith("```", StringComparison.Ordinal)) return content;

        var firstNewline = content.IndexOf('\n');
        if (firstNewline > 0) content = content[(firstNewline + 1)..];
        if (content.EndsWith("```", StringComparison.Ordinal)) content = content[..^3];
        return content.Trim();
    }

    private void RaiseCompletionEvent(TokenUsage usage, TimeSpan duration, bool isStreaming, bool involvedToolCalls)
    {
        TechieRagTelemetry.RecordLlmCompletion(Name, usage.ModelName, usage.InputTokens, usage.OutputTokens, duration, isStreaming, usage.CacheReadTokens);

        OnCompletionCompleted?.Invoke(this, new LlmCompletionEventArgs
        {
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            Duration = duration,
            ModelName = usage.ModelName,
            ProviderName = Name,
            IsStreaming = isStreaming,
            InvolvedToolCalls = involvedToolCalls
        });
    }
}
