using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Embedded;

/// <summary>
/// Local ONNX cross-encoder reranker (BGE-Reranker-v2-M3) with auto-download on first use.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Fully offline second-stage reranking: each (query, chunk) pair is
/// scored by a cross-encoder model, which is far more precise than vector similarity alone.</para>
/// <para><b>Model:</b> BGE-Reranker-v2-M3 (multilingual, XLM-RoBERTa based). The ONNX model
/// is downloaded on first use and cached next to the assembly, following the same
/// ModelDownloadService pattern as <see cref="EmbeddedEmbeddingProvider"/>. A pre-downloaded
/// model directory can be supplied instead to skip the download.</para>
/// <para><b>Usage:</b>
/// <code>
/// builder.WithReranker(() => new OnnxCrossEncoderReranker());
/// </code></para>
/// </remarks>
public class OnnxCrossEncoderReranker : IReranker, IDisposable
{
    private const string RerankerModelName = "bge-reranker-v2-m3";

    /// <summary>Where the ONNX weights come from.</summary>
    /// <remarks>
    /// <para><b>Not <c>BAAI/bge-reranker-v2-m3</c>, and that was the bug.</b> The official BAAI repo
    /// publishes PyTorch weights only — it has no <c>onnx/</c> directory at all — so the URL this
    /// constant used to hold returned <b>HTTP 404</b> and <c>InitializeAsync</c> could never succeed
    /// on a machine without a pre-staged model. That is why this class had never been executed.</para>
    /// <para><c>onnx-community</c> is Hugging Face's own ONNX-conversion organisation, and its export
    /// is laid out exactly as the old <see cref="ModelFiles"/> expected — <c>onnx/model.onnx</c> at
    /// 2,271,088,656 bytes, against the 2,270,000,000 this file already carried as its approximate
    /// size. The code was written against this repo; only the URL pointed elsewhere.</para>
    /// </remarks>
    private const string DefaultBaseUrl =
        "https://huggingface.co/onnx-community/bge-reranker-v2-m3-ONNX/resolve/main/onnx";

    /// <summary>Where the SentencePiece tokenizer comes from.</summary>
    /// <remarks>
    /// A second repo, because no single one has both halves: the ONNX export ships
    /// <c>tokenizer.json</c> but no <c>sentencepiece.bpe.model</c>, and this class needs the
    /// SentencePiece model. It is taken from BAAI's official repo — the authoritative source for the
    /// tokenizer, and byte-identical to the copy the embedded BGE-M3 provider already uses.
    /// </remarks>
    private const string DefaultTokenizerBaseUrl =
        "https://huggingface.co/BAAI/bge-reranker-v2-m3/resolve/main";

    /// <summary>
    /// Environment variable that redirects the one-time model download to an internal mirror.
    /// </summary>
    /// <remarks>
    /// The same escape hatch <c>EmbeddedEmbeddingProvider</c> offers, for the same REQ-NFR-008
    /// reason: the only outbound call this class ever makes is the first-run fetch of the weights,
    /// and an air-gapped deployment must be able to point it at an internal artifact store — or
    /// pre-seed the directory via <see cref="FromDirectory"/> so no download happens at all.
    /// </remarks>
    public const string ModelBaseUrlEnvironmentVariable = "TECHIERAG_RERANKER_BASE_URL";

    // XLM-RoBERTa special token ids
    private const long BosTokenId = 0;
    private const long EosTokenId = 2;
    private const long UnkTokenId = 3;

    /// <summary>
    /// How far a raw SentencePiece id sits from the id XLM-RoBERTa's embedding table expects.
    /// </summary>
    /// <remarks>
    /// <para><b>The two vocabularies are not the same, and using one as the other is silently
    /// wrong.</b> The SentencePiece model numbers <c>&lt;unk&gt;=0</c>, <c>&lt;s&gt;=1</c>,
    /// <c>&lt;/s&gt;=2</c> and then its pieces; XLM-RoBERTa's fairseq vocabulary puts
    /// <c>&lt;s&gt;=0</c>, <c>&lt;pad&gt;=1</c>, <c>&lt;/s&gt;=2</c>, <c>&lt;unk&gt;=3</c> and then
    /// the same pieces, one slot later. Hugging Face's <c>XLMRobertaTokenizer</c> reconciles them
    /// with exactly this shift, and every consumer of these weights must do the same.</para>
    /// <para><b>Why the defect hid for so long.</b> An off-by-one over a 250k vocabulary is not
    /// obviously broken — the shift is CONSISTENT, so a query and a passage that share a word still
    /// share its (wrong) id, and lexical-overlap relevance survives. What does not survive is anything
    /// that needs the embeddings to actually mean something: a Hindi query against an English passage
    /// ranked "Bicycles should have their chains oiled regularly" above "Paris is the capital city of
    /// France". That is what <c>ItRanksAcrossLanguages</c> caught.</para>
    /// </remarks>
    private const int FairseqOffset = 1;

    private static readonly SemaphoreSlim DownloadSemaphore = new(1, 1);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromHours(2) };

    /// <summary>One file the model needs, and which repository it comes from.</summary>
    /// <param name="Filename">The name as it is stored on disk, and as it is fetched.</param>
    /// <param name="DisplaySize">Human-readable size, for the progress report.</param>
    /// <param name="ApproxBytes">Approximate size, for the progress bar's denominator.</param>
    /// <param name="IsTokenizer">True when it comes from <see cref="DefaultTokenizerBaseUrl"/>.</param>
    private sealed record ModelFile(string Filename, string DisplaySize, long ApproxBytes, bool IsTokenizer = false);

    /// <remarks>
    /// <para><b><c>model.onnx_data</c> is the entry that was missing, and without it nothing could
    /// have worked even against a correct URL.</b> An ONNX graph above 2 GB cannot fit in a single
    /// protobuf, so the export splits into a small <c>model.onnx</c> stub plus an external-data
    /// sidecar holding the weights — here 656 KB and 2.27 GB respectively. Fetching only the stub
    /// yields a file that <c>InferenceSession</c> cannot load. The embedded BGE-M3 provider has
    /// always downloaded its own <c>model.onnx_data</c>; this list simply omitted it.</para>
    /// <para>Both land flat in the same directory, which is what the stub's external-data reference
    /// (a bare filename, resolved relative to the <c>.onnx</c>) requires.</para>
    /// </remarks>
    private static readonly ModelFile[] ModelFiles =
    [
        new("model.onnx", "656 KB", 656_891),
        new("model.onnx_data", "2.27 GB", 2_271_088_656),
        new("sentencepiece.bpe.model", "5 MB", 5_069_051, IsTokenizer: true)
    ];

    private readonly string? preloadedModelDirectory;
    private readonly string downloadBaseUrl;
    private readonly int maxSequenceLength;
    private InferenceSession? session;
    private Tokenizer? tokenizer;
    private bool initialized;
    private bool disposed;
    private readonly SemaphoreSlim initLock = new(1, 1);

    /// <inheritdoc/>
    public string Name => "Embedded-ONNX-CrossEncoder";

    /// <summary>
    /// Creates a reranker that downloads BGE-Reranker-v2-M3 on first use.
    /// </summary>
    /// <param name="downloadBaseUrl">Optional Hugging Face base URL override for the model files.</param>
    /// <param name="maxSequenceLength">Maximum token sequence length per (query, chunk) pair.</param>
    public OnnxCrossEncoderReranker(string? downloadBaseUrl = null, int maxSequenceLength = 1024)
    {
        this.downloadBaseUrl = (downloadBaseUrl ?? ModelBaseUrl).TrimEnd('/');
        this.maxSequenceLength = maxSequenceLength;
    }

    /// <summary>
    /// Gets the base URL the ONNX weights are fetched from — the configured mirror when
    /// <see cref="ModelBaseUrlEnvironmentVariable"/> is set, otherwise the public repository.
    /// </summary>
    public static string ModelBaseUrl =>
        Environment.GetEnvironmentVariable(ModelBaseUrlEnvironmentVariable) is { Length: > 0 } configured
        && !string.IsNullOrWhiteSpace(configured)
            ? configured.TrimEnd('/')
            : DefaultBaseUrl;

    private OnnxCrossEncoderReranker(string modelDirectory, int maxSequenceLength, bool preloaded)
    {
        preloadedModelDirectory = modelDirectory;
        downloadBaseUrl = DefaultBaseUrl;
        this.maxSequenceLength = maxSequenceLength;
    }

    /// <summary>
    /// Creates a reranker from a pre-downloaded model directory
    /// (must contain an ONNX model and a sentencepiece tokenizer).
    /// </summary>
    /// <param name="modelDirectory">Directory containing the model files.</param>
    /// <param name="maxSequenceLength">Maximum token sequence length per (query, chunk) pair.</param>
    /// <returns>A reranker bound to the given model directory.</returns>
    /// <exception cref="ArgumentException">Thrown when modelDirectory is null or whitespace.</exception>
    public static OnnxCrossEncoderReranker FromDirectory(string modelDirectory, int maxSequenceLength = 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        return new OnnxCrossEncoderReranker(modelDirectory, maxSequenceLength, preloaded: true);
    }

    /// <summary>
    /// Gets the local cache directory for the reranker model.
    /// </summary>
    /// <returns>The model directory path next to the executing assembly.</returns>
    public static string GetModelDirectory()
    {
        var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? AppContext.BaseDirectory;
        return Path.Combine(assemblyDir, "models", RerankerModelName);
    }

    /// <summary>
    /// Checks whether the reranker model is already downloaded and complete.
    /// </summary>
    /// <returns>True when all model files are present and the weights are fully written.</returns>
    /// <remarks>
    /// <b>The size test is on the EXTERNAL DATA file, not on <c>model.onnx</c>.</b> It used to
    /// require <c>model.onnx</c> to exceed 1 GB, which for an external-data export is never true —
    /// the <c>.onnx</c> is a 656 KB stub. So even a complete download reported "not downloaded", and
    /// the class would have re-fetched 2.27 GB on every single call. Checking the sidecar is what
    /// <c>EmbeddedEmbeddingProvider.IsModelDownloaded</c> has always done, and it doubles as the
    /// partial-download guard: a fetch interrupted halfway leaves a short file that fails this test.
    /// </remarks>
    public static bool IsModelDownloaded()
    {
        var modelDir = GetModelDirectory();
        var modelPath = Path.Combine(modelDir, "model.onnx");
        var dataPath = Path.Combine(modelDir, "model.onnx_data");
        var tokenizerPath = Path.Combine(modelDir, "sentencepiece.bpe.model");

        return File.Exists(modelPath)
            && File.Exists(dataPath)
            && File.Exists(tokenizerPath)
            && new FileInfo(dataPath).Length > 2_000_000_000;
    }

    /// <summary>
    /// Initializes the reranker, downloading the model on first use.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous initialization.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;

        await initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized) return;

            var modelDir = preloadedModelDirectory
                ?? await EnsureModelDownloadedAsync(cancellationToken).ConfigureAwait(false);

            var modelPath = FindModelFile(modelDir);
            var tokenizerPath = Path.Combine(modelDir, "sentencepiece.bpe.model");
            if (!File.Exists(tokenizerPath))
            {
                throw new FileNotFoundException($"Tokenizer not found: {tokenizerPath}");
            }

            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
            };
            session = new InferenceSession(modelPath, sessionOptions);

            using var stream = File.OpenRead(tokenizerPath);
            tokenizer = SentencePieceTokenizer.Create(stream);

            initialized = true;
        }
        finally
        {
            initLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query,
        IReadOnlyList<SearchResult> results,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0) return results;

        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var scored = new List<SearchResult>(results.Count);
        foreach (var result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var score = ScorePair(query, result.Chunk.Text);
            scored.Add(new SearchResult { Chunk = result.Chunk, Score = score });
        }

        return scored
            .OrderByDescending(r => r.Score)
            .Take(Math.Min(topN, scored.Count))
            .ToList();
    }

    /// <summary>
    /// Converts one raw SentencePiece id into the id XLM-RoBERTa's embedding table expects.
    /// </summary>
    /// <param name="sentencePieceId">The id the SentencePiece model produced.</param>
    /// <returns>The corresponding fairseq vocabulary id.</returns>
    /// <remarks>
    /// Mirrors Hugging Face's <c>XLMRobertaTokenizer._convert_token_to_id</c>: the SentencePiece
    /// <c>&lt;unk&gt;</c> is id 0 and maps to fairseq's <c>&lt;unk&gt;</c> at 3 rather than shifting
    /// into <c>&lt;pad&gt;</c>; everything else moves up by <see cref="FairseqOffset"/>.
    /// </remarks>
    private static long ToModelId(int sentencePieceId) =>
        sentencePieceId == 0 ? UnkTokenId : sentencePieceId + FairseqOffset;

    private float ScorePair(string query, string document)
    {
        if (tokenizer is null || session is null)
        {
            throw new InvalidOperationException("Reranker not initialized. Call InitializeAsync() first.");
        }

        // XLM-RoBERTa pair encoding: <s> query </s> </s> document </s>
        var queryIds = tokenizer.EncodeToIds(query);
        var documentIds = tokenizer.EncodeToIds(document);

        // The special tokens below are already fairseq ids and are NOT shifted; only the pieces the
        // SentencePiece model produced are.
        var ids = new List<long> { BosTokenId };
        ids.AddRange(queryIds.Select(ToModelId));
        ids.Add(EosTokenId);
        ids.Add(EosTokenId);
        ids.AddRange(documentIds.Select(ToModelId));
        ids.Add(EosTokenId);

        if (ids.Count > maxSequenceLength)
        {
            ids = ids.Take(maxSequenceLength - 1).Append(EosTokenId).ToList();
        }

        var seqLength = ids.Count;
        var inputIds = new DenseTensor<long>(ids.ToArray(), [1, seqLength]);
        var attentionMask = new DenseTensor<long>(Enumerable.Repeat(1L, seqLength).ToArray(), [1, seqLength]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
        };

        using var outputs = session.Run(inputs);
        var logits = outputs.First().AsTensor<float>();

        // Cross-encoder rerankers output a single relevance logit; apply sigmoid for a 0..1 score
        var logit = logits.Length > 0 ? logits.GetValue(0) : 0f;
        return 1f / (1f + MathF.Exp(-logit));
    }

    private async Task<string> EnsureModelDownloadedAsync(CancellationToken cancellationToken)
    {
        var modelDir = GetModelDirectory();
        var service = ModelDownloadService.Instance;

        if (IsModelDownloaded()) return modelDir;

        await DownloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsModelDownloaded()) return modelDir;

            Directory.CreateDirectory(modelDir);

            service.UpdateProgress(p =>
            {
                p.Status = ModelDownloadStatus.Downloading;
                p.TotalFiles = ModelFiles.Length;
                p.CompletedFiles = 0;
            });

            for (var i = 0; i < ModelFiles.Length; i++)
            {
                var file = ModelFiles[i];
                var destPath = Path.Combine(modelDir, file.Filename);

                // A file counts as already-fetched only when it is PLAUSIBLY COMPLETE. The old test
                // was "exists and is non-empty", which accepts the truncated remains of an
                // interrupted 2.27 GB download and then fails deep inside InferenceSession with
                // nothing pointing back here. A 5% tolerance absorbs the difference between the
                // recorded approximate size and what the repo actually serves.
                if (File.Exists(destPath) && new FileInfo(destPath).Length >= file.ApproxBytes * 0.95)
                {
                    service.UpdateProgress(p => p.CompletedFiles = i + 1);
                    continue;
                }

                service.UpdateProgress(p =>
                {
                    p.CurrentFile = file.Filename;
                    p.CurrentFileSize = file.DisplaySize;
                    p.CurrentFileTotalBytes = file.ApproxBytes;
                    p.CurrentFileBytesDownloaded = 0;
                });

                var baseUrl = file.IsTokenizer ? DefaultTokenizerBaseUrl : downloadBaseUrl;
                await DownloadFileAsync($"{baseUrl}/{file.Filename}", destPath, cancellationToken)
                    .ConfigureAwait(false);

                service.UpdateProgress(p => p.CompletedFiles = i + 1);
            }

            service.UpdateProgress(p => p.Status = ModelDownloadStatus.Completed);
            return modelDir;
        }
        finally
        {
            DownloadSemaphore.Release();
        }
    }

    private static async Task DownloadFileAsync(string url, string destPath, CancellationToken cancellationToken)
    {
        var service = ModelDownloadService.Instance;

        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = File.Create(destPath);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalRead += bytesRead;
            var read = totalRead;
            service.UpdateProgress(p => p.CurrentFileBytesDownloaded = read);
        }
    }

    private static string FindModelFile(string directory)
    {
        var candidates = new[] { "model.onnx", "model_quantized.onnx" };
        foreach (var filename in candidates)
        {
            var path = Path.Combine(directory, filename);
            if (File.Exists(path)) return path;
        }

        var onnxFiles = Directory.GetFiles(directory, "*.onnx");
        if (onnxFiles.Length > 0) return onnxFiles[0];

        throw new FileNotFoundException($"No ONNX model found in {directory}");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed) return;
        session?.Dispose();
        initLock.Dispose();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
