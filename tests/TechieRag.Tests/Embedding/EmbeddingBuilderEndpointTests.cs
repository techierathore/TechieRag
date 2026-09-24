using System.Reflection;
using TechieRag.Abstractions;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// REQ-RAG-100 / BRD-147: each vendor entry point on the builder (<c>UseCohereEmbedding</c>,
/// <c>UseVoyageEmbedding</c>, <c>UseMistralEmbedding</c>, <c>UseGeminiEmbedding</c>) builds an embedder
/// whose requests go to that vendor's own API host.
/// </summary>
/// <remarks>
/// No vendor key exists on the build host, so no request is sent. What is asserted is the address
/// the built embedder's HTTP client sends to; the request and response wire shapes for each vendor
/// are asserted separately in <see cref="EmbeddingProviderTests"/>.
/// </remarks>
public sealed class EmbeddingBuilderEndpointTests
{
    /// <summary>The four vendor entry points and the API host each must reach.</summary>
    public static TheoryData<string, string> Vendors => new()
    {
        { "Cohere", "api.cohere.com" },
        { "Voyage", "api.voyageai.com" },
        { "Mistral", "api.mistral.ai" },
        { "Gemini", "generativelanguage.googleapis.com" }
    };

    /// <summary>
    /// Building with only the vendor's entry point and a key yields an embedder whose HTTP client is
    /// addressed to that vendor's API host.
    /// </summary>
    /// <param name="vendor">The vendor entry point called.</param>
    /// <param name="expectedHost">The vendor's API host.</param>
    [Theory(DisplayName = "REQ-RAG-100 VendorEntryPointTargetsTheVendorsApi")]
    [MemberData(nameof(Vendors))]
    public void VendorEntryPointTargetsTheVendorsApi(string vendor, string expectedHost)
    {
        var builder = new TechieRagBuilder()
            .UseSqliteVec(Path.Combine(Path.GetTempPath(), $"trembed-{Guid.NewGuid():N}.db"));
        builder = vendor switch
        {
            "Cohere" => builder.UseCohereEmbedding("test-key"),
            "Voyage" => builder.UseVoyageEmbedding("test-key"),
            "Mistral" => builder.UseMistralEmbedding("test-key"),
            "Gemini" => builder.UseGeminiEmbedding("test-key"),
            _ => throw new ArgumentOutOfRangeException(nameof(vendor))
        };

        var embedder = FieldOfType<IEmbeddingProvider>(builder.Build());
        var http = FieldOfType<HttpClient>(embedder);

        Assert.Equal(expectedHost, http.BaseAddress!.Host);
        Assert.Equal("https", http.BaseAddress.Scheme);
    }

    private static T FieldOfType<T>(object owner) where T : class =>
        owner.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.GetValue(owner))
            .OfType<T>()
            .First();
}
