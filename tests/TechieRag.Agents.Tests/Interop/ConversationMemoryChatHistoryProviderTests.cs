using Microsoft.Extensions.AI;
using TechieRag.Agents.Interop;
using TechieRag.Agents.Tests.TestDoubles;
using TechieRag.Services;
using Xunit;
using CoreChatMessage = TechieRag.Models.ChatMessage;

namespace TechieRag.Agents.Tests.Interop;

/// <summary>
/// Tests for adapter 4, <see cref="ConversationMemoryChatHistoryProvider"/> (REQ-RAG-017 / BRD-85).
/// </summary>
public class ConversationMemoryChatHistoryProviderTests
{
    /// <summary>
    /// Acceptance for adapter 4: the agent reads prior turns from a TechieRag <c>IConversationMemory</c>
    /// and writes the new turn back into it, unchanged.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-017 AgentReadsAndWritesConversationMemory")]
    public async Task AgentReadsAndWritesConversationMemory()
    {
        var memory = new InMemoryConversationMemory("thread-1");
        await memory.AddMessageAsync(CoreChatMessage.User("My order number is 4411."));
        await memory.AddMessageAsync(CoreChatMessage.Assistant("Noted."));
        var model = new ScriptedChatClient(ScriptedChatClient.Answer("Your order is 4411."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create())
            .UseCustomChatClient(() => model)
            .WithChatHistoryProvider(new ConversationMemoryChatHistoryProvider(memory))
            .Build();

        await agent.AskAsync("What is my order number?");

        Assert.Contains(model.Calls[0], m => m.Role == ChatRole.User && m.Text == "My order number is 4411.");
        var history = await memory.GetHistoryAsync();
        Assert.Equal("What is my order number?", history[^2].Content);
        Assert.Equal("Your order is 4411.", history[^1].Content);
        Assert.Equal(4, history.Count);
    }
}
