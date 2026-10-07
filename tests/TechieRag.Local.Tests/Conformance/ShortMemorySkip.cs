using TechieRag.Local;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace TechieRag.Local.Tests.Conformance;

/// <summary>
/// Turns a test that failed with <see cref="LocalModelMemoryException"/> into a skipped test carrying
/// the exception's own message. A machine without enough free memory for the model is not a defect in
/// the library: the memory gate refusing the load is the behaviour working as designed.
/// </summary>
/// <remarks>
/// xUnit 2 has no run-time skip, so the test case runs through <see cref="RunAsync"/>, which wraps the
/// message bus: a failure whose exception chain names <see cref="LocalModelMemoryException"/> is sent
/// on as <see cref="TestSkipped"/>, and the run summary is corrected to match. Every other failure is
/// passed through unchanged. Tests that expect the exception (<c>Assert.Throws</c>) pass and are not
/// touched.
/// </remarks>
internal static class ShortMemorySkip
{
    private static readonly string ExceptionTypeName = typeof(LocalModelMemoryException).FullName!;

    /// <summary>Runs <paramref name="run"/> with short-memory failures reported as skips.</summary>
    /// <param name="messageBus">The bus the test case would have written to.</param>
    /// <param name="run">The test case's own run, given the wrapping bus.</param>
    /// <returns>The run summary, with each converted failure counted as skipped.</returns>
    public static async Task<RunSummary> RunAsync(IMessageBus messageBus, Func<IMessageBus, Task<RunSummary>> run)
    {
        var bus = new SkippingBus(messageBus);
        var summary = await run(bus).ConfigureAwait(false);
        if (bus.Converted > 0)
        {
            summary.Failed -= bus.Converted;
            summary.Skipped += bus.Converted;
        }

        return summary;
    }

    private sealed class SkippingBus(IMessageBus inner) : IMessageBus
    {
        public int Converted { get; private set; }

        public bool QueueMessage(IMessageSinkMessage message)
        {
            if (message is ITestFailed failed && failed.ExceptionTypes.Contains(ExceptionTypeName))
            {
                var index = Array.IndexOf(failed.ExceptionTypes, ExceptionTypeName);
                Converted++;
                return inner.QueueMessage(new TestSkipped(failed.Test, "Skipped, not enough free memory on this machine: " + failed.Messages[index]));
            }

            return inner.QueueMessage(message);
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// A plain fact whose short-memory failures become skips (<see cref="ShortMemorySkip"/>). Used by
/// <c>LiveLocalLlmFact</c>; the conformance tests get the same through <see cref="ConformanceTestCase"/>.
/// </summary>
public sealed class ShortMemorySkippingTestCase : XunitTestCase
{
    /// <summary>For the de-serializer.</summary>
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public ShortMemorySkippingTestCase()
    {
    }

    /// <summary>Creates the test case for one test method.</summary>
    /// <param name="diagnosticMessageSink">The diagnostic sink.</param>
    /// <param name="defaultMethodDisplay">The default method display.</param>
    /// <param name="defaultMethodDisplayOptions">The default method display options.</param>
    /// <param name="testMethod">The test method.</param>
    public ShortMemorySkippingTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
    }

    /// <inheritdoc />
    public override Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource) =>
        ShortMemorySkip.RunAsync(messageBus, bus =>
            base.RunAsync(diagnosticMessageSink, bus, constructorArguments, aggregator, cancellationTokenSource));
}

/// <summary>Discovers <see cref="ShortMemorySkippingTestCase"/> for <c>LiveLocalLlmFact</c>.</summary>
public sealed class ShortMemorySkippingFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink diagnosticMessageSink;

    /// <summary>Creates the discoverer.</summary>
    /// <param name="diagnosticMessageSink">The diagnostic sink.</param>
    public ShortMemorySkippingFactDiscoverer(IMessageSink diagnosticMessageSink)
    {
        this.diagnosticMessageSink = diagnosticMessageSink;
    }

    /// <inheritdoc />
    public IEnumerable<IXunitTestCase> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute) =>
    [
        new ShortMemorySkippingTestCase(
            diagnosticMessageSink,
            discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(),
            testMethod)
    ];
}
