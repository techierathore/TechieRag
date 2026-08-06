using TechieRag.Models;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// Detecting a corpus that no longer matches what queries are embedded with (REQ-RAG-052 /
/// TR-RAG-044).
/// </summary>
/// <remarks>
/// <para><b>The failure this guards against is silent.</b> When the embedding encoding changes,
/// retrieval does not throw and does not come back empty — it returns confident, wrong results,
/// because cosine similarity between two different vector spaces is a number like any other. The
/// TR-RAG-044 tokenizer fix created exactly that situation, and nothing in the product could tell.
/// These tests are what make it visible.</para>
/// </remarks>
public sealed class EmbeddingStalenessTests
{
    private const string Current = "Embedded-ONNX/bge-m3/r2";
    private const string Previous = "Embedded-ONNX/bge-m3/r1";

    /// <summary>A corpus embedded by the current provider is clean.</summary>
    [Fact]
    public void AMatchingCorpusIsNotStale()
    {
        var report = EmbeddingStaleness.Analyze(
            [Stamped("doc-1", Current), Stamped("doc-2", Current)], Current);

        Assert.True(report.IsDeterminable);
        Assert.False(report.IsStale);
        Assert.Equal(2, report.TotalDocuments);
        Assert.Empty(report.StaleDocuments);
    }

    /// <summary>
    /// A document with NO stamp is stale, not "probably fine" — it is the population this exists for.
    /// </summary>
    /// <remarks>
    /// Every document ingested before 2026-08-04 is unstamped, and those are precisely the vectors
    /// the tokenizer fix invalidated. Treating a missing stamp as a pass would make the check report
    /// a clean corpus in the one situation it was built to catch.
    /// </remarks>
    [Fact]
    public void AnUnstampedDocumentIsStale()
    {
        var report = EmbeddingStaleness.Analyze([Unstamped("doc-1")], Current);

        var stale = Assert.Single(report.StaleDocuments);
        Assert.Equal("doc-1", stale.DocumentId);
        Assert.Equal(EmbeddingStalenessReason.Unstamped, stale.Reason);
        Assert.Null(stale.Signature);
        Assert.True(report.IsEntirelyStale);
    }

    /// <summary>A document stamped by an earlier REVISION of the same provider and model is stale.</summary>
    /// <remarks>
    /// The case a provider/model comparison alone would miss, and the one that actually happened:
    /// TR-RAG-044 changed neither the provider nor the model, only the encoding.
    /// </remarks>
    [Fact]
    public void AnEarlierRevisionOfTheSameModelIsStale()
    {
        var report = EmbeddingStaleness.Analyze([Stamped("doc-1", Previous)], Current);

        var stale = Assert.Single(report.StaleDocuments);
        Assert.Equal(EmbeddingStalenessReason.DifferentSignature, stale.Reason);
        Assert.Equal(Previous, stale.Signature);
    }

    /// <summary>
    /// A corpus holding both current and stale vectors is reported as MIXED, distinctly from
    /// entirely-stale.
    /// </summary>
    /// <remarks>
    /// This is the worse state and the reason a partial re-ingest is dangerous: the store now holds
    /// two incomparable spaces at once, so some queries retrieve sensibly and others do not, with no
    /// pattern a user could report. All-stale is the ordinary post-change state and calls for a full
    /// re-ingest; mixed means one was started and not finished.
    /// </remarks>
    [Fact]
    public void APartiallyReIngestedCorpusIsReportedAsMixed()
    {
        var report = EmbeddingStaleness.Analyze(
            [Stamped("doc-1", Current), Stamped("doc-2", Previous), Unstamped("doc-3")], Current);

        Assert.True(report.IsStale);
        Assert.True(report.IsMixed);
        Assert.False(report.IsEntirelyStale);
        Assert.Equal(2, report.StaleCount);
        Assert.Equal(3, report.TotalDocuments);
    }

    /// <summary>
    /// A provider that publishes no signature yields "cannot determine", NOT a clean result.
    /// </summary>
    /// <remarks>
    /// Returning an empty stale list here would be the same class of lie the whole requirement exists
    /// to remove — a pass that was never actually established. <c>IsDeterminable</c> is what a caller
    /// must read first.
    /// </remarks>
    [Theory]
    [InlineData(EmbeddingStaleness.UnknownSignature)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AProviderWithNoSignatureCannotDetermineAnything(string? signature)
    {
        var report = EmbeddingStaleness.Analyze([Unstamped("doc-1"), Stamped("doc-2", Previous)], signature);

        Assert.False(report.IsDeterminable);
        Assert.False(report.IsStale);
        Assert.Equal(EmbeddingStaleness.UnknownSignature, report.CurrentSignature);
        Assert.Equal(2, report.TotalDocuments);
    }

    /// <summary>An empty store is clean, and not "entirely stale" on a count of zero.</summary>
    [Fact]
    public void AnEmptyStoreIsNeitherStaleNorEntirelyStale()
    {
        var report = EmbeddingStaleness.Analyze([], Current);

        Assert.True(report.IsDeterminable);
        Assert.False(report.IsStale);
        Assert.False(report.IsEntirelyStale);
        Assert.False(report.IsMixed);
    }

    /// <summary>A stamp present but blank counts as no stamp.</summary>
    /// <remarks>
    /// An empty value tells a consumer nothing, and comparing it as a string would let it pass as a
    /// match against an equally empty current signature.
    /// </remarks>
    [Fact]
    public void ABlankStampCountsAsUnstamped()
    {
        var report = EmbeddingStaleness.Analyze([Stamped("doc-1", "   ")], Current);

        Assert.Equal(EmbeddingStalenessReason.Unstamped, Assert.Single(report.StaleDocuments).Reason);
    }

    /// <summary>The signature is built from provider, model AND revision.</summary>
    /// <remarks>
    /// Asserted because the revision is the component that earns this feature its keep — without it
    /// the TR-RAG-044 change would have been invisible to this check.
    /// </remarks>
    [Fact]
    public void TheSignatureCarriesProviderModelAndRevision()
    {
        Assert.Equal("Embedded-ONNX/bge-m3/r2", EmbeddingStaleness.Signature("Embedded-ONNX", "bge-m3", 2));

        Assert.NotEqual(
            EmbeddingStaleness.Signature("Embedded-ONNX", "bge-m3", 1),
            EmbeddingStaleness.Signature("Embedded-ONNX", "bge-m3", 2));
    }

    /// <summary>Different models on the same provider do not match each other.</summary>
    [Fact]
    public void ADifferentModelIsADifferentSignature()
    {
        var report = EmbeddingStaleness.Analyze(
            [Stamped("doc-1", EmbeddingStaleness.Signature("Ollama", "nomic-embed-text"))],
            EmbeddingStaleness.Signature("Ollama", "bge-m3"));

        Assert.True(report.IsStale);
        Assert.Equal(EmbeddingStalenessReason.DifferentSignature, Assert.Single(report.StaleDocuments).Reason);
    }

    /// <summary>Builds a document carrying an embedding signature.</summary>
    /// <param name="id">Document id.</param>
    /// <param name="signature">The stamp it carries.</param>
    /// <returns>The document.</returns>
    private static Document Stamped(string id, string signature) => new()
    {
        Id = id,
        Name = $"{id}.txt",
        SourcePath = $"/corpus/{id}.txt",
        Metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [DocumentMetadataKeys.EmbeddingSignature] = signature
        }
    };

    /// <summary>Builds a document from before stamping existed.</summary>
    /// <param name="id">Document id.</param>
    /// <returns>The document.</returns>
    private static Document Unstamped(string id) => new()
    {
        Id = id,
        Name = $"{id}.txt",
        SourcePath = $"/corpus/{id}.txt",
        Metadata = new Dictionary<string, object>(StringComparer.Ordinal)
    };
}
