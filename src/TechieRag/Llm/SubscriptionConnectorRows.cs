namespace TechieRag.Llm;

/// <summary>
/// The <see cref="LlmSource.Subscription"/> rows of <see cref="LlmConnectorCatalog"/>, one per vendor
/// whose subscription sign-in was researched (REQ-RAG-070 / BRD-113, REQ-FN-062 / BRD-114).
/// </summary>
/// <remarks>
/// <para><b>Every row carries the vendor's stated terms and the date checked.</b> The research and its
/// sources are in <c>DECISIONS.md</c> (2026-09-24). Only a row with
/// <see cref="SubscriptionTerms.Permitted"/> true has a builder method; the others exist so a host can
/// show the user why a vendor is not offered.</para>
/// <para><b>No model prefixes.</b> A subscription connector serves the same model names as the vendor's
/// API-key connector, so it is selected only explicitly, e.g. <c>chatgpt-subscription/gpt-6-luna</c>;
/// a bare <c>gpt-</c> name keeps routing to the API-key <c>openai</c> row.</para>
/// </remarks>
internal static class SubscriptionConnectorRows
{
    /// <summary>The date every row below was checked against the vendor's live documentation.</summary>
    internal static readonly DateOnly CheckedOn = new(2026, 9, 24);

    /// <summary>The ChatGPT subscription connector's key.</summary>
    internal const string ChatGptName = "chatgpt-subscription";

    /// <summary>Gets the rows, permitted vendor first.</summary>
    internal static LlmConnectorDescriptor[] All =>
    [
        Row(ChatGptName, "ChatGPT subscription (OpenAI)", "https://chatgpt.com/backend-api/codex", "gpt-6-luna", new SubscriptionTerms
        {
            Permitted = true,
            Terms = "Permitted. OpenAI states you can use your ChatGPT account in other tools (\"Developers should code in the "
                + "tools they prefer, whether that's Codex, OpenCode, Cline, pi, OpenClaw, or something else\"). The permission is "
                + "a public statement, not a clause in OpenAI's terms, and OpenAI issues no client id to third parties; the "
                + "sign-in uses Codex's public client.",
            AppliesTo = "Individual ChatGPT plans (Free, Go, Plus, Pro); Business and Enterprise workspaces only where the admin enables device-code sign-in.",
            CheckedOn = CheckedOn,
            Sources = ["https://developers.openai.com/community/codex-for-oss", "https://learn.chatgpt.com/docs/auth.md", "https://x.com/thsottiaux/status/2058071172361998482"],
            BuilderMethod = "UseChatGptSubscriptionLlm"
        }),
        Row("claude-subscription", "Claude subscription (Anthropic)", null, null, new SubscriptionTerms
        {
            Permitted = false,
            Terms = "Not permitted. \"The use of OAuth tokens obtained via Claude Free, Pro, or Max accounts in any other product, "
                + "tool, or service — including the Agent SDK — is not permitted and constitutes a violation of the Consumer Terms "
                + "of Service.\" Use an Anthropic API key (connector 'anthropic').",
            AppliesTo = "Claude Free, Pro and Max.",
            CheckedOn = CheckedOn,
            Sources = ["https://code.claude.com/docs/en/legal-and-compliance"]
        }),
        Row("gemini-subscription", "Gemini subscription (Google)", null, null, new SubscriptionTerms
        {
            Permitted = false,
            Terms = "Not permitted. \"Directly accessing the services powering Gemini CLI (for example, the Gemini Code Assist "
                + "service) using third-party software, tools, or services ... is a violation of applicable terms and policies.\" "
                + "Use a Gemini API key (connector 'gemini') or Vertex AI.",
            AppliesTo = "Personal Google accounts and Google AI Pro / Ultra.",
            CheckedOn = CheckedOn,
            Sources = ["https://geminicli.com/docs/resources/tos-privacy/"]
        }),
        Row("grok-subscription", "Grok subscription (xAI)", null, null, new SubscriptionTerms
        {
            Permitted = false,
            Terms = "Not confirmed. xAI has published no terms permitting third-party apps to use a SuperGrok or X Premium "
                + "subscription and no client registration; the third-party apps that offer it cite no xAI source. Use an xAI "
                + "API key (connector 'xai').",
            AppliesTo = "SuperGrok and X Premium+.",
            CheckedOn = CheckedOn,
            Sources = ["https://docs.x.ai/grok/faq"]
        }),
        Row("groq-subscription", "Groq", null, null, new SubscriptionTerms
        {
            Permitted = false,
            Terms = "No subscription sign-in exists. GroqCloud is API-key only and its Services Agreement says the Cloud "
                + "Services are not for consumer use. Use a Groq API key (connector 'groq').",
            AppliesTo = "Not applicable.",
            CheckedOn = CheckedOn,
            Sources = ["https://console.groq.com/docs/legal/services-agreement"]
        }),
        Row("meta-subscription", "Meta AI (Meta)", null, null, new SubscriptionTerms
        {
            Permitted = false,
            Terms = "No subscription sign-in exists. Meta AI offers no sign-in that lets another app use it, and the Llama API "
                + "was an API-key developer preview. Use a host that serves Llama models (e.g. 'groq/<model>').",
            AppliesTo = "Not applicable.",
            CheckedOn = CheckedOn,
            Sources = ["https://dev.meta.ai/"]
        })
    ];

    private static LlmConnectorDescriptor Row(string name, string displayName, string? endpoint, string? defaultModel, SubscriptionTerms terms) =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            Source = LlmSource.Subscription,
            Endpoint = endpoint,
            DefaultModel = defaultModel,
            RequiresApiKey = false,
            Subscription = terms
        };
}
