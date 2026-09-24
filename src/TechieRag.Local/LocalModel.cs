using TechieRag.Local.Runtime;
using TechieRag.Models;

namespace TechieRag.Local;

/// <summary>
/// A language model <c>UseLocalLlm()</c> can download and run in-process: its id, licence, chat
/// template, context length and memory needs (REQ-RAG-057 / BRD-96).
/// </summary>
/// <remarks>
/// <para><b>Two models ship.</b> <see cref="Qwen25Instruct05B"/> (0.5 billion parameters, about 0.5 to
/// 0.9 GB) is the default on Android and iOS. <see cref="Phi3Mini4kInstruct"/> (3.8 billion
/// parameters, about 2.4 to 2.7 GB) is the default on desktops. <see cref="PlatformDefault"/> picks
/// between them.</para>
/// <para><b>One model, a file set per runtime.</b> Each model is published in the format of every
/// runtime the package can use; the provider fetches the one its platform's runtime loads, so the app
/// names a model and never a runtime (REQ-RAG-058). Every file is pinned to a repository commit and
/// carries its SHA-256 (REQ-RAG-061).</para>
/// <para><b>Terms.</b> Each model has a licence (<see cref="LicenceName"/>, <see cref="TermsUrl"/>);
/// nothing downloads until the host signals the user accepted it (REQ-RAG-062 / BRD-101).</para>
/// </remarks>
public sealed class LocalModel
{
    /// <summary>Runtime working memory on top of the weights and the context, in bytes.</summary>
    internal const long RuntimeOverheadBytes = 256L * 1024 * 1024;

    private readonly IReadOnlyList<LocalModelVariant> variants;

    /// <summary>Creates a model description.</summary>
    /// <param name="id">The id apps and <c>local/&lt;id&gt;</c> routes use.</param>
    /// <param name="displayName">The name shown to a user.</param>
    /// <param name="licenceName">The licence, for example <c>Apache-2.0</c>.</param>
    /// <param name="termsUrl">Where the licence text is.</param>
    /// <param name="chatTemplate">The chat format.</param>
    /// <param name="contextLength">The longest context the model supports, in tokens.</param>
    /// <param name="kvBytesPerToken">Memory the context cache takes per token, in bytes.</param>
    /// <param name="runsOnPhones">Whether it is allowed on Android and iOS.</param>
    /// <param name="variants">The file set per runtime format.</param>
    /// <param name="folder">For a model loaded from a folder, that folder; null for a downloadable one.</param>
    internal LocalModel(
        string id,
        string displayName,
        string licenceName,
        Uri? termsUrl,
        LocalChatTemplate chatTemplate,
        int contextLength,
        long kvBytesPerToken,
        bool runsOnPhones,
        IReadOnlyList<LocalModelVariant> variants,
        string? folder = null)
    {
        Id = id;
        DisplayName = displayName;
        LicenceName = licenceName;
        TermsUrl = termsUrl;
        ChatTemplate = chatTemplate;
        ContextLength = contextLength;
        KvBytesPerToken = kvBytesPerToken;
        RunsOnPhones = runsOnPhones;
        this.variants = variants;
        Folder = folder;
    }

    /// <summary>
    /// Qwen2.5-0.5B-Instruct (Apache-2.0): 0.5 billion parameters, 32,768-token context, ChatML.
    /// The default on Android and iOS.
    /// </summary>
    public static LocalModel Qwen25Instruct05B { get; } = new(
        "qwen2.5-0.5b-instruct",
        "Qwen2.5 0.5B Instruct",
        "Apache-2.0",
        new Uri("https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct/blob/main/LICENSE"),
        LocalChatTemplate.ChatMl,
        contextLength: 32_768,
        kvBytesPerToken: 12_288,
        runsOnPhones: true,
        variants:
        [
            new LocalModelVariant(
                LocalModelFormat.Gguf,
                "qwen2.5-0.5b-instruct-gguf",
                "https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/9217f5db79a29953eb74d5343926648285ec7e67",
                [
                    new("qwen2.5-0.5b-instruct-q4_k_m.gguf", "qwen2.5-0.5b-instruct-q4_k_m.gguf", 491_400_032, "74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db")
                ]),
            new LocalModelVariant(
                LocalModelFormat.OnnxGenAi,
                "qwen2.5-0.5b-instruct-onnx",
                "https://huggingface.co/xiaoyao9184/Qwen2.5-0.5B-Instruct-onnx-genai/resolve/455ccdae478e4149475923256cc27bfa6f775b02/cpu_and_mobile/cpu-int4-rtn-block-32",
                [
                    new("added_tokens.json", "added_tokens.json", 605, "58b54bbe36fc752f79a24a271ef66a0a0830054b4dfad94bde757d851968060b"),
                    new("genai_config.json", "genai_config.json", 1_516, "91451609de365f1b383aae19c6bd935e7b0778c610c83e9bfadcd647f3c3e573"),
                    new("merges.txt", "merges.txt", 1_671_853, "8831e4f1a044471340f7c0a83d7bd71306a5b867e95fd870f74d0c5308a904d5"),
                    new("model.onnx", "model.onnx", 169_847, "ce313fa11d3d5b12a752f27531d745514fab253f5ff8690889717d73d9599382"),
                    new("model.onnx.data", "model.onnx.data", 861_939_200, "19a2860d7b120cf2d5c5d91c516ecbbbb9d2f7ce2ddbee75bf4c4c6d1ad26e02"),
                    new("special_tokens_map.json", "special_tokens_map.json", 613, "76862e765266b85aa9459767e33cbaf13970f327a0e88d1c65846c2ddd3a1ecd"),
                    new("tokenizer.json", "tokenizer.json", 11_421_896, "9c5ae00e602b8860cbd784ba82a8aa14e8feecec692e7076590d014d7b7fdafa"),
                    new("tokenizer_config.json", "tokenizer_config.json", 4_686, "0a04a9d7d4a62b28482bdfe726c122756de85714fb64166ace92ae75b8f57614"),
                    new("vocab.json", "vocab.json", 2_776_833, "ca10d7e9fb3ed18575dd1e277a2579c16d108e32f27439684afa0e10b1440910")
                ])
        ]);

    /// <summary>
    /// Phi-3-mini-4k-instruct (MIT): 3.8 billion parameters, 4,096-token context. The desktop default.
    /// </summary>
    public static LocalModel Phi3Mini4kInstruct { get; } = new(
        "phi-3-mini-4k-instruct",
        "Phi-3 mini 4k Instruct",
        "MIT",
        new Uri("https://huggingface.co/microsoft/Phi-3-mini-4k-instruct/blob/main/LICENSE"),
        LocalChatTemplate.Phi3,
        contextLength: 4_096,
        kvBytesPerToken: 393_216,
        runsOnPhones: false,
        variants:
        [
            new LocalModelVariant(
                LocalModelFormat.Gguf,
                "phi-3-mini-4k-instruct-gguf",
                "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf/resolve/a64113399c2f6b8ad3e11c394733a2ddadaa7f33",
                [
                    new("Phi-3-mini-4k-instruct-q4.gguf", "Phi-3-mini-4k-instruct-q4.gguf", 2_393_231_072, "8a83c7fb9049a9b2e92266fa7ad04933bb53aa1e85136b7b30f1b8000ff2edef")
                ]),
            new LocalModelVariant(
                LocalModelFormat.OnnxGenAi,
                "phi-3-mini-4k-instruct-onnx",
                "https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-onnx/resolve/5f5f794c1c23c9d5ee142af85df02a6cc52d6945/cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4",
                [
                    new("added_tokens.json", "added_tokens.json", 306, "f8e5a880c6c563a126d9efacb654e105fc4b66f21e5a540f04436e9487a955fb"),
                    new("config.json", "config.json", 919, "132bb3dca45e641008dce2ba8bbbf3fd877cf8b76a7c18919e0bae1ef13a20ce"),
                    new("genai_config.json", "genai_config.json", 1_576, "d5ec04466a3d080de412409bf762219628f375314cfff303976733364baec5a2"),
                    new("phi3-mini-4k-instruct-cpu-int4-rtn-block-32-acc-level-4.onnx", "phi3-mini-4k-instruct-cpu-int4-rtn-block-32-acc-level-4.onnx", 231_335, "385cd1b908a0d2f8634e86d30236f6dbb7ae660eb3943fd1ef5bdc3847326480"),
                    new("phi3-mini-4k-instruct-cpu-int4-rtn-block-32-acc-level-4.onnx.data", "phi3-mini-4k-instruct-cpu-int4-rtn-block-32-acc-level-4.onnx.data", 2_722_861_056, "5db30ce699aee1123cf9045742488db5928006fa618a42cb3c0840322a85ad0f"),
                    new("special_tokens_map.json", "special_tokens_map.json", 599, "810adc6e6c6ef2f56c285ef930d243358a3a9f05e36a01c5a10bafc6fac4609b"),
                    new("tokenizer.json", "tokenizer.json", 1_937_869, "072ab882d6c7192a42f78790945d16c064691321a73251a4b18f6a380f0fbe39"),
                    new("tokenizer.model", "tokenizer.model", 499_723, "9e556afd44213b6bd1be2b850ebbbd98f5481437a8021afaf58ee7fb1818d347"),
                    new("tokenizer_config.json", "tokenizer_config.json", 3_441, "32f66c2bab499baaa8341819ad9a13342f957501ce1e989bcb90853a01336cc0")
                ])
        ]);

    /// <summary>Every model this package can download.</summary>
    public static IReadOnlyList<LocalModel> All { get; } = [Qwen25Instruct05B, Phi3Mini4kInstruct];

    /// <summary>
    /// Gets the model <c>UseLocalLlm()</c> picks on this platform: <see cref="Qwen25Instruct05B"/> on
    /// Android and iOS, <see cref="Phi3Mini4kInstruct"/> everywhere else.
    /// </summary>
    public static LocalModel PlatformDefault => DefaultFor(IsPhonePlatform);

    /// <summary>Gets whether this process runs on Android or iOS (Mac Catalyst is a desktop).</summary>
    public static bool IsPhonePlatform =>
        OperatingSystem.IsAndroid() || (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst());

    /// <summary>Gets the id apps and <c>local/&lt;id&gt;</c> routes use, for example <c>qwen2.5-0.5b-instruct</c>.</summary>
    public string Id { get; }

    /// <summary>Gets the name shown to a user.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the licence the weights are published under, for example <c>Apache-2.0</c>.</summary>
    public string LicenceName { get; }

    /// <summary>Gets where the licence text is; null for a model loaded from a folder the host supplied.</summary>
    public Uri? TermsUrl { get; }

    /// <summary>Gets the chat format the provider applies.</summary>
    public LocalChatTemplate ChatTemplate { get; }

    /// <summary>Gets the longest context the model supports, in tokens.</summary>
    public int ContextLength { get; }

    /// <summary>Gets whether the model is allowed on Android and iOS.</summary>
    public bool RunsOnPhones { get; }

    /// <summary>Gets the folder a host-supplied model is loaded from; null for a downloadable model.</summary>
    public string? Folder { get; }

    /// <summary>Gets the memory the context cache takes per token, in bytes.</summary>
    internal long KvBytesPerToken { get; }

    /// <summary>Gets the file sets, one per runtime format.</summary>
    internal IReadOnlyList<LocalModelVariant> Variants => variants;

    /// <summary>
    /// Finds a model by its <see cref="Id"/>, ignoring case.
    /// </summary>
    /// <param name="id">The id, for example <c>phi-3-mini-4k-instruct</c>.</param>
    /// <returns>The model, or null when none has that id.</returns>
    public static LocalModel? FromId(string? id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Describes a model the host has already placed in a folder; nothing is downloaded.
    /// </summary>
    /// <param name="folder">The folder: a GGUF file, or an ONNX Runtime GenAI model with <c>genai_config.json</c>.</param>
    /// <param name="chatTemplate">The chat format the model was trained on.</param>
    /// <param name="contextLength">The longest context the model supports, in tokens.</param>
    /// <param name="kvBytesPerToken">Memory the context cache takes per token; the default suits a 1 to 4 billion parameter model.</param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is empty or <paramref name="contextLength"/> is not positive.</exception>
    public static LocalModel FromFolder(
        string folder,
        LocalChatTemplate chatTemplate,
        int contextLength = 4_096,
        long kvBytesPerToken = 131_072)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (contextLength <= 0) throw new ArgumentException("The context length must be positive.", nameof(contextLength));

        var fullPath = Path.GetFullPath(folder);
        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new LocalModel(name, name, "supplied by the host", null, chatTemplate, contextLength, kvBytesPerToken,
            runsOnPhones: true, variants: [], folder: fullPath);
    }

    /// <summary>
    /// Gets the terms a user accepts before this model downloads.
    /// </summary>
    /// <returns>The model's id, name, licence and terms URL.</returns>
    public LocalModelTerms GetTerms() => new(Id, DisplayName, LicenceName, TermsUrl);

    /// <inheritdoc/>
    public override string ToString() => $"{DisplayName} ({Id}, {LicenceName})";

    /// <summary>
    /// Picks the default model for a platform.
    /// </summary>
    /// <param name="isPhone">Whether the platform is Android or iOS.</param>
    /// <returns><see cref="Qwen25Instruct05B"/> on a phone, <see cref="Phi3Mini4kInstruct"/> otherwise.</returns>
    internal static LocalModel DefaultFor(bool isPhone) => isPhone ? Qwen25Instruct05B : Phi3Mini4kInstruct;

    /// <summary>
    /// Gets the context size the provider runs the model with when the host set none: 2,048 tokens on
    /// a phone, 4,096 elsewhere, never more than the model supports.
    /// </summary>
    /// <param name="isPhone">Whether the platform is Android or iOS.</param>
    /// <returns>The context size in tokens.</returns>
    internal int DefaultContextSize(bool isPhone) => Math.Min(ContextLength, isPhone ? 2_048 : 4_096);

    /// <summary>
    /// Estimates the memory loading the model takes: the weights, the context cache and the runtime's
    /// working memory (REQ-RAG-060).
    /// </summary>
    /// <param name="weightBytes">The size of the weight files on disk.</param>
    /// <param name="contextSize">The context size it is loaded with.</param>
    /// <returns>Bytes of free memory the load needs.</returns>
    internal long EstimateMemoryBytes(long weightBytes, int contextSize) =>
        weightBytes + (KvBytesPerToken * contextSize) + RuntimeOverheadBytes;

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
                $"The local model '{Id}' is too large for Android or iOS. Use LocalModel.{nameof(Qwen25Instruct05B)} "
                + "('qwen2.5-0.5b-instruct'), which UseLocalLlm() selects by default on phones.");
        }
    }

    /// <summary>
    /// Finds the file set a runtime loads.
    /// </summary>
    /// <param name="format">The runtime's format.</param>
    /// <returns>The variant, or null when the model is not published in that format.</returns>
    internal LocalModelVariant? FindVariant(LocalModelFormat format) =>
        variants.FirstOrDefault(v => v.Format == format);

    /// <summary>
    /// Gets the folder this model loads from for a variant: the host's folder, else
    /// <c>&lt;ModelRoot&gt;/&lt;variant folder&gt;</c> (REQ-RAG-053).
    /// </summary>
    /// <param name="variant">The variant; ignored for a folder model.</param>
    /// <returns>An absolute folder path.</returns>
    internal string GetDirectory(LocalModelVariant? variant) =>
        Folder ?? ModelRoot.GetModelDirectory(variant?.FolderName ?? Id);
}
