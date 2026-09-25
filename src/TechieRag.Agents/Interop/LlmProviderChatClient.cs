using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using TechieRag.Abstractions;
using TechieRag.Models;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace TechieRag.Agents.Interop;

/// <summary>
/// Adapter 1: any TechieRag <see cref="ILlmProvider"/> as a Microsoft.Extensions.AI
/// <see cref="IChatClient"/> (REQ-RAG-017 / BRD-85; streaming REQ-RAG-068 / BRD-111).
/// </summary>
/// <remarks>
/// <para>Everything behind the provider is preserved because the provider itself is called: the six
/// built-in providers, <c>ModelRouter</c>, <c>RetryHandler</c>, <c>FallbackLlmHandler</c>, token events,
/// prompt caching and image parts. MEAI tools become <see cref="ToolDefinition"/>s built from each
/// function's raw JSON schema; the provider's tool calls come back as <see cref="FunctionCallContent"/>,
/// which is what lets <see cref="FunctionInvokingChatClient"/> and Agent Framework run the tool loop.</para>
/// <para><b>Streaming.</b> <see cref="GetStreamingResponseAsync"/> runs over
/// <see cref="ILlmProvider.ChatStreamEventsAsync"/>: text arrives as it is produced and each decided tool
/// call arrives as one <see cref="FunctionCallContent"/>, so a tool-using turn streams end to end.</para>
/// <para>The provider is not disposed by this adapter; the host that built it owns it.</para>
/// </remarks>
public sealed class LlmProviderChatClient : IChatClient
{
    private readonly ILlmProvider provider;
    private readonly ChatClientMetadata metadata;

    /// <summary>Creates the adapter.</summary>
    /// <param name="provider">The TechieRag provider to expose.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is null.</exception>
    public LlmProviderChatClient(ILlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        this.provider = provider;
        metadata = new ChatClientMetadata(provider.Name, null, provider.ModelName);
    }

    /// <summary>Gets the adapted provider.</summary>
    public ILlmProvider Provider => provider;

    /// <inheritdoc/>
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<MeaiChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var (coreMessages, coreOptions) = ChatOptionsMapper.ToCore(messages, options);
        var response = await provider.ChatAsync(coreMessages, coreOptions, cancellationToken).ConfigureAwait(false);

        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(response.Content)) contents.Add(new TextContent(response.Content));
        contents.AddRange((response.ToolCalls ?? []).Select(ChatMessageMapper.ToFunctionCall));

        return new ChatResponse(new MeaiChatMessage(ChatRole.Assistant, contents))
        {
            ModelId = string.IsNullOrEmpty(response.ModelName) ? provider.ModelName : response.ModelName,
            FinishReason = ToFinishReason(response.FinishReason),
            Usage = ToUsage(response.Usage)
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<MeaiChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var (coreMessages, coreOptions) = ChatOptionsMapper.ToCore(messages, options);
        var responseId = Guid.NewGuid().ToString("N");
        await foreach (var streamEvent in provider.ChatStreamEventsAsync(coreMessages, coreOptions, cancellationToken).ConfigureAwait(false))
        {
            yield return ToUpdate(streamEvent, responseId);
        }
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null) return null;
        if (serviceType == typeof(ChatClientMetadata)) return metadata;
        if (serviceType.IsInstanceOfType(this)) return this;
        return serviceType.IsInstanceOfType(provider) ? provider : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The provider belongs to whoever built it (often a TechieRag instance); nothing to release here.
    }

    private ChatResponseUpdate ToUpdate(LlmStreamEvent streamEvent, string responseId)
    {
        var update = new ChatResponseUpdate { Role = ChatRole.Assistant, ResponseId = responseId, ModelId = provider.ModelName };
        switch (streamEvent.Kind)
        {
            case LlmStreamEventKind.TextDelta:
                update.Contents = [new TextContent(streamEvent.Text)];
                break;
            case LlmStreamEventKind.ToolCall:
                update.Contents = [ChatMessageMapper.ToFunctionCall(streamEvent.ToolCall!)];
                break;
            default:
                update.Contents = streamEvent.Usage is { } usage ? [new UsageContent(ToUsage(usage))] : [];
                update.FinishReason = ToFinishReason(streamEvent.FinishReason);
                update.ModelId = streamEvent.ModelName ?? provider.ModelName;
                break;
        }

        return update;
    }

    private static UsageDetails ToUsage(TokenUsage usage) => new()
    {
        InputTokenCount = usage.InputTokens,
        OutputTokenCount = usage.OutputTokens,
        TotalTokenCount = usage.TotalTokens,
        CachedInputTokenCount = usage.CacheReadTokens > 0 ? usage.CacheReadTokens : null
    };

    private static ChatFinishReason? ToFinishReason(string? finishReason) => finishReason switch
    {
        null or "" => null,
        "stop" or "end_turn" => ChatFinishReason.Stop,
        "tool_calls" or "tool_use" => ChatFinishReason.ToolCalls,
        "length" or "max_tokens" => ChatFinishReason.Length,
        "content_filter" => ChatFinishReason.ContentFilter,
        _ => new ChatFinishReason(finishReason)
    };
}
