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
    private const string DefaultBaseUrl = "https://huggingface.co/BAAI/bge-reranker-v2-m3/resolve/main";

    // XLM-RoBERTa special token ids
    private const long BosTokenId = 0;
    private const long EosTokenId = 2;

    private static readonly SemaphoreSlim DownloadSemaphore = new(1, 1);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromHours(2) };

    private static readonly (string Filename, string DisplaySize, long ApproxBytes)[] ModelFiles =
    [
        ("onnx/model.onnx", "2.2 GB", 2_270_000_000),
        ("sentencepiece.bpe.model", "5 MB", 5_069_051)
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
        this.downloadBaseUrl = (downloadBaseUrl ?? DefaultBaseUrl).TrimEnd('/');
        this.maxSequenceLength = maxSequenceLength;
    }

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
    /// <returns>True when all model files are present.</returns>
    public static bool IsModelDownloaded()
    {
        var modelDir = GetModelDirectory();
        var modelPath = Path.Combine(modelDir, "model.onnx");
        var tokenizerPath = Path.Combine(modelDir, "sentencepiece.bpe.model");
        return File.Exists(modelPath) && File.Exists(tokenizerPath)
            && new FileInfo(modelPath).Length > 1_000_000_000;
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

    private float ScorePair(string query, string document)
    {
        if (tokenizer is null || session is null)
        {
            throw new InvalidOperationException("Reranker not initialized. Call InitializeAsync() first.");
        }

        // XLM-RoBERTa pair encoding: <s> query </s> </s> document </s>
        var queryIds = tokenizer.EncodeToIds(query);
        var documentIds = tokenizer.EncodeToIds(document);

        var ids = new List<long> { BosTokenId };
        ids.AddRange(queryIds.Select(id => (long)id));
        ids.Add(EosTokenId);
        ids.Add(EosTokenId);
        ids.AddRange(documentIds.Select(id => (long)id));
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
                var (filename, displaySize, approxBytes) = ModelFiles[i];
                var destPath = Path.Combine(modelDir, Path.GetFileName(filename));

                if (File.Exists(destPath) && new FileInfo(destPath).Length > 0 && filename != "onnx/model.onnx")
                {
                    service.UpdateProgress(p => p.CompletedFiles = i + 1);
                    continue;
                }

                service.UpdateProgress(p =>
                {
                    p.CurrentFile = filename;
                    p.CurrentFileSize = displaySize;
                    p.CurrentFileTotalBytes = approxBytes;
                    p.CurrentFileBytesDownloaded = 0;
                });

                var url = $"{downloadBaseUrl}/{filename}";
                await DownloadFileAsync(url, destPath, cancellationToken).ConfigureAwait(false);
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
