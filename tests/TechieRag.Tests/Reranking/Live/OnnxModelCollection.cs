using Xunit;

namespace TechieRag.Tests.Reranking.Live;

/// <summary>
/// Serialises the test classes that load a multi-gigabyte ONNX session (REQ-NFR-016).
/// </summary>
/// <remarks>
/// <para>xUnit runs test classes in parallel by default. The reranker and the BGE-M3 embedder each
/// build an <c>InferenceSession</c> over a 2.27 GB graph, so without this they can be resident
/// together — roughly 4.5 GB of model weights plus two ONNX Runtime thread pools competing for the
/// same cores, on top of whatever the rest of the suite is doing.</para>
/// <para><b>Prompted by an observed failure that did not reproduce.</b> One full-solution run showed
/// <c>HindiQueryRanksTheRelevantEnglishPassageHigher</c> failing 1.59s in, on a cold assembly, while
/// five other runs of the same commit were clean and the class passes consistently on its own. The
/// cause was never established, so this does not claim to be its fix — it removes the one condition
/// those runs did not share, which is the honest thing to do about resource contention that
/// REQ-NFR-016 already tracks as a known class of flake here.</para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OnnxModelCollection
{
    /// <summary>The collection name both ONNX-loading classes join.</summary>
    public const string Name = "onnx-model";
}
