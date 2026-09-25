using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using TechieRag.Local.Runtime;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// The ONNX Runtime GenAI runtime without a model: which platforms get it, and how every generation
/// setting maps onto GenAI's search options (REQ-RAG-058 / BRD-97, REQ-RAG-059, REQ-RAG-063).
/// </summary>
public sealed class OnnxGenAiRuntimeTests
{
    /// <summary>
    /// This desktop gets ONNX Runtime GenAI, in its ONNX format, from the platform selector: the one
    /// engine per DECISIONS.md 2026-09-25.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-058 SelectorReturnsOnnxGenAiHere")]
    public void SelectorReturnsOnnxGenAiHere()
    {
        var runtime = LocalRuntimeSelector.ForCurrentPlatform();

        Assert.IsType<OnnxGenAiRuntime>(runtime);
        Assert.Equal(LocalModelFormat.OnnxGenAi, runtime.Format);
    }

    /// <summary>The selector refuses an architecture ONNX Runtime GenAI ships no native build for.</summary>
    [Fact]
    public void SelectorRefusesUnsupportedArchitecture() =>
        Assert.False(LocalRuntimeSelector.IsSupported(Architecture.Wasm));

    /// <summary>Temperature 0 is greedy: no sampling, and neither temperature nor top-p is sent.</summary>
    [Fact]
    public void ZeroTemperatureIsGreedy()
    {
        var search = OnnxGenAiSearchOptions.From(Settings(temperature: 0f), promptTokens: 10, contextSize: 4_096);

        Assert.Equal((false, (double?)null, (double?)null), (search.DoSample, search.Temperature, search.TopP));
    }

    /// <summary>A positive temperature samples with the call's temperature, top-p and seed.</summary>
    [Fact]
    public void SamplingCarriesTemperatureTopPAndSeed()
    {
        var search = OnnxGenAiSearchOptions.From(Settings(temperature: 0.5f) with { Seed = 9 }, promptTokens: 10, contextSize: 4_096);

        Assert.Equal((true, 0.5, 0.9, 9.0), (search.DoSample, search.Temperature!.Value, Math.Round(search.TopP!.Value, 3), search.Seed!.Value));
    }

    /// <summary>max_length is prompt plus answer tokens, never more than the context size.</summary>
    [Fact]
    public void MaxLengthIsPromptPlusAnswerWithinContext()
    {
        Assert.Equal(110, OnnxGenAiSearchOptions.From(Settings(maxTokens: 100), promptTokens: 10, contextSize: 4_096).MaxLength);
        Assert.Equal(64, OnnxGenAiSearchOptions.From(Settings(maxTokens: 100), promptTokens: 10, contextSize: 64).MaxLength);
    }

    /// <summary>
    /// Frequency and presence penalty, which GenAI lacks, map onto its repetition penalty with their
    /// direction kept; neither set leaves the model's own value.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-059 PenaltiesMapToRepetitionPenalty")]
    public void PenaltiesMapToRepetitionPenalty()
    {
        Assert.Null(OnnxGenAiSearchOptions.RepetitionPenaltyFrom(null, null));
        Assert.Equal(1.2, OnnxGenAiSearchOptions.RepetitionPenaltyFrom(0.1f, 0.3f)!.Value, 3);
        Assert.Equal(0.75, OnnxGenAiSearchOptions.RepetitionPenaltyFrom(-0.5f, null)!.Value, 3);
        Assert.Equal(2.0, OnnxGenAiSearchOptions.RepetitionPenaltyFrom(2f, 2f)!.Value, 3);
        Assert.Equal(0.5, OnnxGenAiSearchOptions.RepetitionPenaltyFrom(-2f, -2f)!.Value, 3);
    }

    /// <summary>
    /// A JSON schema becomes the json_schema guidance; JSON mode without a schema is guided to any
    /// object; plain text is unconstrained.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-063 JsonRequestsBecomeGuidance")]
    public void JsonRequestsBecomeGuidance()
    {
        const string schema = """{"type":"object","properties":{"name":{"type":"string"}}}""";

        Assert.Equal(schema, OnnxGenAiSearchOptions.From(Settings() with { JsonMode = true, JsonSchema = schema }, 1, 64).JsonSchema);
        Assert.Equal(OnnxGenAiSearchOptions.AnyObjectSchema, OnnxGenAiSearchOptions.From(Settings() with { JsonMode = true }, 1, 64).JsonSchema);
        Assert.Null(OnnxGenAiSearchOptions.From(Settings(), 1, 64).JsonSchema);
    }

    /// <summary>
    /// The load overlay caps the default length at the context size and, when threads are set, sets
    /// ONNX Runtime's intra-op thread count.
    /// </summary>
    [Fact]
    public void OverlayCarriesContextAndThreads()
    {
        var withThreads = JsonNode.Parse(OnnxGenAiModel.OverlayJson(2_048, 4))!;
        var withoutThreads = JsonNode.Parse(OnnxGenAiModel.OverlayJson(2_048, null))!;

        Assert.Equal(2_048, withThreads["search"]!["max_length"]!.GetValue<int>());
        Assert.Equal(4, withThreads["model"]!["decoder"]!["session_options"]!["intra_op_num_threads"]!.GetValue<int>());
        Assert.Null(withoutThreads["model"]);
    }

    private static LocalGenerationSettings Settings(float temperature = 0.7f, int maxTokens = 16) =>
        new() { MaxTokens = maxTokens, Temperature = temperature, TopP = 0.9f };
}
