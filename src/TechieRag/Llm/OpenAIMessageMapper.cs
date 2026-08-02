using TechieRag.Models;

namespace TechieRag.Llm;

/// <summary>
/// Shared wire-shape mapping for the OpenAI chat-completions family.
/// </summary>
/// <remarks>
/// <para>Three providers speak this dialect — <see cref="OpenAICompatibleLlmProvider"/>,
/// <see cref="LmStudioLlmProvider"/> and <see cref="AzureAIFoundryLlmProvider"/>. Multimodal content
/// (REQ-RAG-039) and prompt-cache passthrough (REQ-RAG-043) map identically for all three, so the
/// mapping lives here once. Three copies of an image-encoding rule is three places for it to drift.</para>
/// <para>Internal: this is a serialization detail of the built-in providers, not a contract offered to
/// consumers. Consumers get the modality through <see cref="Models.ChatContentPart"/>.</para>
/// </remarks>
internal static class OpenAIMessageMapper
{
    /// <summary>
    /// Builds the <c>content</c> value for one message: a plain string, or the parts array when the
    /// message carries images (REQ-RAG-039).
    /// </summary>
    /// <param name="message">The message being mapped.</param>
    /// <returns>A string for text-only messages, otherwise a list of typed content parts.</returns>
    public static object BuildContent(ChatMessage message)
    {
        if (message.Parts is not { Count: > 0 })
        {
            return message.Content ?? string.Empty;
        }

        var parts = new List<object>(message.Parts.Count);

        foreach (var part in message.Parts)
        {
            if (part.Kind == ChatContentKind.Image && part.Image is not null)
            {
                // OpenAI takes one field for both shapes: a fetchable URL, or the bytes as a data URI.
                // That is why ChatImage keeps the two apart but renders to a single string here.
                var url = part.Image.IsInline
                    ? part.Image.ToDataUri()
                    : part.Image.Url!.ToString();

                parts.Add(new Dictionary<string, object>
                {
                    ["type"] = "image_url",
                    ["image_url"] = new Dictionary<string, object> { ["url"] = url }
                });
            }
            else if (part.Text is not null)
            {
                parts.Add(new Dictionary<string, object>
                {
                    ["type"] = "text",
                    ["text"] = part.Text
                });
            }
        }

        // An images-only message that produced nothing means every part was empty. Falling back to
        // the string form keeps the request valid rather than sending an empty array the API rejects.
        return parts.Count > 0 ? parts : message.Content ?? string.Empty;
    }

    /// <summary>
    /// Applies the expressible part of the caller's cache intent to an OpenAI-style request (REQ-RAG-043).
    /// </summary>
    /// <param name="request">The request dictionary being built.</param>
    /// <param name="cache">The caller's cache options, or null.</param>
    /// <remarks>
    /// Prefix caching on these services is automatic and cannot be switched on or off per request, so
    /// <c>CacheSystemPrompt</c>, <c>CacheToolDefinitions</c> and <c>Ttl</c> have no wire representation
    /// and are deliberately not sent. Only <c>prompt_cache_key</c> is real here: it asks the service to
    /// route requests sharing a prefix to the same cache shard.
    /// </remarks>
    public static void ApplyPromptCache(Dictionary<string, object> request, PromptCacheOptions? cache)
    {
        if (!string.IsNullOrEmpty(cache?.CacheKey))
        {
            request["prompt_cache_key"] = cache.CacheKey;
        }
    }
}
