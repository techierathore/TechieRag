using Xunit;

namespace TechieRag.Tests.Diagnostics;

/// <summary>
/// Serialises every test class that touches <c>TechieRagTelemetry.Enabled</c>.
/// </summary>
/// <remarks>
/// <para><c>Enabled</c> is process-wide static state. Before REQ-RAG-036 only one class mutated it,
/// so xUnit's per-class serialisation was enough. Now the exporter tests mutate it too — and an
/// exporter test that turns it on while <c>NoMeasurementsAreRecordedWhenDisabled</c> is asserting
/// silence would make that test flake. Sharing one collection puts both classes on the same thread
/// and removes the race rather than papering over it with retries.</para>
/// <para>This is a scheduling constraint only: it changes nothing about what either class asserts.</para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryCollection
{
    /// <summary>The collection name to put on <c>[Collection]</c>.</summary>
    public const string Name = "TechieRagTelemetry";
}
