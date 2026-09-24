using System.Reflection;
using Xunit;

namespace TechieRag.Tests;

/// <summary>
/// REQ-FN-068 / BRD-162: live tests are gated by attributes that skip with a stated reason, so a host
/// without live credentials runs every other test and reports the live ones as skipped, not failed.
/// </summary>
/// <remarks>
/// The rest of the acceptance — every non-live test passes on such a host — is the result of the
/// test run this class is part of. This class guards the gating itself: that every live test is
/// behind a gate, and that a gate which skips always says why.
/// </remarks>
public sealed class LiveTestGatingTests
{
    /// <summary>
    /// Every <see cref="FactAttribute"/> subclass declared in this assembly is used by at least one test,
    /// and each one, constructed on this host, either runs (no skip) or skips with a reason of more than
    /// a few words.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-068 EveryLiveGateSkipsWithAReason")]
    public void EveryLiveGateSkipsWithAReason()
    {
        var gates = GateTypes().ToList();
        Assert.NotEmpty(gates);

        foreach (var gate in gates)
        {
            Assert.Contains(TestMethods(), method => method.GetCustomAttributes(gate, inherit: false).Length > 0);

            var instance = (FactAttribute)Activator.CreateInstance(gate, nonPublic: true)!;
            if (instance.Skip is not null)
            {
                Assert.True(
                    instance.Skip.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4,
                    $"{gate.Name} skips with no usable reason: '{instance.Skip}'.");
            }
        }
    }

    /// <summary>
    /// Every live test class (a class named <c>Live*Tests</c>) runs its live tests behind one of the
    /// gates, so none of them reaches a real service unconditionally on a host without credentials.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-068 LiveTestClassesUseGatedAttributes")]
    public void LiveTestClassesUseGatedAttributes()
    {
        var gates = GateTypes().ToHashSet();
        var liveClasses = typeof(LiveTestGatingTests).Assembly.GetTypes()
            .Where(type => type != typeof(LiveTestGatingTests)
                && type.Name.StartsWith("Live", StringComparison.Ordinal)
                && type.Name.EndsWith("Tests", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(liveClasses);

        foreach (var liveClass in liveClasses)
        {
            var tests = liveClass.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.GetCustomAttributes<FactAttribute>(inherit: false).Any())
                .ToList();
            Assert.Contains(tests, method => method.GetCustomAttributes<FactAttribute>(inherit: false).Any(attribute => gates.Contains(attribute.GetType())));
        }
    }

    private static IEnumerable<Type> GateTypes() =>
        typeof(LiveTestGatingTests).Assembly.GetTypes()
            .Where(type => typeof(FactAttribute).IsAssignableFrom(type) && !type.IsAbstract && type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) is not null);

    private static IEnumerable<MethodInfo> TestMethods() =>
        typeof(LiveTestGatingTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
}
