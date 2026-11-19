using TechieRag.Models;

namespace TechieRag.Embedded;

/// <summary>
/// An embedding model <c>UseEmbedded()</c> can download and run: its name, width, files and size.
/// </summary>
/// <remarks>
/// <para><b>Three models ship.</b> <see cref="BgeM3"/> (1024 dimensions, multilingual, 2.3 GB) is
/// the desktop default. <see cref="BgeM3Int8"/> is the same model with 8-bit weights (0.6 GB, about 2.3 times
/// as fast on a processor), for a host that embeds a large folder (REQ-RAG-165). <see cref="MiniLM"/> (all-MiniLM-L6-v2, 384 dimensions, English, 91 MB)
/// is the default on Android and iOS, where bge-m3 is refused (REQ-RAG-054 / BRD-91).
/// <see cref="PlatformDefault"/> picks between them.</para>
/// <para><b>Where the files go.</b> <see cref="GetModelDirectory"/> is
/// <c>&lt;ModelRoot&gt;/&lt;Name&gt;</c> (REQ-RAG-053). A complete copy left by an older version
/// next to the assembly is still used, so an upgrade does not download 2.3 GB again.</para>
/// <para><b>Mirrors.</b> <see cref="EmbeddedEmbeddingProvider.ModelBaseUrlEnvironmentVariable"/>
/// redirects every download: bge-m3's files are fetched from <c>&lt;mirror&gt;/&lt;file&gt;</c>
/// (unchanged since v1), any other model's from <c>&lt;mirror&gt;/&lt;Name&gt;/&lt;file&gt;</c>.</para>
/// </remarks>
public sealed class EmbeddedModel
{
    private readonly (string FileName, string RemotePath, long Bytes)[] files;
    private readonly string defaultBaseUrl;
    private readonly bool mirrorAtRoot;

    private EmbeddedModel(
        string name,
        int dimensions,
        int maxSequenceLength,
        bool runsOnPhones,
        string defaultBaseUrl,
        bool mirrorAtRoot,
        (string FileName, string RemotePath, long Bytes)[] files)
    {
        Name = name;
        Dimensions = dimensions;
        MaxSequenceLength = maxSequenceLength;
        RunsOnPhones = runsOnPhones;
        this.defaultBaseUrl = defaultBaseUrl;
        this.mirrorAtRoot = mirrorAtRoot;
        this.files = files;
    }

    /// <summary>
    /// BAAI bge-m3: 1024 dimensions, 100+ languages, 8,192 tokens; a 2.3 GB download. Desktop only.
    /// </summary>
    public static EmbeddedModel BgeM3 { get; } = new(
        "bge-m3",
        dimensions: 1024,
        maxSequenceLength: 8192,
        runsOnPhones: false,
        defaultBaseUrl: "https://huggingface.co/BAAI/bge-m3/resolve/main/onnx",
        mirrorAtRoot: true,
        files:
        [
            ("model.onnx", "model.onnx", 724_923),
            ("model.onnx_data", "model.onnx_data", 2_266_820_608),
            ("tokenizer.json", "tokenizer.json", 17_082_821),
            ("sentencepiece.bpe.model", "sentencepiece.bpe.model", 5_069_051),
            ("config.json", "config.json", 698)
        ]);

    /// <summary>
    /// bge-m3 with its weights stored as 8-bit integers: the same 1024 dimensions, languages, tokenizer and
    /// 8,192 tokens as <see cref="BgeM3"/>, a 0.6 GB download, and about 2.3 times as fast on a computer's
    /// processor (REQ-RAG-165, Sevak TR-RAG-065). Desktop only.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it is faster.</b> Nearly all of bge-m3's time on a processor goes into its matrix
    /// multiplications (88 percent, measured with ONNX Runtime's profiler). Integer multiplications run several
    /// times faster than 32-bit float ones on any x64 processor with AVX2 and faster still with VNNI.
    /// Measured on an Intel Core i5-11300H, 24 chunks of 500 characters (about 131 tokens each), one at a time:
    /// bge-m3 1,400 ms a chunk, this model 607 ms a chunk.</para>
    /// <para><b>Its vectors are close to bge-m3's, not equal.</b> The cosine between the two models' vectors for the
    /// same text is about 0.98, and nine in ten nearest neighbours agree. The model name differs, so the
    /// <c>EmbeddingSignature</c> differs: a corpus embedded with <see cref="BgeM3"/> reads as stale under this
    /// model and is embedded again, never mixed.</para>
    /// <para><b>Source.</b> <c>onnx/sentence_transformers_int8.onnx</c> from <c>Xenova/bge-m3</c> (MIT, as bge-m3),
    /// pinned to commit 4de13258303883538bd53b696b452bf8099f0858: the same graph as bge-m3's own ONNX export
    /// (the one <see cref="BgeM3"/> downloads), with its weights quantized to 8 bits.</para>
    /// </remarks>
    public static EmbeddedModel BgeM3Int8 { get; } = new(
        "bge-m3-int8",
        dimensions: 1024,
        maxSequenceLength: 8192,
        runsOnPhones: false,
        defaultBaseUrl: "https://huggingface.co/Xenova/bge-m3/resolve/4de13258303883538bd53b696b452bf8099f0858",
        mirrorAtRoot: false,
        files:
        [
            ("model.onnx", "onnx/sentence_transformers_int8.onnx", 568_599_071),
            ("tokenizer.json", "tokenizer.json", 17_082_821),
            ("sentencepiece.bpe.model", "sentencepiece.bpe.model", 5_069_051),
            ("config.json", "config.json", 770)
        ]);

    /// <summary>
    /// sentence-transformers all-MiniLM-L6-v2: 384 dimensions, English, 256 tokens; a 91 MB download.
    /// The default on Android and iOS.
    /// </summary>
    public static EmbeddedModel MiniLM { get; } = new(
        "all-minilm-l6-v2",
        dimensions: 384,
        maxSequenceLength: 256,
        runsOnPhones: true,
        defaultBaseUrl: "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main",
        mirrorAtRoot: false,
        files:
        [
            ("model.onnx", "onnx/model.onnx", 90_405_214),
            ("vocab.txt", "vocab.txt", 231_508)
        ]);

    /// <summary>Every model this package can download.</summary>
    public static IReadOnlyList<EmbeddedModel> All { get; } = [BgeM3, BgeM3Int8, MiniLM];

    /// <summary>
    /// Gets the model <c>UseEmbedded()</c> picks on this platform: <see cref="MiniLM"/> on Android
    /// and iOS, <see cref="BgeM3"/> everywhere else.
    /// </summary>
    public static EmbeddedModel PlatformDefault => DefaultFor(IsPhonePlatform);

    /// <summary>
    /// Gets whether this process runs on Android or iOS (Mac Catalyst is a desktop, not a phone).
    /// </summary>
    /// <remarks>
    /// <see cref="OperatingSystem.IsIOS"/> is also true on Mac Catalyst, which is why Catalyst is
    /// excluded explicitly.
    /// </remarks>
    public static bool IsPhonePlatform =>
        OperatingSystem.IsAndroid() || (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst());

    /// <summary>The model's folder name and signature name, for example <c>bge-m3</c>.</summary>
    public string Name { get; }

    /// <summary>The width of the vectors it produces.</summary>
    public int Dimensions { get; }

    /// <summary>The longest input, in tokens, it is run with.</summary>
    public int MaxSequenceLength { get; }

    /// <summary>Whether it is allowed on Android and iOS.</summary>
    public bool RunsOnPhones { get; }

    /// <summary>The total download size in bytes.</summary>
    public long DownloadBytes => files.Sum(f => f.Bytes);

    /// <summary>The total download size as text, for example "2.3 GB".</summary>
    public string DownloadSize => ModelDownloadService.FormatBytes(DownloadBytes);

    /// <summary>Whether the model uses a BERT WordPiece vocabulary (otherwise XLM-RoBERTa SentencePiece).</summary>
    internal bool UsesWordPiece => files.Any(f => f.FileName == "vocab.txt");

    /// <summary>
    /// Finds a model by its <see cref="Name"/>, ignoring case.
    /// </summary>
    /// <param name="name">The name, for example <c>bge-m3</c>.</param>
    /// <returns>The model, or <see langword="null"/> when none has that name.</returns>
    public static EmbeddedModel? FromName(string? name) =>
        All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Names the downloadable model a folder holds, from its ONNX file's size: a folder of
    /// <see cref="BgeM3Int8"/> files is that model, not <see cref="BgeM3"/> (REQ-RAG-165).
    /// </summary>
    /// <param name="folder">The folder a caller gave the provider.</param>
    /// <param name="dimensions">The width the caller said the model produces.</param>
    /// <returns>The model whose <c>model.onnx</c> has exactly the folder's size, or <see langword="null"/>.</returns>
    internal static EmbeddedModel? RecognizeFolder(string folder, int dimensions)
    {
        var modelFile = new FileInfo(Path.Combine(folder, "model.onnx"));
        if (!modelFile.Exists)
        {
            return null;
        }

        return All.FirstOrDefault(m => m.Dimensions == dimensions
            && m.files.Any(f => f.FileName == "model.onnx" && f.Bytes == modelFile.Length));
    }

    /// <summary>
    /// Gets the folder this model's files live in: <c>&lt;ModelRoot&gt;/&lt;Name&gt;</c>.
    /// </summary>
    /// <returns>The absolute folder path.</returns>
    public string GetModelDirectory() => ModelRoot.GetModelDirectory(Name);

    /// <summary>
    /// Gets whether the model is on disk: every file under the model root or in a complete copy an older
    /// version left next to the assembly, or a folder a provider in this process loaded it from
    /// (REQ-RAG-163, AstroLyfe TR-RAG-001).
    /// </summary>
    /// <returns><see langword="true"/> when using it downloads nothing.</returns>
    public bool IsDownloaded() =>
        LoadedFolderStillThere() is not null || ModelDownloadService.IsComplete(ResolveDirectory(), GetDownloadFiles());

    /// <summary>
    /// Gets the folder the model runs from: the folder a provider in this process loaded it from, else the
    /// folder a provider would load it from now (the model root, or a complete older copy next to the
    /// assembly) (REQ-RAG-163, AstroLyfe TR-RAG-001).
    /// </summary>
    /// <returns>The absolute folder path.</returns>
    /// <remarks><see cref="GetModelDirectory"/> stays the folder a download writes to.</remarks>
    public string GetLoadDirectory() => LoadedFolderStillThere() ?? ResolveDirectory();

    /// <summary>The folder each model was last loaded from in this process, by model name.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> SharedLoadedFolders =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A private record for one asynchronous flow, set by <see cref="IsolateLoadedFolders"/>; for tests.</summary>
    private static readonly AsyncLocal<System.Collections.Concurrent.ConcurrentDictionary<string, string>?> IsolatedLoadedFolders = new();

    /// <summary>The record in effect: this flow's private one, else the process-wide one.</summary>
    private static System.Collections.Concurrent.ConcurrentDictionary<string, string> LoadedFolders =>
        IsolatedLoadedFolders.Value ?? SharedLoadedFolders;

    /// <summary>Records the folder a provider loaded this model from.</summary>
    /// <param name="folder">The folder.</param>
    internal void NoteLoaded(string folder) => LoadedFolders[Name] = Path.GetFullPath(folder);

    /// <summary>
    /// Gives the calling asynchronous flow an empty record of loaded folders of its own until the returned
    /// object is disposed; for tests, so a model another test loads in parallel cannot change what this
    /// flow reads, and a load in this flow is not seen by others.
    /// </summary>
    /// <returns>Dispose to return to the process-wide record.</returns>
    internal static IDisposable IsolateLoadedFolders()
    {
        var previous = IsolatedLoadedFolders.Value;
        IsolatedLoadedFolders.Value = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new Restore(previous);
    }

    /// <summary>Puts back the record that was in effect before <see cref="IsolateLoadedFolders"/>.</summary>
    private sealed class Restore(System.Collections.Concurrent.ConcurrentDictionary<string, string>? previous) : IDisposable
    {
        public void Dispose() => IsolatedLoadedFolders.Value = previous;
    }

    /// <summary>The recorded load folder, when it still holds an ONNX model file.</summary>
    /// <returns>The folder, or null.</returns>
    private string? LoadedFolderStillThere() =>
        LoadedFolders.TryGetValue(Name, out var folder)
        && Directory.Exists(folder)
        && Directory.EnumerateFiles(folder, "*.onnx").Any()
            ? folder
            : null;

    /// <summary>
    /// Gets the files a download fetches, with the mirror applied when one is configured.
    /// </summary>
    /// <returns>One entry per file.</returns>
    public IReadOnlyList<ModelDownloadFile> GetDownloadFiles()
    {
        var mirror = Environment.GetEnvironmentVariable(EmbeddedEmbeddingProvider.ModelBaseUrlEnvironmentVariable);
        return files
            .Select(f => new ModelDownloadFile(
                f.FileName,
                new Uri(string.IsNullOrWhiteSpace(mirror)
                    ? $"{defaultBaseUrl}/{f.RemotePath}"
                    : mirrorAtRoot
                        ? $"{mirror.TrimEnd('/')}/{f.FileName}"
                        : $"{mirror.TrimEnd('/')}/{Name}/{f.FileName}"),
                f.Bytes))
            .ToList();
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Dimensions} dimensions, {DownloadSize})";

    /// <summary>
    /// Picks the default model for a platform.
    /// </summary>
    /// <param name="isPhone">Whether the platform is Android or iOS.</param>
    /// <returns><see cref="MiniLM"/> on a phone, <see cref="BgeM3"/> otherwise.</returns>
    internal static EmbeddedModel DefaultFor(bool isPhone) => isPhone ? MiniLM : BgeM3;

    /// <summary>
    /// Refuses a model a platform cannot carry.
    /// </summary>
    /// <param name="isPhone">Whether the platform is Android or iOS.</param>
    /// <exception cref="NotSupportedException">The model is too large for a phone.</exception>
    internal void EnsureSupported(bool isPhone)
    {
        if (isPhone && !RunsOnPhones)
        {
            throw new NotSupportedException(
                $"The embedding model '{Name}' is a {DownloadSize} download and is not supported on Android or iOS. "
                + $"Use EmbeddedModel.{nameof(MiniLM)} ({MiniLM.Dimensions} dimensions, {MiniLM.DownloadSize}), "
                + "which UseEmbedded() selects by default on phones.");
        }
    }

    /// <summary>
    /// The folder to load from: the model root, unless only an older version's copy next to the
    /// assembly is complete.
    /// </summary>
    /// <returns>An absolute folder path.</returns>
    internal string ResolveDirectory()
    {
        var root = GetModelDirectory();
        var downloadFiles = GetDownloadFiles();
        if (ModelDownloadService.IsComplete(root, downloadFiles))
        {
            return root;
        }

        var legacy = LegacyDirectory(Name);
        return legacy is not null && ModelDownloadService.IsComplete(legacy, downloadFiles) ? legacy : root;
    }

    /// <summary>For tests: the folder that stands in for <c>&lt;assembly folder&gt;/models</c>.</summary>
    internal static string? LegacyDirectoryOverride { get; set; }

    /// <summary>
    /// The folder versions before REQ-RAG-053 downloaded into: <c>&lt;assembly folder&gt;/models/&lt;name&gt;</c>.
    /// </summary>
    /// <param name="modelName">The model's folder name.</param>
    /// <returns>The folder, or <see langword="null"/> when the assembly has no file location.</returns>
    internal static string? LegacyDirectory(string modelName)
    {
        var overridden = LegacyDirectoryOverride;
        if (overridden is not null)
        {
            return Path.Combine(overridden, modelName);
        }

        var assemblyLocation = typeof(EmbeddedModel).Assembly.Location;
        var assemblyDirectory = string.IsNullOrEmpty(assemblyLocation)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(assemblyLocation);
        return string.IsNullOrEmpty(assemblyDirectory) ? null : Path.Combine(assemblyDirectory, "models", modelName);
    }
}
