using TechieRag.Llm;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Llm.Live;

/// <summary>
/// Sevak's TR-RAG-063 repro against the real service (REQ-RAG-158): <c>claude-sonnet-5-5</c> with a temperature set
/// answers instead of refusing. Reads <c>Anthropic:ApiKey</c> from <c>livetests.settings.json</c>; one small billed call.
/// </summary>
[Trait("Category", LiveAnthropicFactAttribute.CategoryName)]
public sealed class LiveAnthropicSamplingTests
{
    /// <summary>A chat call with <c>Temperature = 0.3</c> on <c>claude-sonnet-5-5</c> returns text.</summary>
    [LiveAnthropicFact(DisplayName = "REQ-RAG-158 LiveSonnetFiveFiveAnswersWithTemperatureSet")]
    public async Task LiveSonnetFiveFiveAnswersWithTemperatureSet()
    {
        var provider = new AnthropicLlmProvider(LiveAnthropicFactAttribute.Key!, "claude-sonnet-5-5", maxTokens: 64);

        var response = await provider.ChatAsync(
            [ChatMessage.User("Reply with the single word: ready")],
            new LlmCompletionOptions { Temperature = 0.3f, TopP = 0.9f });

        Assert.False(string.IsNullOrWhiteSpace(response.Content));
    }
}
