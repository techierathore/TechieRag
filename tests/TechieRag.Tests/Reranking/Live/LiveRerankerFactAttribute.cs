using TechieRag.Embedded;
using Xunit;

namespace TechieRag.Tests.Reranking.Live;

/// <summary>
/// A <see cref="FactAttribute"/> for a test that runs the real BGE-Reranker-v2-M3 cross-encoder
/// (REQ-RAG-025 / BRD-106).
/// </summary>
/// <remarks>
/// <para><b>Gated on the model being on disk, not on an opt-in flag.</b> The other live suites guard
/// a network call, which is a choice; this guards a 2.28 GB file, which is a fact. If the weights are
/// staged the tests run — there is nothing to opt into and no reason to make a machine that HAS the
/// model skip the only tests that prove it works.</para>
/// <para><b>Staged in the shared cache, not in <c>bin/</c>.</b> <see cref="ModelDirectory"/> is
/// <c>~/.cache/techierag-models/bge-reranker-v2-m3</c> — beside the <c>bge-m3</c> weights the
/// embedded provider already caches there. Downloading into the build output would cost 2.28 GB on
/// every <c>dotnet clean</c> and again per configuration.</para>
/// <para><b>Skipped, not silently absent</b>, so "the local ONNX reranker has never been executed"
/// stays visible in the test output on a machine that has not staged it.</para>
/// </remarks>
public sealed class LiveRerankerFactAttribute : FactAttribute
{
    /// <summary>The trait value these tests are filtered by.</summary>
    public const string CategoryName = "LiveReranker";

    /// <summary>Initializes a new instance of the <see cref="LiveRerankerFactAttribute"/> class.</summary>
    public LiveRerankerFactAttribute()
    {
        if (!IsModelStaged)
        {
            Skip = $"The bge-reranker-v2-m3 weights are not staged at {ModelDirectory}. "
                + "Run OnnxCrossEncoderReranker.InitializeAsync() once, or stage the files there, to run these.";
        }
    }

    /// <summary>Gets the shared cache directory the weights are staged in.</summary>
    public static string ModelDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache",
        "techierag-models",
        "bge-reranker-v2-m3");

    /// <summary>Gets whether a COMPLETE model is present.</summary>
    /// <remarks>
    /// The external-data sidecar carries the weights, so its size is what says "complete" — the same
    /// test <see cref="OnnxCrossEncoderReranker.IsModelDownloaded"/> makes, applied to this
    /// directory rather than the assembly-relative one.
    /// </remarks>
    public static bool IsModelStaged
    {
        get
        {
            var data = Path.Combine(ModelDirectory, "model.onnx_data");

            return File.Exists(Path.Combine(ModelDirectory, "model.onnx"))
                && File.Exists(Path.Combine(ModelDirectory, "sentencepiece.bpe.model"))
                && File.Exists(data)
                && new FileInfo(data).Length > 2_000_000_000;
        }
    }
}
