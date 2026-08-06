using TechieDesk.Services.Threads;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Threads;

/// <summary>
/// Tests for the workspace chat history preparation used by the library's workspace-scoped
/// streaming RAG call (REQ-RAG-009 / REQ-RAG-013).
/// </summary>
public class ChatHistoryTrimmerTests
{
    /// <summary>
    /// The just-persisted question is dropped so the RAG chat template does not append it twice.
    /// </summary>
    [Fact]
    public void DropsTrailingUserTurn()
    {
        var history = new List<ChatMessage>
        {
            ChatMessage.User("earlier"),
            ChatMessage.Assistant("reply"),
            ChatMessage.User("current question")
        };

        var prior = ChatHistoryTrimmer.PriorTurns(history);

        Assert.Equal(2, prior.Count);
        Assert.Equal("reply", prior[^1].Content);
    }

    /// <summary>History ending on an assistant turn is passed through unchanged.</summary>
    [Fact]
    public void KeepsHistoryEndingOnAssistantTurn()
    {
        var history = new List<ChatMessage> { ChatMessage.User("q"), ChatMessage.Assistant("a") };

        var prior = ChatHistoryTrimmer.PriorTurns(history);

        Assert.Equal(2, prior.Count);
    }

    /// <summary>An empty history stays empty, so the first question streams without prior turns.</summary>
    [Fact]
    public void HandlesEmptyHistory()
    {
        Assert.Empty(ChatHistoryTrimmer.PriorTurns([]));
    }
}
