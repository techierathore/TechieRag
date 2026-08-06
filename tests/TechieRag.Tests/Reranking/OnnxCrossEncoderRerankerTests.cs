using TechieRag.Embedded;
using TechieRag.Models;
using TechieRag.Tests.TestDoubles;
using Xunit;

namespace TechieRag.Tests.Reranking;

/// <summary>
/// The local ONNX cross-encoder reranker BRD-106 / REQ-RAG-025 explicitly names.
/// </summary>
/// <remarks>
/// <para><b>This file exists because there was nothing.</b> <c>OnnxCrossEncoderReranker</c> shipped
/// with ZERO test coverage anywhere in the repository, which is why REQ-RAG-025 was demoted from
/// <c>Verified 100%</c>: the row claimed a component nobody had ever executed.</para>
/// <para><b>What is proven here, and what is honestly not.</b> Every path that does not need the
/// model WEIGHTS is covered: argument validation, the empty-input short circuit, the model-location
/// contract, the failure a missing model produces, cancellation, and disposal. Actual SCORING is
/// not — it needs <c>bge-reranker-v2-m3</c>, a &gt;1 GB download that is not in this machine's model
/// cache. Those assertions belong in a live-model test alongside the other opt-in live suites, and
/// the row should not read <c>Verified</c> until they exist. Coverage of the surface is not the same
/// claim as "the model ranks correctly", and this file does not pretend otherwise.</para>
/// </remarks>
public sealed class OnnxCrossEncoderRerankerTests
{
    /// <summary>An empty candidate list is returned untouched, without loading a model.</summary>
    /// <remarks>
    /// The short circuit is ahead of <c>InitializeAsync</c>, and that ordering is the point: a search
    /// that matched nothing must not trigger a gigabyte download. This test would hang or fail on a
    /// machine with no cached model if the order were ever reversed.
    /// </remarks>
    [Fact]
    public async Task AnEmptyCandidateListIsReturnedWithoutLoadingTheModel()
    {
        using var reranker = new OnnxCrossEncoderReranker();

        var results = await reranker.RerankAsync("anything", [], topN: 5);

        Assert.Empty(results);
    }

    /// <summary>A blank query is rejected before any work is done.</summary>
    /// <param name="query">The query the caller supplied.</param>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ABlankQueryIsRejected(string? query)
    {
        using var reranker = new OnnxCrossEncoderReranker();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => reranker.RerankAsync(query!, [TestData.Result("doc", "text", 0.5f)], topN: 1));
    }

    /// <summary>A null candidate list is an argument error, not a null-reference crash.</summary>
    [Fact]
    public async Task ANullCandidateListIsRejected()
    {
        using var reranker = new OnnxCrossEncoderReranker();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reranker.RerankAsync("query", null!, topN: 1));
    }

    /// <summary>The reranker names itself distinctly, so a trace can say which one ran.</summary>
    [Fact]
    public void ItReportsItsOwnName()
    {
        using var reranker = new OnnxCrossEncoderReranker();

        Assert.Equal("Embedded-ONNX-CrossEncoder", reranker.Name);
    }

    /// <summary>
    /// The model directory is a stable, absolute location under the executing assembly.
    /// </summary>
    /// <remarks>
    /// Asserted because this path is the contract between the download step and every consumer that
    /// pre-stages the model — including the packaging step that ships one alongside the app. A path
    /// that moved silently would re-download a gigabyte on every machine.
    /// </remarks>
    [Fact]
    public void TheModelDirectoryIsStableAndNamesTheModel()
    {
        var directory = OnnxCrossEncoderReranker.GetModelDirectory();

        Assert.True(Path.IsPathRooted(directory));
        Assert.Equal("bge-reranker-v2-m3", Path.GetFileName(directory));
        Assert.Equal("models", Path.GetFileName(Path.GetDirectoryName(directory)));

        // Stable across calls — it is a location, not a temp allocation.
        Assert.Equal(directory, OnnxCrossEncoderReranker.GetModelDirectory());
    }

    /// <summary>An empty directory does not count as a downloaded model.</summary>
    /// <remarks>
    /// <c>IsModelDownloaded</c> requires both files AND a plausible size, so a half-finished or
    /// interrupted download reports false rather than being loaded and failing deep inside ONNX.
    /// </remarks>
    [Fact]
    public void AnIncompleteDownloadDoesNotCountAsDownloaded()
    {
        var directory = OnnxCrossEncoderReranker.GetModelDirectory();

        // Truthful either way: with no model present this is false; with a real one present the
        // files exist and are large. What must never happen is "true" for a directory of stubs.
        if (!Directory.Exists(directory))
        {
            Assert.False(OnnxCrossEncoderReranker.IsModelDownloaded());
            return;
        }

        var modelPath = Path.Combine(directory, "model.onnx");
        var isPlausible = File.Exists(modelPath) && new FileInfo(modelPath).Length > 1_000_000_000;
        Assert.Equal(isPlausible && File.Exists(Path.Combine(directory, "sentencepiece.bpe.model")),
            OnnxCrossEncoderReranker.IsModelDownloaded());
    }

    /// <summary>A blank pre-downloaded directory is rejected at construction.</summary>
    /// <param name="directory">The directory the caller supplied.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FromDirectoryRejectsABlankPath(string? directory) =>
        Assert.ThrowsAny<ArgumentException>(() => OnnxCrossEncoderReranker.FromDirectory(directory!));

    /// <summary>
    /// A pre-staged directory with no model in it fails with a message naming what is missing, and
    /// does NOT silently fall back to downloading one.
    /// </summary>
    /// <remarks>
    /// This is the real initialization path, exercised without the weights. A consumer that pointed
    /// <c>FromDirectory</c> at the wrong folder and got a two-hour download instead of an error
    /// would have no way to tell what went wrong.
    /// </remarks>
    [Fact]
    public async Task AnEmptyModelDirectoryFailsRatherThanDownloading()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"techierag-reranker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            using var reranker = OnnxCrossEncoderReranker.FromDirectory(directory);

            var failure = await Assert.ThrowsAsync<FileNotFoundException>(
                () => reranker.InitializeAsync());

            Assert.Contains(directory, failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A directory holding a model but no tokenizer fails naming the TOKENIZER — the two files are
    /// checked separately so a partial stage says which half is absent.
    /// </summary>
    [Fact]
    public async Task AMissingTokenizerIsNamedSpecifically()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"techierag-reranker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "model.onnx"), "not a real model");

        try
        {
            using var reranker = OnnxCrossEncoderReranker.FromDirectory(directory);

            var failure = await Assert.ThrowsAsync<FileNotFoundException>(
                () => reranker.InitializeAsync());

            Assert.Contains("sentencepiece.bpe.model", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An already-cancelled rerank stops before it loads anything.</summary>
    [Fact]
    public async Task AnAlreadyCancelledRerankDoesNotLoadTheModel()
    {
        using var reranker = new OnnxCrossEncoderReranker();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reranker.RerankAsync(
                "query", [TestData.Result("doc", "text", 0.5f)], topN: 1, cancelled.Token));
    }

    /// <summary>Disposing a reranker that never loaded a model is safe, and repeatable.</summary>
    /// <remarks>
    /// The common case on a machine with no cached model: constructed by DI, never initialized,
    /// disposed at scope end. It must not throw on a null session.
    /// </remarks>
    [Fact]
    public void DisposingAnUninitializedRerankerIsSafeAndIdempotent()
    {
        var reranker = new OnnxCrossEncoderReranker();

        reranker.Dispose();
        reranker.Dispose();
    }
}
