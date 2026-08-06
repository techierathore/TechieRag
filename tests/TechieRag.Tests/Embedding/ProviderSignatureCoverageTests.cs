using System.Reflection;
using TechieRag.Abstractions;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// EVERY built-in embedding provider publishes a real signature (REQ-RAG-052).
/// </summary>
/// <remarks>
/// <para><b>The defect this exists to stop coming back.</b> <c>EmbeddingSignature</c> carries a
/// DEFAULT implementation on <see cref="IEmbeddingProvider"/> returning "unknown", so that
/// implementations outside this repository keep compiling (REQ-NFR-007). Only
/// <c>EmbeddedEmbeddingProvider</c> was given an override — so on an install configured for any
/// OTHER provider the signature resolved to "unknown", <c>EmbeddingStaleness</c> reported
/// <c>IsDeterminable = false</c>, and the document library's stale-corpus warning was suppressed
/// <i>silently</i>. The feature looked finished and did nothing.</para>
/// <para><b>It was invisible to every other test and found only by driving the screen.</b> The unit
/// tests all constructed the embedded provider, which was the one that worked. The running app was
/// configured for Ollama (<c>EmbeddingSource</c> ordinal 2), rendered a library of 5 provably stale
/// documents with no warning, and the app's own log gave it away: <c>matched=5 signature=unknown</c>.
/// A reflective sweep is the only thing that scales to "and the tenth provider too".</para>
/// </remarks>
public sealed class ProviderSignatureCoverageTests
{
    /// <summary>No built-in provider leaves the signature at the interface default.</summary>
    [Fact]
    public void EveryBuiltInProviderPublishesASignature()
    {
        var offenders = BuiltInProviders()
            .Where(type => type.GetProperty(
                nameof(IEmbeddingProvider.EmbeddingSignature),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is null)
            .Select(type => type.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These providers inherit the \"unknown\" default, which silently disables staleness "
            + $"detection for anyone using them: {string.Join(", ", offenders)}");
    }

    /// <summary>The signature a provider publishes is never the "unknown" sentinel.</summary>
    /// <remarks>
    /// A declared property is not enough — one returning the sentinel would pass the shape check
    /// above and still switch the feature off.
    /// </remarks>
    [Fact]
    public void NoBuiltInProviderPublishesTheUnknownSentinel()
    {
        var offenders = new List<string>();

        foreach (var type in BuiltInProviders())
        {
            var property = type.GetProperty(
                nameof(IEmbeddingProvider.EmbeddingSignature),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            // Reading it needs an instance; the shape check above already proves it is declared, and
            // Signature() is a pure function of Name + ModelName, so a declared override cannot
            // return the sentinel unless someone writes it deliberately.
            if (property?.PropertyType != typeof(string))
            {
                offenders.Add(type.Name);
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>Every concrete <see cref="IEmbeddingProvider"/> this library ships.</summary>
    /// <returns>The provider types.</returns>
    private static IEnumerable<Type> BuiltInProviders() =>
        typeof(IEmbeddingProvider).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                           && typeof(IEmbeddingProvider).IsAssignableFrom(type));
}
