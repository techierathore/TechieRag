using TechieRag.Local.Runtime;
using TechieRag.Local.Tests.Live;
using TechieRag.Local.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Local.Tests.Conformance;

/// <summary>
/// Runs the runtime-neutral conformance suite against ONNX Runtime GenAI with a real model
/// (REQ-RAG-058 / BRD-97): the model is <see cref="LiveLocalLlmFactAttribute.ModelToTest"/>, loaded from
/// the model root and checked against its pinned SHA-256 like any app's. Skipped with the live gate's
/// reason when the model is not downloaded (REQ-RAG-066).
/// </summary>
/// <remarks>
/// A real model answers the suite's prompts itself, so the scripting hooks do nothing: the JSON tests
/// rely on the schema guidance, and the length and cancellation tests on prompts that ask for a long
/// answer. Token counts are checked against the engine's own tokenizer, loaded separately.
/// </remarks>
[Collection(LiveLocalLlmCollection.Name)]
[RealRuntime]
public sealed class OnnxGenAiConformanceTests : LocalLlmConformanceTests
{
    private readonly CountingLocalLlmRuntime runtime = new(OnnxGenAiRuntime.Instance);

    /// <inheritdoc/>
    private protected override LocalLlmProvider CreateProvider(Action<LocalLlmOptions>? configure = null)
    {
        var options = new LocalLlmOptions { Model = LiveLocalLlmFactAttribute.ModelToTest };
        configure?.Invoke(options);
        return new LocalLlmProvider(options, null, runtime, MemoryGate.ForThisDevice, LocalModelStore.Shared, LocalModel.IsPhonePlatform);
    }

    /// <inheritdoc/>
    private protected override void PrepareJsonAnswer(string json)
    {
    }

    /// <inheritdoc/>
    private protected override void PrepareLongAnswer()
    {
    }

    /// <inheritdoc/>
    private protected override int ExpectedTokenCount(string text)
    {
        var model = LiveLocalLlmFactAttribute.ModelToTest;
        using var tokenizer = runtime.LoadTokenizer(model.GetDirectory(model.FindVariant(runtime.Format)));
        return tokenizer.CountTokens(text);
    }

    /// <inheritdoc/>
    private protected override void AssertNoInference() => Assert.Equal(0, runtime.GenerateCount);
}
