using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// Serialises tests that go through the process-wide <c>ModelDownloadService.Instance</c>, whose
/// events and one-download-at-a-time gate are shared.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModelDownloadCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "ModelDownloads";
}
