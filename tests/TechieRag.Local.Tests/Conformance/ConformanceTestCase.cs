using System.Reflection;
using TechieRag.Local.Tests.Live;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace TechieRag.Local.Tests.Conformance;

/// <summary>
/// One conformance test run by one subclass: its display name ends with the subclass's name, and a
/// <see cref="RealRuntimeAttribute"/> subclass is skipped with <see cref="LiveLocalLlmFactAttribute.SkipReason"/>
/// when the host cannot run it.
/// </summary>
public sealed class ConformanceTestCase : XunitTestCase
{
    /// <summary>For the xUnit deserializer only.</summary>
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public ConformanceTestCase()
    {
    }

    /// <summary>Creates the test case.</summary>
    /// <param name="diagnosticMessageSink">xUnit's diagnostic sink.</param>
    /// <param name="defaultMethodDisplay">The default method display.</param>
    /// <param name="defaultMethodDisplayOptions">The default method display options.</param>
    /// <param name="testMethod">The test method.</param>
    public ConformanceTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
    }

    /// <inheritdoc/>
    protected override string GetDisplayName(IAttributeInfo factAttribute, string displayName)
    {
        var name = base.GetDisplayName(factAttribute, displayName);
        var className = TestMethod.TestClass.Class.ToRuntimeType().Name;
        return name.Contains(className, StringComparison.Ordinal) ? name : $"{name} ({className})";
    }

    /// <inheritdoc/>
    protected override string GetSkipReason(IAttributeInfo factAttribute)
    {
        var testClass = TestMethod.TestClass.Class.ToRuntimeType();
        return testClass.GetCustomAttribute<RealRuntimeAttribute>() is null
            ? base.GetSkipReason(factAttribute)
            : LiveLocalLlmFactAttribute.SkipReason()!;
    }
}
