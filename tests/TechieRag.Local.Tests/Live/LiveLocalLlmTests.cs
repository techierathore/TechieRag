using TechieRag.Local.Tests.Conformance;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Local.Tests.Live;

/// <summary>
/// The local model for real: one answer, typed JSON on three schemas, and token counts from the
/// model's own tokenizer (REQ-RAG-057, REQ-RAG-063, REQ-RAG-065; gated per REQ-RAG-066).
/// </summary>
[Collection(LiveLocalLlmCollection.Name)]
public sealed class LiveLocalLlmTests
{
    /// <summary>
    /// Token counts of <see cref="LocalLlmConformanceTests.TokenCountSamples"/>, without a
    /// beginning-of-text token, as both candidate runtimes' tokenizers gave them on 2026-09-24
    /// (llama.cpp through LLamaSharp 0.27.0 and ONNX Runtime GenAI 0.16.0 agreed on every one).
    /// </summary>
    private static readonly Dictionary<string, int[]> MeasuredCounts = new(StringComparer.Ordinal)
    {
        [LocalModel.Qwen25Instruct05B.Id] = [10, 13, 12],
        [LocalModel.Phi3Mini4kInstruct.Id] = [12, 14, 13]
    };

    /// <summary>The platform's local model answers a prompt with text.</summary>
    [LiveLocalLlmFact(DisplayName = "REQ-RAG-057 LiveAnswersOnePrompt")]
    public async Task LiveAnswersOnePrompt()
    {
        using var provider = CreateProvider();

        var response = await provider.CompleteAsync("Write one sentence about the sea.", new LlmCompletionOptions { MaxTokens = 48 });

        Assert.False(string.IsNullOrWhiteSpace(response.Content));
        Assert.True(response.Usage.OutputTokens > 0);
    }

    /// <summary>CompleteAsync&lt;T&gt; returns JSON that parses into T for the three test schemas.</summary>
    [LiveLocalLlmFact(DisplayName = "REQ-RAG-063 LiveTypedAnswersParse")]
    public async Task LiveTypedAnswersParse()
    {
        using var provider = CreateProvider();

        var person = await provider.CompleteAsync<LocalLlmConformanceTests.PersonAnswer>("Give the name and age at death of Ada Lovelace.");
        var place = await provider.CompleteAsync<LocalLlmConformanceTests.PlaceAnswer>("Where is Paris? Give the city, country and continent.");
        var colours = await provider.CompleteAsync<LocalLlmConformanceTests.ColourListAnswer>("List the three primary colours of light.");

        Assert.False(string.IsNullOrWhiteSpace(person.Name));
        Assert.False(string.IsNullOrWhiteSpace(place.Location.Country));
        Assert.NotEmpty(colours.Colours);
    }

    /// <summary>EstimateTokenCount equals the runtime tokenizer's count on the fixed test strings.</summary>
    [LiveLocalLlmFact(DisplayName = "REQ-RAG-065 LiveTokenCountsMatchRuntimeTokenizer")]
    public async Task LiveTokenCountsMatchRuntimeTokenizer()
    {
        using var provider = CreateProvider();
        await provider.LoadAsync();
        var expected = MeasuredCounts[provider.ModelName];

        var counts = LocalLlmConformanceTests.TokenCountSamples.Select(provider.EstimateTokenCount).ToArray();

        Assert.Equal(expected, counts);
    }

    private static LocalLlmProvider CreateProvider() =>
        new(new LocalLlmOptions { Model = LiveLocalLlmFactAttribute.ModelToTest, Temperature = 0f });
}
