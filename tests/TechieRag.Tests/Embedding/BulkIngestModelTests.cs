using System.Diagnostics;
using TechieRag.Embedded;
using TechieRag.Processors;
using TechieRag.Tests.Reranking.Live;
using Xunit;
using Xunit.Abstractions;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// REQ-RAG-165 (Sevak TR-RAG-065): the embedded bge-m3 took about two seconds a chunk on a computer's
/// processor, so a folder of notes took more than a day to become searchable.
/// </summary>
/// <remarks>
/// <para><b>Where the time went.</b> Profiled with ONNX Runtime on the real weights: 88 percent of each chunk is
/// the model's 32-bit float matrix multiplications. Tokenizing (about 8 ms a chunk), pooling and the copy out
/// are noise; more threads (8 instead of 4) and denormal flushing changed nothing; batching eight chunks into
/// one padded pass was slower (2,119 ms against 1,544 ms a chunk). The cut is integer arithmetic: the same
/// graph with 8-bit weights, <see cref="EmbeddedModel.BgeM3Int8"/>.</para>
/// <para><b>The same input for both.</b> <c>Fixtures/bulk-ingest-sample.md</c> (the opening of Sevak's
/// feedback file, fixed on 2026-11-19), cut into 500-character chunks with a 50-character overlap, Sevak's
/// settings. The live test runs where both models are staged in the shared cache,
/// <c>~/.cache/techierag-models/bge-m3</c> and <c>~/.cache/techierag-models/bge-m3-int8</c>.</para>
/// </remarks>
[Trait("Category", LiveRerankerFactAttribute.CategoryName)]
[Collection(OnnxModelCollection.Name)]
public sealed class BulkIngestModelTests
{
    private const string Commit = "4de13258303883538bd53b696b452bf8099f0858";

    private readonly ITestOutputHelper output;

    /// <summary>Initializes a new instance of the <see cref="BulkIngestModelTests"/> class.</summary>
    /// <param name="output">Where the measured figures are written.</param>
    public BulkIngestModelTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// The package offers the 8-bit bge-m3 by name: 1024 dimensions like bge-m3, under 0.6 GB, desktop only, and
    /// every file pinned to one commit of its repository.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-165 Int8ModelIsOfferedPinned")]
    public void Int8ModelIsOfferedPinned()
    {
        var model = EmbeddedModel.FromName("bge-m3-int8");

        Assert.NotNull(model);
        Assert.Equal(EmbeddedModel.BgeM3.Dimensions, model.Dimensions);
        Assert.Equal(EmbeddedModel.BgeM3.MaxSequenceLength, model.MaxSequenceLength);
        Assert.False(model.RunsOnPhones);
        Assert.InRange(model.DownloadBytes, 500_000_000, 600_000_000);
        Assert.All(model.GetDownloadFiles(), file => Assert.Contains(Commit, file.Url.ToString()));
        Assert.Contains(model.GetDownloadFiles(), file => file.FileName == "model.onnx" && file.Url.ToString().EndsWith("onnx/sentence_transformers_int8.onnx"));
        Assert.Contains(model, EmbeddedModel.All);
    }

    /// <summary>
    /// A provider created from a folder of bge-m3-int8 files names that model, so its vectors are never stamped
    /// as bge-m3's and mixed with them; a bge-m3 folder still reads as bge-m3.
    /// </summary>
    [BothModelsStagedFact(DisplayName = "REQ-RAG-165 Int8FolderIsNotStampedAsBgeM3")]
    public void Int8FolderIsNotStampedAsBgeM3()
    {
        using var int8 = new EmbeddedEmbeddingProvider(BothModelsStagedFactAttribute.Int8Directory);
        using var full = new EmbeddedEmbeddingProvider(BothModelsStagedFactAttribute.FullDirectory);

        Assert.Equal("bge-m3-int8", int8.ModelName);
        Assert.Equal("bge-m3", full.ModelName);
        Assert.NotEqual(full.EmbeddingSignature, int8.EmbeddingSignature);
    }

    /// <summary>
    /// On the same chunks, one at a time as ingestion embeds them, the 8-bit model takes at most 60 percent of
    /// bge-m3's time (measured 43 percent), and its vectors stay bge-m3's: cosine at least 0.95 for every chunk,
    /// and a Hindi question still ranks the English passage that answers it first.
    /// </summary>
    /// <returns>A task that completes when both models have embedded the sample.</returns>
    [BothModelsStagedFact(DisplayName = "REQ-RAG-165 Int8EmbedsTheSampleInUnderSixtyPercentOfTheTime")]
    public async Task Int8EmbedsTheSampleInUnderSixtyPercentOfTheTime()
    {
        var sample = LibraryRepoFiles.Read("tests/TechieRag.Tests/Embedding/Fixtures/bulk-ingest-sample.md");
        var chunks = TextChunker.ChunkText(sample, 500, 50).Take(12).ToList();
        string[] probe = ["फ्रांस की राजधानी क्या है?", "Paris is the capital city of France.", "Bicycles should have their chains oiled regularly."];

        var (fullMs, fullVectors, fullProbe) = await MeasureAsync(BothModelsStagedFactAttribute.FullDirectory, chunks, probe);
        var (int8Ms, int8Vectors, int8Probe) = await MeasureAsync(BothModelsStagedFactAttribute.Int8Directory, chunks, probe);

        var cosines = fullVectors.Zip(int8Vectors, Dot).ToList();
        output.WriteLine($"{chunks.Count} chunks of {sample.Length} characters at 500/50: bge-m3 {fullMs / chunks.Count:F0} ms a chunk, "
            + $"bge-m3-int8 {int8Ms / chunks.Count:F0} ms a chunk ({int8Ms / fullMs:P0}); cosine min {cosines.Min():F4}, mean {cosines.Average():F4}");

        Assert.True(int8Ms <= fullMs * 0.6, $"bge-m3-int8 took {int8Ms:F0} ms against bge-m3's {fullMs:F0} ms.");
        Assert.All(cosines, cosine => Assert.True(cosine >= 0.95, $"cosine {cosine:F4}"));
        Assert.True(Dot(int8Probe[0], int8Probe[1]) > Dot(int8Probe[0], int8Probe[2]) + 0.3,
            "The Hindi question no longer finds the English passage that answers it.");
        Assert.True(Dot(fullProbe[0], fullProbe[1]) > Dot(fullProbe[0], fullProbe[2]) + 0.3);
    }

    /// <summary>Embeds the chunks one at a time, as ingestion does, after one warm-up call.</summary>
    /// <param name="directory">The model folder.</param>
    /// <param name="chunks">The chunks to time.</param>
    /// <param name="probe">Texts embedded after the timing, for the quality check.</param>
    /// <returns>The milliseconds for all chunks, their vectors, and the probe's vectors.</returns>
    private static async Task<(double Milliseconds, List<float[]> Vectors, List<float[]> Probe)> MeasureAsync(
        string directory, IReadOnlyList<string> chunks, IReadOnlyList<string> probe)
    {
        using var provider = new EmbeddedEmbeddingProvider(directory);
        await provider.EmbedAsync("warm up");

        var vectors = new List<float[]>();
        var stopwatch = Stopwatch.StartNew();
        foreach (var chunk in chunks)
        {
            vectors.Add(await provider.EmbedAsync(chunk));
        }

        stopwatch.Stop();
        var probeVectors = new List<float[]>();
        foreach (var text in probe)
        {
            probeVectors.Add(await provider.EmbedAsync(text));
        }

        return (stopwatch.Elapsed.TotalMilliseconds, vectors, probeVectors);
    }

    private static float Dot(float[] left, float[] right)
    {
        var sum = 0f;
        for (var i = 0; i < left.Length; i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }

    /// <summary>Runs a test only where both bge-m3 and bge-m3-int8 are staged in the shared cache.</summary>
    private sealed class BothModelsStagedFactAttribute : FactAttribute
    {
        public BothModelsStagedFactAttribute()
        {
            var fullData = Path.Combine(FullDirectory, "model.onnx_data");
            var int8Model = Path.Combine(Int8Directory, "model.onnx");
            if (!File.Exists(fullData) || new FileInfo(fullData).Length < 2_000_000_000)
            {
                Skip = $"The bge-m3 weights are not staged at {FullDirectory}.";
            }
            else if (!File.Exists(int8Model) || new FileInfo(int8Model).Length != 568_599_071
                || !File.Exists(Path.Combine(Int8Directory, "sentencepiece.bpe.model")))
            {
                Skip = $"The bge-m3-int8 weights are not staged at {Int8Directory}.";
            }
        }

        public static string FullDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "techierag-models", "bge-m3");

        public static string Int8Directory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "techierag-models", "bge-m3-int8");
    }
}
