using System.Diagnostics;
using System.Reflection;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Embedded;

/// <summary>
/// Embedding provider with BGE-M3 ONNX model.
/// Model is auto-downloaded on first use and cached locally (~2.3GB).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> High-quality multilingual embeddings with auto-download.</para>
/// <para><b>Model:</b> BGE-M3 (1024 dimensions, 100+ languages, best quality)</para>
/// <para><b>First Use:</b> Downloads ~2.3GB model (cached for subsequent uses)</para>
/// </remarks>
public class EmbeddedEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private const string EmbeddedModelName = "bge-m3";
    private const int EmbeddedModelDimensions = 1024;
    private const string DefaultModelBaseUrl = "https://huggingface.co/BAAI/bge-m3/resolve/main/onnx";

    /// <summary>
    /// Environment variable that redirects the one-time model download to an internal mirror.
    /// </summary>
    /// <remarks>
    /// REQ-NFR-008 (data locality): the only outbound call this provider ever makes is the
    /// first-run fetch of the BGE-M3 weights — no instance data is transmitted, and once the
    /// model is cached the provider is fully offline. Air-gapped or policy-restricted
    /// deployments can point this at an internal artifact store instead of huggingface.co, or
    /// pre-seed the model directory so no download occurs at all.
    /// </remarks>
    public const string ModelBaseUrlEnvironmentVariable = "TECHIERAG_MODEL_BASE_URL";

    /// <summary>
    /// Gets the base URL the model weights are downloaded from — the configured mirror when
    /// <see cref="ModelBaseUrlEnvironmentVariable"/> is set, otherwise the public Hugging Face
    /// repository.
    /// </summary>
    public static string ModelBaseUrl
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(ModelBaseUrlEnvironmentVariable);
            return string.IsNullOrWhiteSpace(configured)
                ? DefaultModelBaseUrl
                : configured.TrimEnd('/');
        }
    }

    private static readonly SemaphoreSlim DownloadSemaphore = new(1, 1);
    private static string? cachedModelDir;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromHours(2) };

    // File information with approximate sizes in bytes
    private static readonly (string Filename, string DisplaySize, long ApproxBytes)[] ModelFiles =
    [
        ("model.onnx", "725 KB", 742_400),
        ("model.onnx_data", "2.27 GB", 2_437_000_000),
        ("tokenizer.json", "17 MB", 17_825_792),
        ("sentencepiece.bpe.model", "5 MB", 5_242_880),
        ("config.json", "698 B", 698)
    ];

    /// <inheritdoc />
    public string Name => "Embedded-ONNX";

    /// <inheritdoc />
    public string ModelName { get; private set; } = EmbeddedModelName;

    /// <inheritdoc />
    public int Dimensions { get; private set; } = EmbeddedModelDimensions;

    /// <summary>
    /// The encoding revision of this provider (REQ-RAG-052).
    /// </summary>
    /// <remarks>
    /// <para><b>r1</b> — the original encoding: raw SentencePiece ids, no <c>&lt;s&gt;</c>/<c>&lt;/s&gt;</c>
    /// wrapper. Wrong (TR-RAG-044), and every vector produced before 2026-08-04 carries it — except
    /// that stamping did not exist then either, so in practice such a corpus is UNSTAMPED and is
    /// reported as stale on that basis.</para>
    /// <para><b>r2</b> — 2026-08-04 onwards: fairseq-shifted ids inside <c>&lt;s&gt;</c> …
    /// <c>&lt;/s&gt;</c>. Neither the provider nor the model changed, which is precisely why a
    /// revision is needed and why a provider/model pair alone would not have caught it.</para>
    /// <para>Bump this for ANY change that alters the vector for identical input — encoding, pooling,
    /// normalisation, or a different export of the same weights.</para>
    /// </remarks>
    private const int EncodingRevision = 2;

    /// <inheritdoc />
    public string EmbeddingSignature =>
        EmbeddingStaleness.Signature(Name, ModelName, EncodingRevision);

    /// <inheritdoc />
    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

    private InferenceSession? session;
    private Tokenizer? tokenizer;
    private readonly int maxSequenceLength;
    private bool disposed;
    private bool initialized;
    private readonly string? preloadedModelDirectory;

    /// <summary>
    /// Creates the default embedded provider using BGE-M3.
    /// Model download is deferred until first use or explicit initialization.
    /// </summary>
    public static EmbeddedEmbeddingProvider CreateDefault()
    {
        return new EmbeddedEmbeddingProvider();
    }

    /// <summary>
    /// Creates and initializes the provider asynchronously with progress reporting.
    /// </summary>
    public static async Task<EmbeddedEmbeddingProvider> CreateDefaultAsync(CancellationToken cancellationToken = default)
    {
        var provider = new EmbeddedEmbeddingProvider();
        await provider.InitializeAsync(cancellationToken);
        return provider;
    }

    /// <summary>
    /// Checks if the BGE-M3 model is already downloaded and ready.
    /// </summary>
    public static bool IsModelDownloaded()
    {
        var modelDir = GetModelDirectory();
        var modelPath = Path.Combine(modelDir, "model.onnx");
        var dataPath = Path.Combine(modelDir, "model.onnx_data");

        if (!File.Exists(modelPath) || !File.Exists(dataPath))
            return false;

        var dataSize = new FileInfo(dataPath).Length;
        return dataSize > 2_000_000_000; // > 2GB means complete
    }

    /// <summary>
    /// Gets the model directory path.
    /// </summary>
    public static string GetModelDirectory()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? AppContext.BaseDirectory;
        return Path.Combine(assemblyDir, "models", EmbeddedModelName);
    }

    /// <summary>
    /// Creates a lazy-initializing embedded provider.
    /// Call InitializeAsync() or first embed call will trigger initialization.
    /// </summary>
    private EmbeddedEmbeddingProvider(int maxSequenceLength = 8192)
    {
        this.maxSequenceLength = maxSequenceLength;
    }

    /// <summary>
    /// Creates embedded provider from a pre-downloaded model directory.
    /// </summary>
    public EmbeddedEmbeddingProvider(string modelDirectory, int dimensions = 1024, int maxSequenceLength = 8192)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        this.preloadedModelDirectory = modelDirectory;
        this.maxSequenceLength = maxSequenceLength;
        Dimensions = dimensions;

        // Initialize immediately for pre-loaded models
        InitializeFromDirectory(modelDirectory);
    }

    /// <summary>
    /// Initializes the provider, downloading the model if needed.
    /// Reports progress through ModelDownloadService.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;

        var service = ModelDownloadService.Instance;

        try
        {
            service.UpdateProgress(p =>
            {
                p.Status = ModelDownloadStatus.Checking;
                p.TotalFiles = ModelFiles.Length;
            });

            var modelDir = await EnsureModelDownloadedAsync(cancellationToken);
            InitializeFromDirectory(modelDir);

            service.UpdateProgress(p => p.Status = ModelDownloadStatus.Completed);
        }
        catch (Exception ex)
        {
            service.UpdateProgress(p =>
            {
                p.Status = ModelDownloadStatus.Failed;
                p.ErrorMessage = ex.Message;
            });
            throw;
        }
    }

    private void InitializeFromDirectory(string modelDirectory)
    {
        var modelPath = FindModelFile(modelDirectory);
        var tokenizerPath = FindTokenizerFile(modelDirectory);

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"ONNX model not found: {modelPath}");

        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };

        session = new InferenceSession(modelPath, sessionOptions);
        tokenizer = LoadTokenizer(tokenizerPath, modelDirectory);
        initialized = true;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!initialized)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private static async Task<string> EnsureModelDownloadedAsync(CancellationToken cancellationToken)
    {
        var modelDir = GetModelDirectory();
        var service = ModelDownloadService.Instance;

        // Quick check without lock
        if (IsModelDownloaded())
        {
            cachedModelDir = modelDir;
            service.UpdateProgress(p =>
            {
                p.Status = ModelDownloadStatus.Completed;
                p.CompletedFiles = ModelFiles.Length;
            });
            return modelDir;
        }

        await DownloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (cachedModelDir != null && Directory.Exists(cachedModelDir))
            {
                service.UpdateProgress(p => p.Status = ModelDownloadStatus.Completed);
                return cachedModelDir;
            }

            Directory.CreateDirectory(modelDir);

            service.UpdateProgress(p =>
            {
                p.Status = ModelDownloadStatus.Downloading;
                p.TotalFiles = ModelFiles.Length;
                p.CompletedFiles = 0;
            });

            for (int i = 0; i < ModelFiles.Length; i++)
            {
                var (filename, displaySize, approxBytes) = ModelFiles[i];
                var destPath = Path.Combine(modelDir, filename);

                // Check if file already exists and is complete
                if (File.Exists(destPath))
                {
                    if (filename == "model.onnx_data")
                    {
                        var existingSize = new FileInfo(destPath).Length;
                        if (existingSize > 2_000_000_000)
                        {
                            service.UpdateProgress(p => p.CompletedFiles = i + 1);
                            continue;
                        }
                    }
                    else
                    {
                        service.UpdateProgress(p => p.CompletedFiles = i + 1);
                        continue;
                    }
                }

                service.UpdateProgress(p =>
                {
                    p.CurrentFile = filename;
                    p.CurrentFileSize = displaySize;
                    p.CurrentFileTotalBytes = approxBytes;
                    p.CurrentFileBytesDownloaded = 0;
                });

                Console.WriteLine($"[TechieRag.Embedded] Downloading {filename} ({displaySize})...");

                var url = $"{ModelBaseUrl}/{filename}";
                await DownloadFileWithProgressAsync(url, destPath, approxBytes, cancellationToken);

                service.UpdateProgress(p => p.CompletedFiles = i + 1);
                Console.WriteLine($"[TechieRag.Embedded] Downloaded {filename}");
            }

            cachedModelDir = modelDir;
            Console.WriteLine("[TechieRag.Embedded] BGE-M3 model ready!");

            return modelDir;
        }
        finally
        {
            DownloadSemaphore.Release();
        }
    }

    private static async Task DownloadFileWithProgressAsync(
        string url,
        string destPath,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        var service = ModelDownloadService.Instance;

        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
        service.UpdateProgress(p => p.CurrentFileTotalBytes = totalBytes);

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(destPath);

        var buffer = new byte[81920]; // 80KB buffer
        long totalRead = 0;
        int bytesRead;
        var lastProgressUpdate = DateTime.UtcNow;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;

            // Update progress every 500ms to avoid too frequent updates
            if ((DateTime.UtcNow - lastProgressUpdate).TotalMilliseconds > 500)
            {
                service.UpdateProgress(p => p.CurrentFileBytesDownloaded = totalRead);
                lastProgressUpdate = DateTime.UtcNow;
            }
        }

        // Final progress update
        service.UpdateProgress(p => p.CurrentFileBytesDownloaded = totalRead);
    }

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var results = await EmbedBatchAsync([text], cancellationToken);
        return results[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var textList = texts.ToList();
        var results = new List<float[]>(textList.Count);
        var stopwatch = Stopwatch.StartNew();
        var totalTokens = 0;

        foreach (var text in textList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedding = GenerateEmbedding(text, out var tokenCount);
            results.Add(embedding);
            totalTokens += tokenCount;
        }

        stopwatch.Stop();

        OnEmbeddingCompleted?.Invoke(this, new EmbeddingCompletedEventArgs
        {
            TokenCount = totalTokens,
            TextCount = textList.Count,
            Duration = stopwatch.Elapsed,
            ModelName = ModelName,
            ProviderName = Name
        });

        return results;
    }

    // XLM-RoBERTa special token ids, and the shift between the SentencePiece vocabulary and the
    // model's own (TR-RAG-044 / REQ-RAG-052).
    private const long BosTokenId = 0;
    private const long EosTokenId = 2;
    private const long UnkTokenId = 3;
    private const int FairseqOffset = 1;

    /// <summary>
    /// Converts one raw SentencePiece id into the id BGE-M3's XLM-RoBERTa embedding table expects.
    /// </summary>
    /// <param name="sentencePieceId">The id the SentencePiece model produced.</param>
    /// <returns>The corresponding fairseq vocabulary id.</returns>
    /// <remarks>
    /// <para><b>The two vocabularies are not the same.</b> SentencePiece numbers <c>&lt;unk&gt;=0</c>,
    /// <c>&lt;s&gt;=1</c>, <c>&lt;/s&gt;=2</c> then its pieces; XLM-RoBERTa's fairseq vocabulary is
    /// <c>&lt;s&gt;=0</c>, <c>&lt;pad&gt;=1</c>, <c>&lt;/s&gt;=2</c>, <c>&lt;unk&gt;=3</c> then the
    /// same pieces one slot later. Hugging Face's <c>XLMRobertaTokenizer</c> reconciles them exactly
    /// this way, and this provider passed the ids through unshifted.</para>
    /// <para><b>Why nobody noticed.</b> The shift is CONSISTENT, so a query and a passage sharing a
    /// word still share its wrong id — lexical-overlap retrieval keeps working, and English results
    /// look reasonable. Semantics do not survive: before this fix a Hindi query scored "Paris is the
    /// capital city of France" at 0.3536 and "Bicycles should have their chains oiled regularly" at
    /// 0.3642, both at noise level, with the wrong passage winning. Identical defect, and identical
    /// disguise, to the one <c>OnnxCrossEncoderReranker</c> carried.</para>
    /// <para>Mirrors <c>OnnxCrossEncoderReranker.ToModelId</c> deliberately: two copies of four lines
    /// in two assemblies, rather than a shared helper that would put a public tokenizer detail on the
    /// package's API surface.</para>
    /// </remarks>
    private static long ToModelId(int sentencePieceId) =>
        sentencePieceId == 0 ? UnkTokenId : sentencePieceId + FairseqOffset;

    private float[] GenerateEmbedding(string text, out int tokenCount)
    {
        if (tokenizer == null || session == null)
            throw new InvalidOperationException("Provider not initialized. Call InitializeAsync() first.");

        // Two slots are reserved so the <s>/</s> wrapper cannot push a maximum-length input over the
        // model's sequence limit.
        var encoded = tokenizer.EncodeToIds(text, Math.Max(1, maxSequenceLength - 2), out _, out _);

        // XLM-RoBERTa encoding: <s> text </s>, with the piece ids shifted into the model's
        // vocabulary (TR-RAG-044 / REQ-RAG-052). Both halves were missing: the ids went in raw, and
        // the sequence carried no special tokens at all.
        var seqLength = Math.Min(encoded.Count, maxSequenceLength - 2) + 2;
        var inputIds = new long[seqLength];
        var attentionMask = new long[seqLength];

        inputIds[0] = BosTokenId;
        for (var i = 0; i < seqLength - 2; i++)
        {
            inputIds[i + 1] = ToModelId(encoded[i]);
        }

        inputIds[seqLength - 1] = EosTokenId;
        Array.Fill(attentionMask, 1L);

        // The count reported is what the model actually processed, wrapper included.
        tokenCount = seqLength;

        var inputIdsTensor = new DenseTensor<long>(inputIds, [1, seqLength]);
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, [1, seqLength]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
        };

        using var outputs = session.Run(inputs);

        var output = outputs.FirstOrDefault(o => o.Name == "sentence_embedding")
                  ?? outputs.FirstOrDefault(o => o.Name == "last_hidden_state")
                  ?? outputs.First();

        var outputTensor = output.AsTensor<float>();

        if (outputTensor.Dimensions.Length == 2 && outputTensor.Dimensions[1] == Dimensions)
        {
            var embedding = new float[Dimensions];
            for (var i = 0; i < Dimensions; i++)
                embedding[i] = outputTensor[0, i];
            return Normalize(embedding);
        }

        var pooled = MeanPooling(outputTensor, attentionMask);
        return Normalize(pooled);
    }

    private float[] MeanPooling(Tensor<float> hiddenStates, long[] attentionMask)
    {
        var embedding = new float[Dimensions];
        var validTokenCount = attentionMask.Length;

        if (validTokenCount == 0) return embedding;

        for (var i = 0; i < validTokenCount; i++)
            for (var j = 0; j < Dimensions; j++)
                embedding[j] += hiddenStates[0, i, j];

        for (var j = 0; j < Dimensions; j++)
            embedding[j] /= validTokenCount;

        return embedding;
    }

    private static float[] Normalize(float[] vector)
    {
        var magnitude = (float)Math.Sqrt(vector.Sum(v => v * v));
        if (magnitude < 1e-12f) return vector;

        for (var i = 0; i < vector.Length; i++)
            vector[i] /= magnitude;

        return vector;
    }

    private static Tokenizer LoadTokenizer(string tokenizerPath, string modelDirectory)
    {
        var extension = Path.GetExtension(tokenizerPath).ToLowerInvariant();

        if (extension == ".txt")
            return BertTokenizer.Create(tokenizerPath);

        if (extension == ".json")
        {
            var spModelPath = Path.Combine(modelDirectory, "sentencepiece.bpe.model");
            if (File.Exists(spModelPath))
            {
                using var stream = File.OpenRead(spModelPath);
                return SentencePieceTokenizer.Create(stream);
            }

            var vocabPath = Path.Combine(modelDirectory, "vocab.txt");
            if (File.Exists(vocabPath))
                return BertTokenizer.Create(vocabPath);
        }

        if (extension == ".model")
        {
            using var stream = File.OpenRead(tokenizerPath);
            return SentencePieceTokenizer.Create(stream);
        }

        throw new NotSupportedException($"Unsupported tokenizer format: {extension}");
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

    private static string FindTokenizerFile(string directory)
    {
        var candidates = new[] { "tokenizer.json", "sentencepiece.bpe.model", "vocab.txt" };
        foreach (var filename in candidates)
        {
            var path = Path.Combine(directory, filename);
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException($"No tokenizer file found in {directory}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed) return;
        session?.Dispose();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
