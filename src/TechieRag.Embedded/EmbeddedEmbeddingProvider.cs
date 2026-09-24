using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using TechieRag.Abstractions;
using TechieRag.Models;

namespace TechieRag.Embedded;

/// <summary>
/// Offline embedding provider running an ONNX model in process: bge-m3 on desktops, all-MiniLM-L6-v2
/// on phones, downloaded once into the model root.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Embeddings with no server and no network after the first download.</para>
/// <para><b>Models:</b> <see cref="EmbeddedModel.BgeM3"/> (1024 dimensions, 100+ languages, 2.3 GB)
/// and <see cref="EmbeddedModel.MiniLM"/> (384 dimensions, English, 91 MB).
/// <see cref="CreateDefault"/> picks <see cref="EmbeddedModel.PlatformDefault"/>.</para>
/// <para><b>Where files go:</b> <c>&lt;ModelRoot&gt;/&lt;model name&gt;</c>, the per-user application
/// data folder unless the host moves it (REQ-RAG-053).</para>
/// <para><b>Download:</b> through <see cref="ModelDownloadService"/>, which reports the size before the
/// first byte and progress after (REQ-RAG-055).</para>
/// </remarks>
public class EmbeddedEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    /// <summary>
    /// Environment variable that redirects the one-time model download to an internal mirror.
    /// </summary>
    /// <remarks>
    /// REQ-NFR-008 (data locality): the only outbound call this provider ever makes is the
    /// first-run fetch of the model weights — no instance data is transmitted, and once the
    /// model is cached the provider is fully offline. Air-gapped or policy-restricted
    /// deployments can point this at an internal artifact store instead of huggingface.co, or
    /// pre-seed the model directory so no download occurs at all. bge-m3's files are fetched from
    /// <c>&lt;mirror&gt;/&lt;file&gt;</c>; any other model's from <c>&lt;mirror&gt;/&lt;name&gt;/&lt;file&gt;</c>.
    /// </remarks>
    public const string ModelBaseUrlEnvironmentVariable = "TECHIERAG_MODEL_BASE_URL";

    private const string DefaultModelBaseUrl = "https://huggingface.co/BAAI/bge-m3/resolve/main/onnx";

    /// <summary>
    /// Gets the base URL the bge-m3 weights are downloaded from — the configured mirror when
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

    /// <inheritdoc />
    public string Name => "Embedded-ONNX";

    /// <inheritdoc />
    public string ModelName { get; private set; }

    /// <inheritdoc />
    public int Dimensions { get; private set; }

    /// <summary>
    /// Gets the downloadable model this provider runs, or <see langword="null"/> when it was created
    /// from a folder the caller supplied.
    /// </summary>
    public EmbeddedModel? Model { get; }

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
    /// normalisation, or a different export of the same weights. The MiniLM path added on 2026-09-24
    /// is a different model name, so its signature differs already and r2 is kept.</para>
    /// </remarks>
    private const int EncodingRevision = 2;

    /// <inheritdoc />
    public string EmbeddingSignature =>
        EmbeddingStaleness.Signature(Name, ModelName, EncodingRevision);

    /// <inheritdoc />
    public event EventHandler<EmbeddingCompletedEventArgs>? OnEmbeddingCompleted;

    private InferenceSession? session;
    private Tokenizer? tokenizer;
    private bool feedsTokenTypeIds;
    private readonly int maxSequenceLength;
    private bool disposed;
    private bool initialized;
    private readonly SemaphoreSlim initLock = new(1, 1);

    /// <summary>
    /// Creates the provider for this platform's default model (<see cref="EmbeddedModel.PlatformDefault"/>:
    /// bge-m3 on desktops, all-MiniLM-L6-v2 on Android and iOS).
    /// Model download is deferred until first use or explicit initialization.
    /// </summary>
    /// <returns>A provider that downloads on first use.</returns>
    public static EmbeddedEmbeddingProvider CreateDefault() => Create(EmbeddedModel.PlatformDefault);

    /// <summary>
    /// Creates the provider for one model. Model download is deferred until first use.
    /// </summary>
    /// <param name="model">The model to run.</param>
    /// <returns>A provider that downloads on first use.</returns>
    /// <exception cref="NotSupportedException">The model is too large for this platform (bge-m3 on a phone).</exception>
    public static EmbeddedEmbeddingProvider Create(EmbeddedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.EnsureSupported(EmbeddedModel.IsPhonePlatform);
        return new EmbeddedEmbeddingProvider(model);
    }

    /// <summary>
    /// Creates and initializes the default provider asynchronously with progress reporting.
    /// </summary>
    /// <param name="cancellationToken">Cancels the download or load.</param>
    /// <returns>An initialized provider.</returns>
    public static async Task<EmbeddedEmbeddingProvider> CreateDefaultAsync(CancellationToken cancellationToken = default)
    {
        var provider = CreateDefault();
        await provider.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return provider;
    }

    /// <summary>
    /// Checks if this platform's default model is already downloaded and ready.
    /// </summary>
    /// <returns><see langword="true"/> when no download is needed.</returns>
    public static bool IsModelDownloaded() => EmbeddedModel.PlatformDefault.IsDownloaded();

    /// <summary>
    /// Gets the folder this platform's default model lives in: <c>&lt;ModelRoot&gt;/&lt;model name&gt;</c>.
    /// </summary>
    /// <returns>The absolute folder path.</returns>
    public static string GetModelDirectory() => EmbeddedModel.PlatformDefault.GetModelDirectory();

    /// <summary>
    /// Creates a lazy-initializing provider for a downloadable model.
    /// </summary>
    /// <param name="model">The model; platform checks are the caller's.</param>
    internal EmbeddedEmbeddingProvider(EmbeddedModel model)
    {
        Model = model;
        ModelName = model.Name;
        Dimensions = model.Dimensions;
        maxSequenceLength = model.MaxSequenceLength;
    }

    /// <summary>
    /// Creates embedded provider from a pre-downloaded model directory.
    /// </summary>
    /// <param name="modelDirectory">Folder holding an ONNX model and its tokenizer files.</param>
    /// <param name="dimensions">The width of the vectors the model produces.</param>
    /// <param name="maxSequenceLength">The longest input, in tokens.</param>
    public EmbeddedEmbeddingProvider(string modelDirectory, int dimensions = 1024, int maxSequenceLength = 8192)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        this.maxSequenceLength = maxSequenceLength;
        Dimensions = dimensions;
        ModelName = dimensions == EmbeddedModel.BgeM3.Dimensions ? EmbeddedModel.BgeM3.Name : $"embedded-onnx-{dimensions}d";

        // Initialize immediately for pre-loaded models
        InitializeFromDirectory(modelDirectory);
    }

    /// <summary>
    /// Initializes the provider, downloading the model if needed.
    /// Reports the size and progress through <see cref="ModelDownloadService"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the download or load.</param>
    /// <returns>A task that completes when the model is loaded.</returns>
    /// <exception cref="ModelDownloadDeclinedException">The host declined the download.</exception>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;

        await initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized || Model is null) return;

            var service = ModelDownloadService.Instance;
            var modelDirectory = Model.ResolveDirectory();
            await service.DownloadAsync(Model.Name, modelDirectory, Model.GetDownloadFiles(), cancellationToken)
                .ConfigureAwait(false);

            InitializeFromDirectory(modelDirectory);
            service.UpdateProgress(p => p.Status = ModelDownloadStatus.Completed);
        }
        finally
        {
            initLock.Release();
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
        feedsTokenTypeIds = session.InputMetadata.ContainsKey("token_type_ids");
        tokenizer = LoadTokenizer(tokenizerPath, modelDirectory);
        initialized = true;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var results = await EmbedBatchAsync([text], cancellationToken).ConfigureAwait(false);
        return results[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

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

        var inputIds = tokenizer is BertTokenizer wordPiece
            ? EncodeWordPiece(wordPiece, text)
            : EncodeXlmRoberta(tokenizer, text);
        var seqLength = inputIds.Length;
        var attentionMask = new long[seqLength];
        Array.Fill(attentionMask, 1L);

        // The count reported is what the model actually processed, special tokens included.
        tokenCount = seqLength;

        var inputIdsTensor = new DenseTensor<long>(inputIds, [1, seqLength]);
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, [1, seqLength]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
        };

        // BERT exports (all-MiniLM-L6-v2) declare a third input; a single segment is all zeros.
        if (feedsTokenTypeIds)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[seqLength], [1, seqLength])));
        }

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

    /// <summary>
    /// Encodes text for a BERT WordPiece model: <c>[CLS] pieces [SEP]</c>, ids unshifted.
    /// </summary>
    /// <param name="wordPiece">The WordPiece tokenizer.</param>
    /// <param name="text">The text.</param>
    /// <returns>The model's input ids, at most <see cref="maxSequenceLength"/> long.</returns>
    private long[] EncodeWordPiece(BertTokenizer wordPiece, string text)
    {
        var ids = wordPiece.EncodeToIds(text, maxSequenceLength, addSpecialTokens: true, out _, out _);
        var result = new long[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            result[i] = ids[i];
        }

        return result;
    }

    /// <summary>
    /// Encodes text for an XLM-RoBERTa SentencePiece model (bge-m3): <c>&lt;s&gt; pieces &lt;/s&gt;</c>
    /// with the fairseq shift (TR-RAG-044 / REQ-RAG-052).
    /// </summary>
    /// <param name="sentencePiece">The SentencePiece tokenizer.</param>
    /// <param name="text">The text.</param>
    /// <returns>The model's input ids, at most <see cref="maxSequenceLength"/> long.</returns>
    private long[] EncodeXlmRoberta(Tokenizer sentencePiece, string text)
    {
        // Two slots are reserved so the <s>/</s> wrapper cannot push a maximum-length input over the
        // model's sequence limit.
        var encoded = sentencePiece.EncodeToIds(text, Math.Max(1, maxSequenceLength - 2), out _, out _);

        // XLM-RoBERTa encoding: <s> text </s>, with the piece ids shifted into the model's
        // vocabulary (TR-RAG-044 / REQ-RAG-052). Both halves were missing: the ids went in raw, and
        // the sequence carried no special tokens at all.
        var seqLength = Math.Min(encoded.Count, maxSequenceLength - 2) + 2;
        var inputIds = new long[seqLength];

        inputIds[0] = BosTokenId;
        for (var i = 0; i < seqLength - 2; i++)
        {
            inputIds[i + 1] = ToModelId(encoded[i]);
        }

        inputIds[seqLength - 1] = EosTokenId;
        return inputIds;
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
        initLock.Dispose();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}
