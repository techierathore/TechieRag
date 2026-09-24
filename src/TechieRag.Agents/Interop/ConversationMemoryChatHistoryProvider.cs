using Microsoft.Agents.AI;
using TechieRag.Abstractions;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace TechieRag.Agents.Interop;

/// <summary>
/// Adapter 4: a TechieRag <see cref="IConversationMemory"/> as an Agent Framework
/// <see cref="ChatHistoryProvider"/> (REQ-RAG-017 / BRD-85).
/// </summary>
/// <remarks>
/// <para>Before each run the memory's history is handed to the agent; after it, the run's new request
/// and response messages are appended to the memory. Whatever persists the memory (the in-memory
/// default, <c>DbConversationMemory</c> over an <c>IConversationStore</c>) keeps doing so, so a MAF agent
/// shares one conversation with the classic loop.</para>
/// <para>One memory is one conversation: the memory's own <see cref="IConversationMemory.ConversationId"/>
/// decides which thread is read and written, not the <see cref="AgentSession"/>. Use one provider (one
/// agent) per conversation.</para>
/// </remarks>
public sealed class ConversationMemoryChatHistoryProvider : ChatHistoryProvider
{
    private readonly IConversationMemory memory;

    /// <summary>Creates the adapter.</summary>
    /// <param name="memory">The conversation memory to read and write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="memory"/> is null.</exception>
    public ConversationMemoryChatHistoryProvider(IConversationMemory memory)
        : base(null, null, null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        this.memory = memory;
    }

    /// <summary>Gets the adapted memory.</summary>
    public IConversationMemory Memory => memory;

    /// <inheritdoc/>
    protected override async ValueTask<IEnumerable<MeaiChatMessage>> ProvideChatHistoryAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var history = await memory.GetHistoryAsync(cancellationToken).ConfigureAwait(false);
        return ChatMessageMapper.ToMeai(history);
    }

    /// <inheritdoc/>
    protected override async ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.InvokeException is not null) return;

        var newMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []);
        foreach (var message in ChatMessageMapper.ToCore(newMessages))
        {
            await memory.AddMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }
}
