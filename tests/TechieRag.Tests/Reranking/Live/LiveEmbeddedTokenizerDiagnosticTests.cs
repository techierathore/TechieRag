using TechieRag.Embedded;
using Xunit;

namespace TechieRag.Tests.Reranking.Live;

/// <summary>
/// The BGE-M3 embedder encodes text the way XLM-RoBERTa expects (REQ-RAG-052 / TR-RAG-044).
/// </summary>
/// <remarks>
/// <para><b>Where these came from.</b> Fixing <c>OnnxCrossEncoderReranker</c> showed it fed RAW
/// SentencePiece ids to an XLM-RoBERTa graph, which needs them shifted by one. BGE-M3 is also
/// XLM-RoBERTa based and <c>EmbeddedEmbeddingProvider</c> tokenized the same way — no shift, and no
/// <c>&lt;s&gt;</c>/<c>&lt;/s&gt;</c> wrapper at all. It was wrong in the same way, in the provider
/// that produced every stored vector.</para>
/// <para><b>The same-language test is the weak one and is kept anyway.</b> A consistent id shift
/// leaves lexical overlap intact, so English retrieval looked fine throughout and proved almost
/// nothing — which is precisely how the defect survived. Its job here is to catch a regression that
/// breaks the ordinary path. The CROSS-LANGUAGE test is the one with teeth, and it is the one that
/// found this.</para>
/// <para><b>Measured before and after (2026-08-04), Hindi query against English passages:</b>
/// relevant <c>0.3536</c> / irrelevant <c>0.3642</c> — wrong passage winning, both at noise level —
/// became relevant <c>0.7182</c>, a genuine multilingual match.</para>
/// </remarks>
[Trait("Category", LiveRerankerFactAttribute.CategoryName)]
[Collection(OnnxModelCollection.Name)]
public sealed class LiveEmbeddedTokenizerDiagnosticTests
{
    /// <summary>Runs when the BGE-M3 weights are staged.</summary>
    /// <remarks>
    /// These were opt-in while the defect was open, so a machine with the model cached did not go red
    /// over a decision that belonged to the owner. That decision is made and the fix is in, so they
    /// run by default now — a regression here is a real failure, and leaving them behind a flag would
    /// mean nobody ever saw it.
    /// </remarks>
    private sealed class EmbedderDiagnosticFactAttribute : FactAttribute
    {
        public EmbedderDiagnosticFactAttribute()
        {
            if (!IsBgeM3Staged)
            {
                Skip = $"The bge-m3 weights are not staged at {BgeM3Directory}.";
            }
        }
    }

    /// <summary>Gets the shared cache directory the BGE-M3 weights are staged in.</summary>
    private static string BgeM3Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "techierag-models", "bge-m3");

    /// <summary>Gets whether a complete BGE-M3 model is present.</summary>
    private static bool IsBgeM3Staged
    {
        get
        {
            var data = Path.Combine(BgeM3Directory, "model.onnx_data");

            return File.Exists(Path.Combine(BgeM3Directory, "model.onnx"))
                && File.Exists(Path.Combine(BgeM3Directory, "sentencepiece.bpe.model"))
                && File.Exists(data)
                && new FileInfo(data).Length > 2_000_000_000;
        }
    }

    private const string Question = "What is the capital of France?";
    private const string HindiQuestion = "फ्रांस की राजधानी क्या है?";
    private const string Relevant = "Paris is the capital city of France.";
    private const string Irrelevant = "Bicycles should have their chains oiled regularly.";

    /// <summary>Same-language retrieval ranks the relevant passage higher.</summary>
    /// <remarks>
    /// The baseline. If this fails the embedder is broken outright; if it passes it proves much less
    /// than it appears to, because lexical overlap survives a shifted vocabulary.
    /// </remarks>
    [EmbedderDiagnosticFact]
    public async Task EnglishQueryRanksTheRelevantPassageHigher()
    {
        var (relevant, irrelevant) = await SimilaritiesAsync(Question);

        Assert.True(
            relevant > irrelevant,
            $"English: relevant={relevant:F4} did not beat irrelevant={irrelevant:F4}.");
    }

    /// <summary>
    /// Cross-language retrieval ranks the relevant passage higher — the claim REQ-NFR-006 makes for
    /// BGE-M3's 100+ languages, and the assertion that exposed the reranker's defect.
    /// </summary>
    [EmbedderDiagnosticFact]
    public async Task HindiQueryRanksTheRelevantEnglishPassageHigher()
    {
        var (relevant, irrelevant) = await SimilaritiesAsync(HindiQuestion);

        Assert.True(
            relevant > irrelevant,
            $"Hindi→English: relevant={relevant:F4} did not beat irrelevant={irrelevant:F4}. "
            + "BGE-M3 is multilingual, so this failing points at the tokenizer, not the model.");

        // "Higher" is too weak a claim to be worth much: before the fix these sat at 0.3536 and
        // 0.3642 — both noise, differing in the third decimal, and the wrong one won. A genuine
        // BGE-M3 cross-lingual match is far above that, so the threshold is what actually
        // distinguishes a working tokenizer from a lucky ordering.
        Assert.True(
            relevant > 0.60,
            $"Hindi→English similarity {relevant:F4} is at noise level, not a real match.");
    }

    /// <summary>Embeds the query and both passages and returns the two cosine similarities.</summary>
    /// <param name="query">The question to embed.</param>
    /// <returns>Similarity to the relevant passage, and to the irrelevant one.</returns>
    private static async Task<(double Relevant, double Irrelevant)> SimilaritiesAsync(string query)
    {
        using var provider = new EmbeddedEmbeddingProvider(BgeM3Directory);

        var vectors = await provider.EmbedBatchAsync([query, Relevant, Irrelevant]);

        return (Cosine(vectors[0], vectors[1]), Cosine(vectors[0], vectors[2]));
    }

    /// <summary>Cosine similarity between two vectors.</summary>
    /// <param name="left">First vector.</param>
    /// <param name="right">Second vector.</param>
    /// <returns>The similarity.</returns>
    private static double Cosine(float[] left, float[] right)
    {
        double dot = 0, leftMagnitude = 0, rightMagnitude = 0;

        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftMagnitude += left[i] * left[i];
            rightMagnitude += right[i] * right[i];
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude) + 1e-12);
    }
}
