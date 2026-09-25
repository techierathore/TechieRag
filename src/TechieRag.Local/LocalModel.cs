using TechieRag.Local.HuggingFace;
using TechieRag.Local.Runtime;
using TechieRag.Models;

namespace TechieRag.Local;

/// <summary>
/// A language model <c>UseLocalLlm()</c> can download and run in-process: its id, licence, chat
/// template, context length and memory needs (REQ-RAG-057 / BRD-96).
/// </summary>
/// <remarks>
/// <para><b>Two models ship.</b> <see cref="Qwen25Instruct05B"/> (0.5 billion parameters, about 0.3
/// GB) is the default on Android and iOS. <see cref="Phi3Mini4kInstruct"/> (3.8 billion parameters,
/// about 2.7 GB) is the default on desktops. <see cref="PlatformDefault"/> picks
/// between them.</para>
/// <para><b>One model, one format.</b> ONNX Runtime GenAI runs every model on every platform
/// (DECISIONS.md 2026-09-25), so each model has exactly one file set, in that engine's format; the app
/// names a model and never a runtime (REQ-RAG-058). Every file carries its size and SHA-256, and its
/// source is pinned to a repository commit or served from the owner's mirror (REQ-RAG-061).</para>
/// <para><b>Terms.</b> Each model has a licence (<see cref="LicenceName"/>, <see cref="TermsUrl"/>);
/// nothing downloads until the host signals the user accepted it (REQ-RAG-062 / BRD-101).</para>
/// <para><b>Any model on Hugging Face.</b> <see cref="FromHuggingFace(string, string?, string?)"/> names any other ONNX Runtime
/// GenAI model by its repository; its licence, files and fingerprints come from Hugging Face's public
/// API (REQ-RAG-108 / BRD-166).</para>
/// </remarks>
public sealed class LocalModel
{
    /// <summary>Runtime working memory on top of the weights and the context, in bytes.</summary>
    internal const long RuntimeOverheadBytes = 256L * 1024 * 1024;

    private readonly IReadOnlyList<LocalModelVariant> variants;
    private readonly string licenceName;
    private readonly Uri? termsUrl;
    private int contextLength;
    private long kvBytesPerToken;
    private HuggingFaceResolution? huggingFaceResolution;

    /// <summary>Creates a model description.</summary>
    /// <param name="id">The id apps and <c>local/&lt;id&gt;</c> routes use.</param>
    /// <param name="displayName">The name shown to a user.</param>
    /// <param name="licenceName">The licence, for example <c>Apache-2.0</c>.</param>
    /// <param name="termsUrl">Where the licence text is.</param>
    /// <param name="chatTemplate">The chat format.</param>
    /// <param name="contextLength">The longest context the model supports, in tokens.</param>
    /// <param name="kvBytesPerToken">Memory the context cache takes per token, in bytes.</param>
    /// <param name="runsOnPhones">Whether it is allowed on Android and iOS.</param>
    /// <param name="variants">The file set, one per format (one format today).</param>
    /// <param name="folder">For a model loaded from a folder, that folder; null for a downloadable one.</param>
    /// <param name="huggingFace">For a model named on Hugging Face, its name; null otherwise.</param>
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
        string? folder = null,
        HuggingFaceModelSource? huggingFace = null)
    {
        Id = id;
        DisplayName = displayName;
        this.licenceName = licenceName;
        this.termsUrl = termsUrl;
        ChatTemplate = chatTemplate;
        this.contextLength = contextLength;
        this.kvBytesPerToken = kvBytesPerToken;
        RunsOnPhones = runsOnPhones;
        this.variants = variants;
        Folder = folder;
        HuggingFace = huggingFace;
    }

    /// <summary>
    /// Qwen2.5-0.5B-Instruct (Apache-2.0): 0.5 billion parameters, 32,768-token context, ChatML.
    /// The default on Android and iOS.
    /// </summary>
    /// <remarks>
    /// The ONNX files are TechieRag's own conversion (DECISIONS.md 2026-09-25 decision 3): Qwen's
    /// official release <c>Qwen/Qwen2.5-0.5B-Instruct</c> at commit
    /// <c>7ae557604adf67be50417f59c2c2f167def9a775</c>, converted with ONNX Runtime GenAI 0.16.0's model
    /// builder, <c>python -m onnxruntime_genai.models.builder -p int4 -e cpu --extra_options
    /// int4_block_size=32</c> (about 322 MB). They are published on the owner's Hugging Face repository
    /// <c>techierathore/Qwen2.5-0.5B-Instruct-onnx-genai</c> and pinned to commit
    /// <c>c056eda7447d7df98eba0950ffabd1f95d7aab55</c>; <c>TECHIERAG_MODEL_BASE_URL</c> still redirects
    /// them to <c>&lt;mirror&gt;/qwen2.5-0.5b-instruct-onnx/&lt;file&gt;</c>.
    /// </remarks>
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
                LocalModelFormat.OnnxGenAi,
                "qwen2.5-0.5b-instruct-onnx",
                "https://huggingface.co/techierathore/Qwen2.5-0.5B-Instruct-onnx-genai/resolve/c056eda7447d7df98eba0950ffabd1f95d7aab55",
                [
                    new("chat_template.jinja", "chat_template.jinja", 2_507, "cd8e9439f0570856fd70470bf8889ebd8b5d1107207f67a5efb46e342330527f"),
                    new("genai_config.json", "genai_config.json", 1_581, "a023918fb6dfdf680b0267bdb4ead00083a954194d8178d8fb1af7a0d9adbe63"),
                    new("model.onnx", "model.onnx", 191_725, "4bab94c84fbdd6f3ef6351505aa3cad6d8011e99c5886b2e3def4ddd99f74cd3"),
                    new("model.onnx.data", "model.onnx.data", 320_970_752, "049349730cf56adaf79e2444c11eddc76ac4b114b2953c3959274952a17d5b6c"),
                    new("tokenizer.json", "tokenizer.json", 11_421_892, "3fd169731d2cbde95e10bf356d66d5997fd885dd8dbb6fb4684da3f23b2585d8"),
                    new("tokenizer_config.json", "tokenizer_config.json", 691, "bd9df05957db8b747d5b6fae78d8a00cc1f1bebe46f7bf3d159c17e0aaf9a5b2")
                ])
        ]);

    /// <summary>
    /// Phi-3-mini-4k-instruct (MIT): 3.8 billion parameters, 4,096-token context. The desktop default.
    /// </summary>
    /// <remarks>Microsoft's own ONNX Runtime GenAI build (<c>cpu-int4-rtn-block-32-acc-level-4</c>), pinned to a commit.</remarks>
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

    /// <summary>
    /// Gets the licence the weights are published under, for example <c>Apache-2.0</c>; for a Hugging Face
    /// model, the model card's licence once it has been read (<see cref="GetTermsAsync"/>).
    /// </summary>
    public string LicenceName => Resolution?.Snapshot.LicenceName ?? licenceName;

    /// <summary>
    /// Gets where the licence text is; null for a model loaded from a folder the host supplied. For a
    /// Hugging Face model: the repository's LICENSE file at the pinned commit, else the card's licence
    /// link, else the model's page at that commit; the model's page until it has been read.
    /// </summary>
    public Uri? TermsUrl => Resolution?.Snapshot.TermsUrl ?? termsUrl;

    /// <summary>Gets the chat format the provider applies.</summary>
    public LocalChatTemplate ChatTemplate { get; }

    /// <summary>
    /// Gets the longest context the model supports, in tokens; for a Hugging Face model, 0 until its
    /// <c>genai_config.json</c> is on disk.
    /// </summary>
    public int ContextLength => Volatile.Read(ref contextLength);

    /// <summary>Gets whether the model is allowed on Android and iOS.</summary>
    public bool RunsOnPhones { get; }

    /// <summary>Gets the folder a host-supplied model is loaded from; null for a downloadable model.</summary>
    public string? Folder { get; }

    /// <summary>Gets the memory the context cache takes per token, in bytes.</summary>
    internal long KvBytesPerToken => Volatile.Read(ref kvBytesPerToken);

    /// <summary>Gets the file sets, one per format (one format today); for a Hugging Face model, the resolved one.</summary>
    internal IReadOnlyList<LocalModelVariant> Variants =>
        Resolution is { } resolution ? [resolution.Variant] : variants;

    /// <summary>Gets the Hugging Face name this model was created from, or null.</summary>
    internal HuggingFaceModelSource? HuggingFace { get; }

    /// <summary>Gets the pinned commit, files and folder of a Hugging Face model, once known.</summary>
    internal HuggingFaceResolution? Resolution => Volatile.Read(ref huggingFaceResolution);

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
    /// <param name="folder">
    /// The folder of an ONNX Runtime GenAI model: <c>genai_config.json</c>, the <c>.onnx</c> file(s) it
    /// names and the tokenizer files, as <c>onnxruntime_genai.models.builder</c> writes them.
    /// </param>
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
    /// Names any ONNX Runtime GenAI model published on Hugging Face (REQ-RAG-108 / BRD-166). Nothing is
    /// requested until the model is first used or <see cref="GetTermsAsync"/> is called.
    /// </summary>
    /// <param name="repository">The repository id, <c>owner/name</c>, for example <c>Arm/gemma-3-1b-instruct-onnx-genai-int4-emb-int8</c>.</param>
    /// <param name="folder">The folder inside the repository that holds <c>genai_config.json</c>; null for its root.</param>
    /// <param name="version">
    /// A commit id (used exactly), a branch or a tag; null for the current version. A name that is not a
    /// commit id is resolved to one commit once and recorded in the model root, so a later change on the
    /// page never reaches an installed app and the model works offline after its first download.
    /// </param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentException">A part is malformed (see the remarks).</exception>
    /// <remarks>
    /// <para><b>Licence first.</b> The model card's licence, the terms URL and the download size are read
    /// through Hugging Face's public API and handed to <see cref="LocalLlmOptions.ConfirmTermsAsync"/> (or
    /// carried by <see cref="LocalModelTermsNotAcceptedException"/>) before any model file is requested.</para>
    /// <para><b>Only what the engine loads.</b> From the folder (not its sub-folders): <c>genai_config.json</c>,
    /// the <c>.onnx</c> files and their external data, the tokenizer files, the chat template and
    /// <c>config.json</c>. Each is checked against the fingerprint Hugging Face publishes — SHA-256 for a
    /// large file, the git blob SHA-1 for a small one — and deleted when it does not match.</para>
    /// <para><b>Its own chat template and limits.</b> The prompt is built with the model's own chat
    /// template (<see cref="LocalChatTemplate.ModelDefined"/>); the context length and the memory check's
    /// cache size come from its <c>genai_config.json</c>.</para>
    /// <para><b>Refused:</b> a gated or private model (the library takes no Hugging Face token), a name
    /// outside Hugging Face's character set (letters, digits, <c>.</c>, <c>_</c>, <c>-</c>; no <c>..</c> or
    /// <c>--</c>), and a folder without <c>genai_config.json</c>. <c>TECHIERAG_MODEL_BASE_URL</c> does not
    /// apply: the name is the source. The developer answers for the model they name.</para>
    /// </remarks>
    public static LocalModel FromHuggingFace(string repository, string? folder = null, string? version = null) =>
        FromHuggingFace(repository, folder, version, modelRoot: null);

    /// <summary>
    /// Names a Hugging Face model whose files go to a given model root instead of <see cref="ModelRoot.Current"/>.
    /// </summary>
    /// <param name="repository">The repository id.</param>
    /// <param name="folder">The folder inside it, or null.</param>
    /// <param name="version">The version, or null.</param>
    /// <param name="modelRoot">The model root, or null for <see cref="ModelRoot.Current"/>.</param>
    /// <returns>The model, already resolved when a pinned copy is on disk.</returns>
    internal static LocalModel FromHuggingFace(string repository, string? folder, string? version, string? modelRoot)
    {
        var source = HuggingFaceModelSource.Create(repository, folder, version, modelRoot);
        var model = new LocalModel(
            source.Id,
            source.Id,
            "not read yet from the model card",
            new Uri(HuggingFaceHub.DefaultBaseAddress, source.Repository),
            LocalChatTemplate.ModelDefined,
            contextLength: 0,
            kvBytesPerToken: 0,
            runsOnPhones: true,
            variants: [],
            huggingFace: source);
        HuggingFaceResolution.TryRestore(model);
        return model;
    }

    /// <summary>
    /// Gets the terms a user accepts before this model downloads.
    /// </summary>
    /// <returns>
    /// The model's id, name, licence and terms URL. For a Hugging Face model not yet resolved, the licence
    /// is not known yet: use <see cref="GetTermsAsync"/>.
    /// </returns>
    public LocalModelTerms GetTerms() =>
        new(Id, DisplayName, LicenceName, TermsUrl) { DownloadBytes = Resolution?.Snapshot.DownloadBytes ?? 0 };

    /// <summary>
    /// Gets the terms a user accepts before this model downloads, with the download size. For a Hugging
    /// Face model this asks Hugging Face's API (once; a pinned copy on disk needs no network) and requests
    /// no model file (REQ-RAG-108).
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The terms.</returns>
    /// <exception cref="InvalidOperationException">A Hugging Face model is gated, private, missing, or not in ONNX Runtime GenAI's format.</exception>
    /// <exception cref="HttpRequestException">Hugging Face could not be reached.</exception>
    public Task<LocalModelTerms> GetTermsAsync(CancellationToken cancellationToken = default) =>
        LocalModelStore.Shared.GetTermsAsync(this, LocalModelFormat.OnnxGenAi, cancellationToken);

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
    internal int DefaultContextSize(bool isPhone) =>
        ContextLength > 0 ? Math.Min(ContextLength, isPhone ? 2_048 : 4_096) : isPhone ? 2_048 : 4_096;

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
        Variants.FirstOrDefault(v => v.Format == format);

    /// <summary>
    /// Gets the folder this model loads from for a variant: the host's folder, else
    /// <c>&lt;ModelRoot&gt;/&lt;variant folder&gt;</c> (REQ-RAG-053).
    /// </summary>
    /// <param name="variant">The variant; ignored for a folder model.</param>
    /// <returns>An absolute folder path.</returns>
    internal string GetDirectory(LocalModelVariant? variant) =>
        Folder ?? Resolution?.Directory ?? ModelRoot.GetModelDirectory(variant?.FolderName ?? Id);

    /// <summary>
    /// Records what a Hugging Face model resolved to; its terms, files and folder come from it from now on.
    /// </summary>
    /// <param name="resolution">The pinned snapshot and its folder.</param>
    internal void ApplyResolution(HuggingFaceResolution resolution) =>
        Volatile.Write(ref huggingFaceResolution, resolution);

    /// <summary>
    /// Records the context length and cache size read from the model's own <c>genai_config.json</c>.
    /// </summary>
    /// <param name="config">The values read.</param>
    internal void ApplyGenAiConfig(GenAiConfigInfo config)
    {
        Volatile.Write(ref kvBytesPerToken, config.KvBytesPerToken);
        Volatile.Write(ref contextLength, config.ContextLength);
    }
}
