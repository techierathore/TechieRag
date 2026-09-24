using TechieRag.Models;
using TechieRag.Services;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// REQ-RAG-096 / BRD-143 (TR-RAG-009): <see cref="PromptTemplateEngine"/> signals truncation the
/// same way <see cref="WorkspaceManager"/> does — a <c>ContextTruncated</c> event carrying
/// <see cref="ContextTruncatedEventArgs"/> — instead of cutting context silently.
/// </summary>
public sealed class PromptContextTruncationTests
{
    /// <summary>
    /// Eight results against a budget of five: the caller is told, with the counts, and the prompt
    /// holds exactly the five that were kept.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-096 DroppedChunksRaiseContextTruncated")]
    public void DroppedChunksRaiseContextTruncated()
    {
        var engine = new PromptTemplateEngine(new PromptConfig { MaxContextChunks = 5 });
        ContextTruncatedEventArgs? signal = null;
        engine.ContextTruncated += (_, args) => signal = args;

        var messages = engine.BuildRagPrompt("when does it renew?", Results(8));

        Assert.NotNull(signal);
        Assert.True(signal!.Context.WasTruncated);
        Assert.Equal(3, signal.Context.EvictedCount);
        Assert.Equal(5, signal.Context.Results.Count);
        Assert.Equal(5, signal.Context.MaxContextChunks);
        Assert.Equal("when does it renew?", signal.Question);
        Assert.Contains("chunk 5", messages[0].Content);
        Assert.DoesNotContain("chunk 6", messages[0].Content);
    }

    /// <summary>The chat prompt path signals too, since it formats context the same way.</summary>
    [Fact(DisplayName = "REQ-RAG-096 TheChatPromptAlsoSignalsTruncation")]
    public void TheChatPromptAlsoSignalsTruncation()
    {
        var engine = new PromptTemplateEngine(new PromptConfig { MaxContextChunks = 2 });
        var raised = 0;
        engine.ContextTruncated += (_, _) => raised++;

        engine.BuildRagChatPrompt("and then?", Results(3), [ChatMessage.User("earlier")]);

        Assert.Equal(1, raised);
    }

    /// <summary>A context that fits raises nothing, so the event is a trustworthy negative signal.</summary>
    [Fact]
    public void ContextThatFitsRaisesNothing()
    {
        var engine = new PromptTemplateEngine(new PromptConfig { MaxContextChunks = 5 });
        var raised = false;
        engine.ContextTruncated += (_, _) => raised = true;

        engine.BuildRagPrompt("q", Results(5));

        Assert.False(raised);
    }

    /// <summary>
    /// A budget of zero or less disables trimming, exactly as it does in
    /// <see cref="WorkspaceManager"/>: every result reaches the prompt and nothing is signalled.
    /// </summary>
    [Fact]
    public void NonPositiveBudgetKeepsEveryChunk()
    {
        var engine = new PromptTemplateEngine(new PromptConfig { MaxContextChunks = 0 });
        var raised = false;
        engine.ContextTruncated += (_, _) => raised = true;

        var messages = engine.BuildRagPrompt("q", Results(7));

        Assert.False(raised);
        Assert.Contains("chunk 7", messages[0].Content);
    }

    private static List<SearchResult> Results(int count) =>
        Enumerable.Range(1, count)
            .Select(index => TestData.Result($"doc-{index}", $"chunk {index}", 1f - (index * 0.01f)))
            .ToList();
}
