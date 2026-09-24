using TechieRag.Abstractions;

namespace TechieRag.Llm;

/// <summary>
/// Settings for <see cref="ChatGptSubscriptionLlmProvider"/> (REQ-RAG-069 / BRD-112).
/// </summary>
/// <remarks>
/// Every default is the value OpenAI's own open-source Codex client uses, as checked on 2026-09-24
/// (<c>DECISIONS.md</c>, REQ-FN-062). They are settable so a host can follow a vendor change without
/// waiting for a library release.
/// </remarks>
public sealed class ChatGptSubscriptionOptions
{
    /// <summary>The public OAuth client id of OpenAI's open-source Codex client.</summary>
    /// <remarks>OpenAI issues no client id to third parties; its public statements endorse tools that
    /// sign in through this client (DECISIONS.md 2026-09-24).</remarks>
    public const string CodexPublicClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    /// <summary>Gets or sets the model, e.g. <c>gpt-6-luna</c> (available on every ChatGPT plan when checked).</summary>
    public string Model { get; set; } = "gpt-6-luna";

    /// <summary>Gets or sets where the session is kept; null means a new <see cref="InMemorySubscriptionSessionStore"/>.</summary>
    public ISubscriptionSessionStore? SessionStore { get; set; }

    /// <summary>Gets or sets the OAuth client id.</summary>
    public string ClientId { get; set; } = CodexPublicClientId;

    /// <summary>Gets or sets OpenAI's authorisation server.</summary>
    public Uri Issuer { get; set; } = new("https://auth.openai.com");

    /// <summary>Gets or sets the base of the Codex backend that serves subscription model calls.</summary>
    public Uri BackendEndpoint { get; set; } = new("https://chatgpt.com/backend-api/codex");

    /// <summary>Gets or sets how long the user has to authorise; OpenAI's codes expire after 15 minutes.</summary>
    public TimeSpan SignInTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets the instructions sent when the conversation has no system message.</summary>
    /// <remarks>The Codex backend requires instructions on every call. Model-facing, invariant English.</remarks>
    public string DefaultInstructions { get; set; } = "You are a helpful assistant.";

    /// <summary>Gets or sets the handler all requests go through; null means the library's guarded handler.</summary>
    /// <remarks>Test seam: a fake authorisation server and backend are plugged in here.</remarks>
    internal HttpMessageHandler? Handler { get; set; }

    /// <summary>Gets or sets how the device-code poll waits between attempts.</summary>
    /// <remarks>Test seam: tests replace it so a poll does not sleep for real.</remarks>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    /// <summary>Gets or sets the clock.</summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
}
