using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace TechieRag.Tests.Llm;

/// <summary>
/// REQ-RAG-046 / BRD-161: image generation, realtime audio, batch, fine-tuning, moderation and OCR
/// endpoints are deferred, so no public type or member of the shipped packages claims them.
/// </summary>
/// <remarks>
/// The BRD row says "deferred"; this test is the code half of the acceptance. It reads the public
/// surface by reflection rather than grepping source, so a claim added under any file name is found.
/// Batch EMBEDDING (<c>EmbedBatchAsync</c>) and batch UPSERT are ordinary shipped operations, not the
/// deferred asynchronous batch-job API, and are deliberately not matched.
/// </remarks>
public sealed class DeferredEndpointTests
{
    private static readonly Regex DeferredName = new(
        "ImageGenerat|GenerateImage|Realtime|RealTime|BatchJob|CreateBatch|FineTun|Finetun|Moderat|Ocr(?![a-z])|OpticalCharacter",
        RegexOptions.CultureInvariant);

    /// <summary>No public type or public member in the three packages names a deferred endpoint.</summary>
    [Fact(DisplayName = "REQ-RAG-046 NoPublicSurfaceClaimsADeferredEndpoint")]
    public void NoPublicSurfaceClaimsADeferredEndpoint()
    {
        var assemblies = new[]
        {
            typeof(ITechieRag).Assembly,
            typeof(TechieRag.Embedded.EmbeddedEmbeddingProvider).Assembly,
            typeof(TechieRag.Telemetry.TechieRagTelemetryOptions).Assembly
        };

        var claims = assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => new[] { type.FullName ?? type.Name }
                .Concat(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(member => $"{type.FullName}.{member.Name}")))
            .Where(name => DeferredName.IsMatch(name))
            .ToList();

        Assert.True(claims.Count == 0, "Deferred endpoints are claimed by: " + string.Join(", ", claims));
    }
}
