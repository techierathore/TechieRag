using System.Text.Json;
using TechieRag.Abstractions;
using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm.Subscription;

/// <summary>
/// Offline tests for the ChatGPT subscription sign-in and provider (REQ-RAG-069 / BRD-112), driven
/// through <see cref="FakeChatGptServer"/> so the device-code flow, the session seam and the typed
/// stream are exercised without a network or a real account.
/// </summary>
public class ChatGptSubscriptionTests
{
    private static readonly IReadOnlyList<ChatMessage> Question = [ChatMessage.System("Be brief."), ChatMessage.User("Say hello.")];

    /// <summary>
    /// Acceptance of REQ-RAG-069: when a developer calls <c>UseChatGptSubscriptionLlm(callback)</c> in a test
    /// host, the callback receives the verification URL and the user code, and after the user authorises
    /// the builder's provider answers a chat call.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-069 BuilderCallbackGetsUrlAndCodeThenProviderAnswers")]
    public async Task BuilderCallbackGetsUrlAndCodeThenProviderAnswers()
    {
        var server = new FakeChatGptServer();
        SubscriptionSignInPrompt? prompt = null;
        var builder = new TechieRagBuilder().UseChatGptSubscriptionLlm(
            (shown, _) => { prompt = shown; server.Authorise(); return Task.CompletedTask; },
            Options(server));

        var provider = builder.CreateLlmProvider();
        var response = await provider.ChatAsync(Question);

        Assert.Equal(new Uri("https://auth.openai.com/codex/device"), prompt!.VerificationUri);
        Assert.Equal(FakeChatGptServer.UserCode, prompt.UserCode);
        Assert.Equal("Hello from ChatGPT.", response.Content);
    }

    /// <summary>The builder records the subscription connector as the configured LLM source.</summary>
    [Fact]
    public void BuilderReportsSubscriptionSource()
    {
        var builder = new TechieRagBuilder().UseChatGptSubscriptionLlm((_, _) => Task.CompletedTask);

        var provider = builder.CreateLlmProvider();

        Assert.IsType<ChatGptSubscriptionLlmProvider>(provider);
        Assert.Equal("gpt-6-luna", provider.ModelName);
    }

    /// <summary>The library keeps polling while the vendor answers "pending" and stops once authorised.</summary>
    [Fact(DisplayName = "REQ-RAG-069 PollWaitsUntilAuthorised")]
    public async Task PollWaitsUntilAuthorised()
    {
        var server = new FakeChatGptServer { PendingPolls = 3 };
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => Task.CompletedTask, Options(server));

        await provider.SignInAsync();

        Assert.Equal(4, server.Polls);
        Assert.Equal(1, server.CodeExchanges);
    }

    /// <summary>A code the user never authorises ends in <see cref="SubscriptionSignInException.CodeExpired"/>.</summary>
    [Fact]
    public async Task UnauthorisedCodeExpires()
    {
        var server = new FakeChatGptServer { PendingPolls = int.MaxValue };
        var now = DateTimeOffset.UtcNow;
        var options = Options(server);
        options.Clock = () => now;
        options.Delay = (interval, _) => { now += interval; return Task.CompletedTask; };
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => Task.CompletedTask, options);

        var failure = await Assert.ThrowsAsync<SubscriptionSignInException>(() => provider.SignInAsync());

        Assert.Equal(SubscriptionSignInException.CodeExpired, failure.Code);
    }

    /// <summary>After sign-in the session, with the account id from the identity token, is saved to the host's store.</summary>
    [Fact]
    public async Task SignInSavesSessionToStore()
    {
        var server = new FakeChatGptServer();
        var store = new InMemorySubscriptionSessionStore();
        var options = Options(server);
        options.SessionStore = store;
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => Task.CompletedTask, options);

        await provider.SignInAsync();
        var saved = await store.LoadAsync("chatgpt-subscription");

        Assert.Equal(FakeChatGptServer.AccountId, saved!.AccountId);
        Assert.Equal("refresh-1", saved.RefreshToken);
    }

    /// <summary>A session restored from the store is used as is: no sign-in callback, no code exchange.</summary>
    [Fact]
    public async Task RestoredSessionSkipsSignIn()
    {
        var server = new FakeChatGptServer();
        var store = new InMemorySubscriptionSessionStore();
        var token = FakeChatGptServer.Jwt(new { exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() });
        server.Accept(token);
        await store.SaveAsync(new SubscriptionSession { ConnectorName = "chatgpt-subscription", AccessToken = token, AccountId = "acct-restored" });
        var options = Options(server);
        options.SessionStore = store;
        var callbacks = 0;
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => { callbacks++; return Task.CompletedTask; }, options);

        await provider.ChatAsync(Question);

        Assert.Equal(0, callbacks);
        Assert.Equal(0, server.CodeExchanges);
    }

    /// <summary>A session about to expire is refreshed before the call, and the rotated session is saved.</summary>
    [Fact]
    public async Task ExpiringSessionIsRefreshed()
    {
        var server = new FakeChatGptServer();
        var store = new InMemorySubscriptionSessionStore();
        await store.SaveAsync(new SubscriptionSession
        {
            ConnectorName = "chatgpt-subscription",
            AccessToken = "stale",
            RefreshToken = "refresh-0",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        });
        var options = Options(server);
        options.SessionStore = store;
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => Task.CompletedTask, options);

        await provider.ChatAsync(Question);

        Assert.Equal(1, server.Refreshes);
        Assert.Equal("refresh-1", (await store.LoadAsync("chatgpt-subscription"))!.RefreshToken);
    }

    /// <summary>A 401 from the backend renews the session once and retries the call.</summary>
    [Fact]
    public async Task RevokedTokenRenewsOnceAndRetries()
    {
        var server = new FakeChatGptServer();
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => Task.CompletedTask, Options(server));
        await provider.SignInAsync();
        server.RevokeAll();

        var response = await provider.ChatAsync(Question);

        Assert.Equal("Hello from ChatGPT.", response.Content);
        Assert.Equal(1, server.Refreshes);
        Assert.Equal(2, server.BackendBodies.Count);
    }

    /// <summary>When the refresh token is refused too, the provider signs in again through the callback.</summary>
    [Fact]
    public async Task RefusedRefreshSignsInAgain()
    {
        var server = new FakeChatGptServer { RefuseRefresh = true };
        var callbacks = 0;
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => { callbacks++; return Task.CompletedTask; }, Options(server));
        await provider.SignInAsync();
        server.RevokeAll();

        await provider.ChatAsync(Question);

        Assert.Equal(2, callbacks);
    }

    /// <summary>
    /// The typed stream yields text deltas, then the whole tool call, then exactly one completed event
    /// last carrying the reported usage (REQ-RAG-067 contract).
    /// </summary>
    [Fact]
    public async Task TypedStreamOrdersTextToolCallCompleted()
    {
        var server = new FakeChatGptServer { ResponseStream = FakeChatGptServer.ToolCallStream() };
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => Task.CompletedTask, Options(server));

        var events = new List<LlmStreamEvent>();
        await foreach (var streamEvent in provider.ChatStreamEventsAsync(Question)) events.Add(streamEvent);

        Assert.Equal([LlmStreamEventKind.TextDelta, LlmStreamEventKind.ToolCall, LlmStreamEventKind.Completed], events.Select(e => e.Kind));
        Assert.Equal("get_weather", events[1].ToolCall!.Name);
        Assert.Equal("Paris", JsonDocument.Parse(events[1].ToolCall!.ArgumentsJson).RootElement.GetProperty("city").GetString());
        Assert.Equal("tool_calls", events[2].FinishReason);
        Assert.Equal(20, events[2].Usage!.InputTokens);
    }

    /// <summary><c>ChatStreamAsync</c> is the text projection of the typed stream and terminates.</summary>
    [Fact]
    public async Task TextStreamProjectsDeltas()
    {
        var server = new FakeChatGptServer();
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => Task.CompletedTask, Options(server));

        var pieces = new List<string>();
        await foreach (var piece in provider.ChatStreamAsync(Question)) pieces.Add(piece);

        Assert.Equal(["Hello ", "from ChatGPT."], pieces);
    }

    /// <summary>
    /// The backend request carries the bearer token and account header, turns system messages into
    /// instructions, sends tools in Responses-API form, and asks for a stream that is not stored.
    /// </summary>
    [Fact]
    public async Task RequestUsesResponsesApiShape()
    {
        var server = new FakeChatGptServer();
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => Task.CompletedTask, Options(server));
        var options = new LlmCompletionOptions
        {
            Tools = [new ToolDefinition { Name = "get_weather", Description = "Weather.", ParametersSchema = """{"type":"object"}""" }]
        };

        await provider.ChatAsync(Question, options);

        var body = JsonDocument.Parse(server.BackendBodies[0]).RootElement;
        Assert.Equal("Be brief.", body.GetProperty("instructions").GetString());
        Assert.Equal("input_text", body.GetProperty("input")[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("get_weather", body.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.False(body.GetProperty("store").GetBoolean());
        Assert.Equal(FakeChatGptServer.AccountId, server.BackendRequests[0].Headers.GetValues("ChatGPT-Account-Id").Single());
    }

    /// <summary>Concurrent first calls share one sign-in instead of starting one each.</summary>
    [Fact]
    public async Task ConcurrentCallsShareOneSignIn()
    {
        var server = new FakeChatGptServer();
        var callbacks = 0;
        using var provider = new ChatGptSubscriptionLlmProvider((_, _) => { Interlocked.Increment(ref callbacks); return Task.CompletedTask; }, Options(server));

        await Task.WhenAll(provider.ChatAsync(Question), provider.ChatAsync(Question), provider.ChatAsync(Question));

        Assert.Equal(1, callbacks);
    }

    /// <summary>A session's text form never contains its tokens, so logging one leaks nothing.</summary>
    [Fact]
    public void SessionTextHidesTokens()
    {
        var session = new SubscriptionSession { ConnectorName = "chatgpt-subscription", AccessToken = "secret-access", RefreshToken = "secret-refresh" };

        var text = session.ToString();

        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    private static ChatGptSubscriptionOptions Options(FakeChatGptServer server) => new()
    {
        Handler = server,
        Delay = (_, _) => Task.CompletedTask
    };
}
