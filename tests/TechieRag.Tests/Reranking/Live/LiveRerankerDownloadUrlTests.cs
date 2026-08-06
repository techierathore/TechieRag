using TechieRag.Embedded;
using TechieRag.Tests.Web.Live;
using Xunit;

namespace TechieRag.Tests.Reranking.Live;

/// <summary>
/// Every URL the reranker's first-run download builds actually resolves (REQ-RAG-025 / BRD-106).
/// </summary>
/// <remarks>
/// <para><b>This is the test that would have caught the defect, for the price of three range
/// requests.</b> <c>OnnxCrossEncoderReranker</c> pointed its download at
/// <c>BAAI/bge-reranker-v2-m3</c>, which publishes PyTorch weights and no ONNX export at all, so
/// <c>onnx/model.onnx</c> returned <b>HTTP 404</b> and <c>InitializeAsync</c> could never succeed on
/// any machine without a pre-staged model. The component had never run. Nothing in a hermetic suite
/// could see it, and the 2.28 GB scoring suite is too heavy to be the thing that notices.</para>
/// <para><b>Range requests, not downloads.</b> Each check asks for the first byte, so the whole class
/// costs a few kilobytes and still proves the exact string the production code composes — a repo
/// rename, a moved path or a withdrawn export fails here immediately.</para>
/// <para>Opt-in under the existing live-network flag, since these do reach huggingface.co.</para>
/// </remarks>
[Trait("Category", LiveNetworkFactAttribute.CategoryName)]
public sealed class LiveRerankerDownloadUrlTests
{
    /// <summary>The ONNX graph stub resolves.</summary>
    [LiveNetworkFact]
    public Task TheModelStubUrlResolves() => AssertResolves($"{OnnxCrossEncoderReranker.ModelBaseUrl}/model.onnx");

    /// <summary>
    /// The external-data sidecar resolves — the file that carries the actual weights.
    /// </summary>
    /// <remarks>
    /// It was absent from the download list entirely. An ONNX graph over 2 GB cannot fit in one
    /// protobuf, so <c>model.onnx</c> is a 656 KB stub and this 2.27 GB file is the model. Fetching
    /// only the stub produces something <c>InferenceSession</c> cannot load.
    /// </remarks>
    [LiveNetworkFact]
    public Task TheExternalDataUrlResolves() => AssertResolves($"{OnnxCrossEncoderReranker.ModelBaseUrl}/model.onnx_data");

    /// <summary>
    /// The SentencePiece tokenizer resolves from BAAI's official repository.
    /// </summary>
    /// <remarks>
    /// A second repository on purpose: the ONNX export ships <c>tokenizer.json</c> but no
    /// <c>sentencepiece.bpe.model</c>, and this class needs the SentencePiece model. Neither repo
    /// alone can satisfy the download.
    /// </remarks>
    [LiveNetworkFact]
    public Task TheTokenizerUrlResolves() => AssertResolves(
        "https://huggingface.co/BAAI/bge-reranker-v2-m3/resolve/main/sentencepiece.bpe.model");

    /// <summary>
    /// A mirror configured through the environment variable is used in place of the public repo.
    /// </summary>
    /// <remarks>
    /// REQ-NFR-008: the first-run fetch is the only outbound call this class makes, and an air-gapped
    /// deployment has to be able to redirect it to an internal artifact store.
    /// </remarks>
    [Fact]
    public void AConfiguredMirrorReplacesThePublicRepository()
    {
        var original = Environment.GetEnvironmentVariable(
            OnnxCrossEncoderReranker.ModelBaseUrlEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(
                OnnxCrossEncoderReranker.ModelBaseUrlEnvironmentVariable,
                "https://artifacts.internal.test/models/reranker/");

            // Trailing slash trimmed, so the composed URL has exactly one separator.
            Assert.Equal("https://artifacts.internal.test/models/reranker", OnnxCrossEncoderReranker.ModelBaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                OnnxCrossEncoderReranker.ModelBaseUrlEnvironmentVariable, original);
        }
    }

    /// <summary>Asserts a URL serves content, without downloading it.</summary>
    /// <param name="url">The URL the production code would fetch.</param>
    /// <returns>A task that faults when the URL does not resolve.</returns>
    private static async Task AssertResolves(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.True(
            response.IsSuccessStatusCode,
            $"{url} returned {(int)response.StatusCode} {response.StatusCode}. "
            + "The reranker's first-run download cannot succeed against this URL.");
    }
}
