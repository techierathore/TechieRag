using TechieRag.Local.HuggingFace;
using TechieRag.Local.Runtime;
using TechieRag.Models;

namespace TechieRag.Local;

/// <summary>
/// A language model <c>UseLocalLlm()</c> can download and run in-process: its id, licence, chat
/// template, context length and memory needs (REQ-RAG-057 / BRD-96).
/// </summary>
/// <remarks>
/// <para><b>Four models ship.</b> <see cref="Qwen25Instruct05B"/> (0.5 billion parameters, about 0.3
/// GB) is the default on Android and iOS. <see cref="Phi3Mini4kInstruct"/> (3.8 billion parameters,
/// about 2.7 GB) is the default on desktops. <see cref="PlatformDefault"/> picks
/// between them. Two read pictures (<see cref="ReadsImages"/>, REQ-RAG-145…148 / BRD-203…206):
/// <see cref="Phi35VisionInstruct"/> runs on a laptop's processor and is the desktop default when an app
/// asks for pictures (<see cref="LocalLlmOptions.ReadsImages"/>); <see cref="Phi4MultimodalInstruct"/> is a
/// graphics-card build (<see cref="RequiresGpu"/>) that this package's processor engine refuses before any
/// download. Neither runs on phones.</para>
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
    private volatile bool readsImages;
    private volatile bool imageSupportKnown;
    private volatile string? modelType;
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
    /// <param name="supportsToolCalling">Whether the provider should treat this model as able to call tools.</param>
    /// <param name="readsImages">Whether the model reads pictures; null when its configuration has not been read yet.</param>
    /// <param name="modelType">The engine's model type, for example <c>phi3v</c>; null when not known yet.</param>
    /// <param name="imageTokenFloor">The fewest prompt tokens one picture takes, for the check before the engine counts it.</param>
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
        HuggingFaceModelSource? huggingFace = null,
        bool supportsToolCalling = false,
        bool? readsImages = false,
        string? modelType = null,
        int imageTokenFloor = 0)
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
        SupportsToolCalling = supportsToolCalling;
        this.readsImages = readsImages ?? false;
        imageSupportKnown = readsImages is not null;
        this.modelType = modelType;
        ImageTokenFloor = imageTokenFloor > 0 ? imageTokenFloor : DefaultImageTokenFloor;
    }

    /// <summary>
    /// The fewest prompt tokens one picture takes when a model's own figure is not known: Phi-3.5 vision's
    /// overview of the whole picture, 144 image tokens and a separator. A picture takes more the larger it is
    /// (a 1200 × 900 calendar page took 1,960 with its prompt, measured 2026-11-05), so the engine's own count,
    /// read after it has processed the picture, is the one the context check trusts.
    /// </summary>
    internal const int DefaultImageTokenFloor = 145;

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
        ],
        supportsToolCalling: true);

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
        ],
        supportsToolCalling: true);

    /// <summary>
    /// Phi-3.5-vision-instruct (MIT): 4.2 billion parameters, reads pictures, runs on a laptop's processor
    /// (REQ-RAG-145 / BRD-203). The desktop default when an app asks for pictures and names no model.
    /// </summary>
    /// <remarks>
    /// Microsoft's own ONNX Runtime GenAI build, <c>microsoft/Phi-3.5-vision-instruct-onnx</c>, folder
    /// <c>cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4</c>, pinned to commit
    /// <c>672d73375fa86f3d7787e40ac593e33a4f04a055</c> (about 3.2 GB). Every file carries its SHA-256, read on
    /// 2026-11-05: from Hugging Face's large-file record for the weights, computed from the file at that
    /// commit for the five small ones. Too large for phones.
    /// </remarks>
    public static LocalModel Phi35VisionInstruct { get; } = new(
        "phi-3.5-vision-instruct",
        "Phi-3.5 vision Instruct",
        "MIT",
        new Uri("https://huggingface.co/microsoft/Phi-3.5-vision-instruct/blob/main/LICENSE"),
        LocalChatTemplate.Phi3,
        contextLength: 131_072,
        kvBytesPerToken: 393_216,
        runsOnPhones: false,
        variants:
        [
            new LocalModelVariant(
                LocalModelFormat.OnnxGenAi,
                "phi-3.5-vision-instruct-onnx",
                "https://huggingface.co/microsoft/Phi-3.5-vision-instruct-onnx/resolve/672d73375fa86f3d7787e40ac593e33a4f04a055/cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4",
                [
                    new("genai_config.json", "genai_config.json", 2_121, "aab58b61ca85a8bf2d4781fe1b4299e3d0f20a4cd902bc5c02adfe361b62aad9"),
                    new("phi-3.5-v-instruct-embedding.onnx", "phi-3.5-v-instruct-embedding.onnx", 6_091, "83bb330b0a319812eccdf6b6bc5ce17eb6a120bcf76120f6af27c19c3d396ea1"),
                    new("phi-3.5-v-instruct-embedding.onnx.data", "phi-3.5-v-instruct-embedding.onnx.data", 394_002_432, "2dcbf0fcb14433ac8fa82b35c39675c01fb552512b4c0e5d54e8f247046ef393"),
                    new("phi-3.5-v-instruct-text.onnx", "phi-3.5-v-instruct-text.onnx", 52_123_846, "f7580bd589693e87e3825ffe8eda3f5fda4158d2d82d27803c263aa0e7454a81"),
                    new("phi-3.5-v-instruct-text.onnx.data", "phi-3.5-v-instruct-text.onnx.data", 2_327_285_760, "214fd2e8a09b8c7b141f9095171aed6f994768a36803b7eeb9ea3be16da99dd6"),
                    new("phi-3.5-v-instruct-vision.onnx", "phi-3.5-v-instruct-vision.onnx", 414_021, "e06ccb2d2c35c2565f0abe507241f33cae9d7f63a1d79f6170b317d12f35c3bf"),
                    new("phi-3.5-v-instruct-vision.onnx.data", "phi-3.5-v-instruct-vision.onnx.data", 444_916_232, "45af0e626ba0eff702d9c1873c84f9e60f1f17bb2036bf1abc045968b5e645df"),
                    new("processor_config.json", "processor_config.json", 752, "b6f5a3ceb16ad076b31430e358d68f0ebb53d9ddeb91270e9e5c4ff8fefed67f"),
                    new("special_tokens_map.json", "special_tokens_map.json", 670, "f9535fada19315e0d8eb89521cebe111c008fee98d16e7c6744acfb50505d8c3"),
                    new("tokenizer.json", "tokenizer.json", 1_851_388, "478fb6fd0bf1403eb2ffda892e285f5ae968b9e0c06eb2815552ebedacb8ca93"),
                    new("tokenizer_config.json", "tokenizer_config.json", 9_547, "d637971d627e4f8ce0422401bd0a305d9f763657ad21e8205afe8d36b6672524")
                ])
        ],
        readsImages: true,
        modelType: "phi3v",
        imageTokenFloor: DefaultImageTokenFloor);

    /// <summary>
    /// Phi-4-multimodal-instruct (MIT): 5.6 billion parameters, reads pictures, a graphics-card build
    /// (REQ-RAG-146 / BRD-204). <see cref="RequiresGpu"/> is true: this package ships the processor engine
    /// only, so loading it refuses before any download with a message saying so.
    /// </summary>
    /// <remarks>
    /// Microsoft's own ONNX Runtime GenAI build, <c>microsoft/Phi-4-multimodal-instruct-onnx</c>, folder
    /// <c>gpu/gpu-int4-rtn-block-32</c>, pinned to commit <c>295df4cddf8c3740df7cd66e5d05f06a872fcaf5</c>
    /// (about 5.1 GB). Microsoft publishes no processor build of it. One picture takes at least one 256-token
    /// overview and up to 36 crops more. Too large for phones.
    /// </remarks>
    public static LocalModel Phi4MultimodalInstruct { get; } = new(
        "phi-4-multimodal-instruct",
        "Phi-4 multimodal Instruct",
        "MIT",
        new Uri("https://huggingface.co/microsoft/Phi-4-multimodal-instruct/blob/main/LICENSE"),
        LocalChatTemplate.Phi3,
        contextLength: 131_072,
        kvBytesPerToken: 131_072,
        runsOnPhones: false,
        variants:
        [
            new LocalModelVariant(
                LocalModelFormat.OnnxGenAi,
                "phi-4-multimodal-instruct-onnx-gpu",
                "https://huggingface.co/microsoft/Phi-4-multimodal-instruct-onnx/resolve/295df4cddf8c3740df7cd66e5d05f06a872fcaf5/gpu/gpu-int4-rtn-block-32",
                [
                    new("added_tokens.json", "added_tokens.json", 249, "d4f2aceb0f20b71dd1f4bcc7e052e4412946bf281840b8f83d39f259571af486"),
                    new("genai_config.json", "genai_config.json", 2_919, "d674802d88d850c2f8cf2361f419160f2c739376c78361f00736740c9e6658e3"),
                    new("merges.txt", "merges.txt", 2_418_348, "856ce61180bb689282eed6b3a6838bb1f438399be23aefe9d20eb379791fb4ad"),
                    new("phi-4-mm-embedding.onnx", "phi-4-mm-embedding.onnx", 12_008, "e201c5981608c06f93b15860993bf6029cfc3f60401290926c404ab84946dddd"),
                    new("phi-4-mm-embedding.onnx.data", "phi-4-mm-embedding.onnx.data", 1_229_193_216, "85e0389fd112f00584016e1429588410e07f548cbedc5111c84bafabb14c825b"),
                    new("phi-4-mm-speech.onnx", "phi-4-mm-speech.onnx", 428_970, "7b3d608bbc3affc2228f065f38962efc59cfb219ab21bcb3add01267b6e97ce3"),
                    new("phi-4-mm-speech.onnx.data", "phi-4-mm-speech.onnx.data", 519_824_640, "d28dc86d07fe3469e307bf06d6ad05dde254f58b7e5625d9b6f88ac65284878e"),
                    new("phi-4-mm-speech.onnx_adapter", "phi-4-mm-speech.onnx_adapter", 312_704_560, "67d2ef0f27d22477f5c48f750b25c67e728b79c72d7c72ae5dd5c2090dd48ddd"),
                    new("phi-4-mm-text.onnx", "phi-4-mm-text.onnx", 236_917_443, "f84c26e4745958d98628619dbc9f6bc17cf72d9642a34053197301a9f6e10b18"),
                    new("phi-4-mm-text.onnx.data", "phi-4-mm-text.onnx.data", 2_184_001_536, "e64a9c88de1e7e316e11a6a86e063a533ce26cc8e92e65003e2daebf6e0106b6"),
                    new("phi-4-mm-vision.onnx", "phi-4-mm-vision.onnx", 376_524, "c5ef47554450d7bf96f9dcfe28e6bbf2c99beddcd5dfc8786d69713b7f70e9fd"),
                    new("phi-4-mm-vision.onnx.data", "phi-4-mm-vision.onnx.data", 383_529_536, "0faf7f9b430c3092737e96e5f98b5509022e8cd5063cc5c05e4987503fbd7728"),
                    new("phi-4-mm-vision.onnx_adapter", "phi-4-mm-vision.onnx_adapter", 250_183_216, "b596e59e6092dfda593f449abc7c6437cf46cc1f366e5283475299ae2e7629ab"),
                    new("special_tokens_map.json", "special_tokens_map.json", 587, "aff38493227d813e29fcf8406e8e90062f1f031aa47d589325e9c31d89ac7cc3"),
                    new("speech_processor.json", "speech_processor.json", 1_799, "77ac9c85f4cc8b947bdd19d77ba71de814f76ef6658130e344ee4ecc2eef7014"),
                    new("tokenizer.json", "tokenizer.json", 15_524_479, "4c1b9f641d4f8b7247b8d5007dd3b6a9f6a87cb5123134fe0d326f14d10c0585"),
                    new("tokenizer_config.json", "tokenizer_config.json", 3_274, "60d63678e0cfc6c6c3deb98419b4326a3ada8680165aa0318389355383208b41"),
                    new("vision_processor.json", "vision_processor.json", 2_010, "e40a4384c7f530f73514fd87d3fe9219be2a012fded0037bfa06a5468067623a"),
                    new("vocab.json", "vocab.json", 3_910_310, "6cb65a857824fa6615bb1782d95d882617a8bbce1da0317118586b36f39e98bd")
                ],
                RequiresGpu: true)
        ],
        readsImages: true,
        modelType: "phi4mm",
        imageTokenFloor: 257);

    /// <summary>Every model this package can download.</summary>
    public static IReadOnlyList<LocalModel> All { get; } =
        [Qwen25Instruct05B, Phi3Mini4kInstruct, Phi35VisionInstruct, Phi4MultimodalInstruct];

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

    /// <summary>
    /// Gets whether this model can call tools through <see cref="LocalLlmProvider"/> (TR-RAG-052).
    /// True for the shipped catalogue models trained for tool calling; false for host-supplied
    /// folder models unless explicitly enabled.
    /// </summary>
    public bool SupportsToolCalling { get; }

    /// <summary>Gets the folder a host-supplied model is loaded from; null for a downloadable model.</summary>
    public string? Folder { get; }

    /// <summary>
    /// Gets whether the model reads pictures: a chat turn's <c>ChatImage</c> parts reach it, through
    /// <c>IMultimodalLlmProvider</c> (REQ-RAG-148 / BRD-206). True for <see cref="Phi35VisionInstruct"/> and
    /// <see cref="Phi4MultimodalInstruct"/>, false for the text models. For a model named on Hugging Face or
    /// placed in a folder it is read from the model's own <c>genai_config.json</c> (a <c>model.vision</c>
    /// section): false until that file is on disk, which for a Hugging Face model is after its download.
    /// </summary>
    public bool ReadsImages => readsImages;

    /// <summary>
    /// Gets whether the model is a graphics-card build that needs an ONNX Runtime GenAI GPU runtime
    /// (REQ-RAG-146 / BRD-204). This package ships the processor engine only, so such a model is refused before
    /// any download; a host can check this to leave it out of a model list.
    /// </summary>
    public bool RequiresGpu => Variants.Any(v => v.RequiresGpu);

    /// <summary>Gets whether <see cref="ReadsImages"/> is settled; false for a model whose configuration is not on disk yet.</summary>
    internal bool ImageSupportKnown => imageSupportKnown;

    /// <summary>Gets the engine's model type from its configuration, for example <c>phi3v</c>; null when not known.</summary>
    internal string? ModelType => modelType;

    /// <summary>Gets the fewest prompt tokens one picture takes, for the context check before the engine counts it.</summary>
    internal int ImageTokenFloor { get; }

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
        long kvBytesPerToken = 131_072) =>
        FromFolder(folder, chatTemplate, contextLength, kvBytesPerToken, supportsToolCalling: false);

    /// <summary>
    /// Describes a model the host has already placed in a folder, optionally enabling tool calling.
    /// </summary>
    /// <param name="folder">The folder of an ONNX Runtime GenAI model.</param>
    /// <param name="chatTemplate">The chat format the model was trained on.</param>
    /// <param name="contextLength">The longest context the model supports, in tokens.</param>
    /// <param name="kvBytesPerToken">Memory the context cache takes per token.</param>
    /// <param name="supportsToolCalling">Whether the provider should treat this model as able to call tools.</param>
    /// <returns>The model.</returns>
    internal static LocalModel FromFolder(
        string folder,
        LocalChatTemplate chatTemplate,
        int contextLength,
        long kvBytesPerToken,
        bool supportsToolCalling)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (contextLength <= 0) throw new ArgumentException("The context length must be positive.", nameof(contextLength));

        var fullPath = Path.GetFullPath(folder);
        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var model = new LocalModel(name, name, "supplied by the host", null, chatTemplate, contextLength, kvBytesPerToken,
            runsOnPhones: true, variants: [], folder: fullPath, supportsToolCalling: supportsToolCalling, readsImages: null);
        model.TryReadImageSupport(fullPath);
        return model;
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
            huggingFace: source,
            readsImages: null);
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
    /// Picks the default model for a platform when the app asks for pictures or not (owner decision 2026-11-05).
    /// </summary>
    /// <param name="isPhone">Whether the platform is Android or iOS.</param>
    /// <param name="readsImages">Whether the app asked for a model that reads pictures.</param>
    /// <returns><see cref="Phi35VisionInstruct"/> for pictures on a desktop; otherwise <see cref="DefaultFor(bool)"/>.</returns>
    /// <exception cref="NotSupportedException">Pictures were asked for on Android or iOS, where no picture model runs.</exception>
    internal static LocalModel DefaultFor(bool isPhone, bool readsImages)
    {
        if (!readsImages)
        {
            return DefaultFor(isPhone);
        }

        if (isPhone)
        {
            throw new NotSupportedException(
                "No local model that reads pictures runs on Android or iOS in this version of TechieRag.Local. "
                + "Send pictures to a hosted provider on a phone, or read them on a desktop with LocalModel.Phi35VisionInstruct.");
        }

        return Phi35VisionInstruct;
    }

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
    /// Estimates the free memory loading this model needs, before anything is downloaded, so a host can
    /// compare it with <see cref="AvailableMemory.Read"/> before offering the download (REQ-RAG-125, Sevak TR-RAG-047).
    /// </summary>
    /// <param name="contextSize">
    /// The context size in tokens the model will be loaded with; null uses the default a provider picks on
    /// this platform (2,048 on a phone, 4,096 elsewhere), never more than the model supports. Pass
    /// <see cref="LocalLlmProvider.ContextSize"/> to match a configured provider.
    /// </param>
    /// <returns>
    /// Bytes: the weights, the context cache and the runtime's working memory, the same figure the load checks.
    /// Null when the weight size is not known yet: a Hugging Face model before
    /// <see cref="LocalLlmProvider.GetTermsAsync"/> has read its file list, or a host folder that does not exist.
    /// </returns>
    /// <remarks>
    /// For a Hugging Face model the context cache size is read from its <c>genai_config.json</c>, which arrives
    /// with the download; before then the figure counts the weights and the runtime only, a lower bound.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="contextSize"/> is zero or negative.</exception>
    public long? EstimateRequiredMemoryBytes(int? contextSize = null)
    {
        if (contextSize is { } requested)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requested, nameof(contextSize));
        }

        long? weightBytes = Folder is not null
            ? Directory.Exists(Folder) ? FolderBytes(Folder) : null
            : FindVariant(LocalModelFormat.OnnxGenAi)?.DownloadBytes;
        if (weightBytes is not { } weights)
        {
            return null;
        }

        var size = contextSize ?? DefaultContextSize(IsPhonePlatform);
        if (ContextLength > 0)
        {
            size = Math.Min(ContextLength, size);
        }

        return EstimateMemoryBytes(weights, size);
    }

    /// <summary>Sums the size of every file under a folder.</summary>
    /// <param name="directory">The folder.</param>
    /// <returns>Bytes; 0 when the folder does not exist.</returns>
    internal static long FolderBytes(string directory) =>
        Directory.Exists(directory)
            ? new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;

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
        ApplyImageSupport(config);
    }

    /// <summary>Records whether the model reads pictures, and its type, from its own configuration (REQ-RAG-148).</summary>
    /// <param name="config">The values read.</param>
    internal void ApplyImageSupport(GenAiConfigInfo config)
    {
        modelType = config.ModelType ?? modelType;
        readsImages = config.ReadsImages;
        imageSupportKnown = true;
    }

    /// <summary>Reads whether a folder model reads pictures when its configuration is already there; otherwise leaves it unknown.</summary>
    /// <param name="directory">The model folder.</param>
    internal void TryReadImageSupport(string directory)
    {
        if (!File.Exists(Path.Combine(directory, GenAiConfigInfo.FileName)))
        {
            return;
        }

        try
        {
            ApplyImageSupport(GenAiConfigInfo.Read(directory));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // Read again, with its error surfaced, when the model loads.
        }
    }
}
