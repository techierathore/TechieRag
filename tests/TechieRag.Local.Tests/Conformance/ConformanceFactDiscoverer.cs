using Xunit.Abstractions;
using Xunit.Sdk;

namespace TechieRag.Local.Tests.Conformance;

/// <summary>Discovers a <see cref="ConformanceFactAttribute"/> test as a <see cref="ConformanceTestCase"/>.</summary>
public sealed class ConformanceFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink diagnosticMessageSink;

    /// <summary>Creates the discoverer.</summary>
    /// <param name="diagnosticMessageSink">xUnit's diagnostic sink.</param>
    public ConformanceFactDiscoverer(IMessageSink diagnosticMessageSink)
    {
        this.diagnosticMessageSink = diagnosticMessageSink;
    }

    /// <inheritdoc/>
    public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute) =>
    [
        new ConformanceTestCase(
            diagnosticMessageSink,
            discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(),
            testMethod)
    ];
}
