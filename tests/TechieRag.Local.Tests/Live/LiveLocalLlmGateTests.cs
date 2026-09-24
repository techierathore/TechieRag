using System.Reflection;
using Xunit;

namespace TechieRag.Local.Tests.Live;

/// <summary>
/// REQ-RAG-066 / BRD-107: the gate on the live local-model tests. On a host with no runtime or no
/// downloaded model every live test is skipped with a printed reason, so nothing fails; the live tests
/// share one non-parallel collection.
/// </summary>
public sealed class LiveLocalLlmGateTests
{
    /// <summary>
    /// Acceptance of REQ-RAG-066: every test marked <see cref="LiveLocalLlmFactAttribute"/> carries the
    /// host's skip reason — a non-empty sentence naming what is missing when this host has no local
    /// model, and no skip at all when it has one — and every class holding such tests is in the
    /// <see cref="LiveLocalLlmCollection"/>, which disables parallel execution.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-066 LiveTestsSkipWithReasonWhenNoModel")]
    public void LiveTestsSkipWithReasonWhenNoModel()
    {
        var hostReason = LiveLocalLlmFactAttribute.SkipReason();
        var liveTests = typeof(LiveLocalLlmGateTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => (Method: method, Fact: method.GetCustomAttribute<LiveLocalLlmFactAttribute>()))
            .Where(entry => entry.Fact is not null)
            .ToList();

        Assert.NotEmpty(liveTests);
        foreach (var (method, fact) in liveTests)
        {
            Assert.Equal(hostReason, fact!.Skip);

            var collection = method.DeclaringType!.GetCustomAttributesData()
                .SingleOrDefault(data => data.AttributeType == typeof(CollectionAttribute));
            Assert.True(collection is not null, $"{method.DeclaringType.Name} is not in a test collection.");
            Assert.Equal(LiveLocalLlmCollection.Name, collection.ConstructorArguments[0].Value);
        }

        if (hostReason is not null)
        {
            Assert.StartsWith("Live local-model test.", hostReason, StringComparison.Ordinal);
            Assert.True(hostReason.Length > "Live local-model test.".Length + 10, "The skip reason must say what is missing.");
        }

        var definition = typeof(LiveLocalLlmCollection).GetCustomAttribute<CollectionDefinitionAttribute>();
        Assert.NotNull(definition);
        Assert.True(definition.DisableParallelization);
    }
}
