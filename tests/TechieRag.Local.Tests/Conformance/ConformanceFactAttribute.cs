using Xunit;
using Xunit.Sdk;

namespace TechieRag.Local.Tests.Conformance;

/// <summary>
/// A <see cref="FactAttribute"/> for the conformance suite's tests: each subclass of
/// <see cref="LocalLlmConformanceTests"/> gets its own case, named after the subclass, and a subclass
/// marked <see cref="RealRuntimeAttribute"/> is skipped with the live gate's reason when this host has
/// no runtime or no downloaded model (REQ-RAG-058, REQ-RAG-066).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer("TechieRag.Local.Tests.Conformance.ConformanceFactDiscoverer", "TechieRag.Local.Tests")]
public sealed class ConformanceFactAttribute : FactAttribute
{
}
