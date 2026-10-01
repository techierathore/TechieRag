using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// Live check of REQ-RAG-109 against OpenCode Go (Chatur TR-RAG-003): the <c>opencode-go</c> connector
/// answers instead of 400 <c>MissingSessionID</c>.
/// </summary>
/// <remarks>The key is read from <see cref="LiveOpenCodeGoFactAttribute.KeyVariable"/> only; it is never
/// written to the repository.</remarks>
[Trait("Category", LiveOpenCodeGoFactAttribute.CategoryName)]
public class LiveOpenCodeGoTests
{
    /// <summary>
    /// Acceptance of REQ-RAG-109: a chat call and a streamed call through <c>UseConnectorLlm("opencode-go")</c>
    /// with one conversation's session id both answer.
    /// </summary>
    [LiveOpenCodeGoFact(DisplayName = "REQ-RAG-109 LiveOpenCodeGoAnswersWithSessionHeader")]
    public async Task LiveOpenCodeGoAnswersWithSessionHeader()
    {
        var provider = new TechieRagBuilder()
            .UseConnectorLlm(LlmConnectorCatalog.OpenCodeGoName, LiveOpenCodeGoFactAttribute.Key)
            .CreateLlmProvider();
        var options = new LlmCompletionOptions { SessionId = $"techierag-live-{Guid.NewGuid():N}", MaxTokens = 400 };
        IReadOnlyList<ChatMessage> question = [ChatMessage.User("Reply with the single word OK.")];

        var response = await provider.ChatAsync(question, options);
        var streamed = new List<LlmStreamEvent>();
        await foreach (var streamEvent in provider.ChatStreamEventsAsync(question, options))
        {
            streamed.Add(streamEvent);
        }

        Assert.True(response.Usage.OutputTokens > 0);
        Assert.Contains(streamed, e => e.Kind == LlmStreamEventKind.Completed);
    }
}

/// <summary>Runs the live OpenCode Go test only when a key is set.</summary>
public sealed class LiveOpenCodeGoFactAttribute : FactAttribute
{
    /// <summary>The trait value these tests are filtered by.</summary>
    public const string CategoryName = "LiveOpenCodeGo";

    /// <summary>Environment variable holding the OpenCode Go key.</summary>
    public const string KeyVariable = "TechieRagOpenCodeGoKey";

    /// <summary>Initializes a new instance of the <see cref="LiveOpenCodeGoFactAttribute"/> class.</summary>
    public LiveOpenCodeGoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            Skip = $"Live OpenCode Go needs a subscription key. Set {KeyVariable} to run it.";
        }
    }

    /// <summary>Gets the key, or an empty string when none is set.</summary>
    public static string Key => Environment.GetEnvironmentVariable(KeyVariable) ?? string.Empty;
}
