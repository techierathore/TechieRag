namespace TechieRag.Models;

/// <summary>Provider-agnostic prompt-caching controls (REQ-RAG-043 / BRD-124).</summary>
/// <remarks>
/// <para><b>What this is.</b> A passthrough. TechieRag caches nothing itself and stores no prompt —
/// it translates a caller's intent ("this prefix is stable, bill it as cached") into whatever each
/// provider's API calls that. The properties here are the intersection of intents the providers can
/// actually express; anything a given provider cannot express is simply not sent.</para>
/// <para><b>How each provider reads it:</b></para>
/// <list type="table">
/// <item><term>Anthropic</term><description>Real, explicit control. <see cref="CacheSystemPrompt"/> and
/// <see cref="CacheToolDefinitions"/> place a <c>cache_control</c> breakpoint on the system block and
/// the final tool definition; <see cref="ChatMessage.CacheBoundary"/> places one on that message.
/// <see cref="Ttl"/> selects the 5-minute or 1-hour tier. Reads back
/// <c>cache_read_input_tokens</c> and <c>cache_creation_input_tokens</c> into <see cref="TokenUsage"/>.</description></item>
/// <item><term>Gemini</term><description>Caching is a separate resource the caller creates out of band;
/// <see cref="ProviderCacheId"/> is passed through as <c>cachedContent</c>. The breakpoint flags have no
/// analogue and are not sent.</description></item>
/// <item><term>OpenAI-compatible, Azure AI Foundry, LM Studio</term><description>Caching is automatic on
/// the prefix. Only <see cref="CacheKey"/> is expressible, as <c>prompt_cache_key</c>, which asks the
/// service to route requests sharing a prefix to the same cache. Cached prompt tokens are read back
/// from <c>prompt_tokens_details.cached_tokens</c>.</description></item>
/// <item><term>Ollama</term><description>Local inference with no cache-control wire format. Nothing is
/// sent and nothing is claimed.</description></item>
/// </list>
/// <para>Setting an option a provider ignores is not an error. The alternative — throwing — would make
/// a single set of options unusable across a fallback chain that spans providers, which is precisely
/// the situation this library exists to support.</para>
/// </remarks>
public sealed class PromptCacheOptions
{
    /// <summary>Gets whether the system prompt should be marked cacheable.</summary>
    /// <remarks>Honoured by Anthropic. The system prompt is usually the largest stable prefix, so this
    /// is the single highest-value breakpoint in a RAG application.</remarks>
    public bool CacheSystemPrompt { get; init; }

    /// <summary>Gets whether tool definitions should be marked cacheable.</summary>
    /// <remarks>Honoured by Anthropic, which caches everything up to the breakpoint — so marking the
    /// last tool definition also covers the system prompt that precedes it.</remarks>
    public bool CacheToolDefinitions { get; init; }

    /// <summary>Gets the requested cache lifetime, or null for the provider default.</summary>
    /// <remarks>Anthropic offers two tiers. Anything under an hour maps to the 5-minute tier, an hour
    /// or more to the 1-hour tier; the exact value is not honoured because no provider accepts one.</remarks>
    public TimeSpan? Ttl { get; init; }

    /// <summary>Gets a stable key grouping requests that share a cacheable prefix, or null.</summary>
    /// <remarks>Sent as OpenAI's <c>prompt_cache_key</c>. Use a per-workspace or per-system-prompt value,
    /// never a per-user or per-request one — a unique key defeats the cache it is asking for.</remarks>
    public string? CacheKey { get; init; }

    /// <summary>Gets the identifier of a cache resource the caller created with the provider, or null.</summary>
    /// <remarks>Sent as Gemini's <c>cachedContent</c>, e.g. <c>cachedContents/abc123</c>. Creating and
    /// expiring that resource is the caller's job; TechieRag only references it.</remarks>
    public string? ProviderCacheId { get; init; }
}
