using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// Serialises the tests that move the process-wide model root (REQ-RAG-053).
/// </summary>
/// <remarks>
/// <c>ModelRoot.Set</c> changes a static for the whole process; run in parallel with the reranker and
/// native-load tests, which read the default root, it would make them flaky. A collection with
/// parallelisation disabled runs on its own.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModelRootCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "ModelRoot";
}
