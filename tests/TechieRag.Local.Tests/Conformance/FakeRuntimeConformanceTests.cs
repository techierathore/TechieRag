using TechieRag.Local.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Local.Tests.Conformance;

/// <summary>
/// Runs the runtime-neutral conformance suite against the scripted runtime (REQ-RAG-058). When the
/// owner has chosen a runtime per platform, a sibling class runs the same suite against it.
/// </summary>
public sealed class FakeRuntimeConformanceTests : LocalLlmConformanceTests
{
    private readonly FakeLocalLlmRuntime runtime = new();

    /// <inheritdoc/>
    private protected override LocalLlmProvider CreateProvider(Action<LocalLlmOptions>? configure = null) =>
        FakeProviderFactory.Create(runtime, configure);

    /// <inheritdoc/>
    private protected override void PrepareJsonAnswer(string json) =>
        runtime.Script = (_, settings) => settings.JsonSchema is null ? ["Hello"] : Pieces(json);

    /// <inheritdoc/>
    private protected override void PrepareLongAnswer() =>
        runtime.Script = (_, _) => Enumerable.Range(1, 200).Select(n => $" {n}");

    /// <inheritdoc/>
    private protected override int ExpectedTokenCount(string text) => FakeLocalModel.Count(text);

    /// <inheritdoc/>
    private protected override void AssertNoInference() => Assert.Equal(0, runtime.GenerateCount);

    private static IEnumerable<string> Pieces(string text) => text.Chunk(4).Select(c => new string(c));
}
