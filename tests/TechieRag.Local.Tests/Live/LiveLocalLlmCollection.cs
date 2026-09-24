using Xunit;

namespace TechieRag.Local.Tests.Live;

/// <summary>
/// Runs the live local-model tests one at a time: each loads gigabytes of weights, and two at once
/// would measure memory pressure rather than the model (REQ-RAG-066 / BRD-107).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveLocalLlmCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "LiveLocalLlm";
}
