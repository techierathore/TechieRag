using System.Text.RegularExpressions;
using TechieRag.Llm;
using TechieRag.Local.HuggingFace;
using TechieRag.Local.Runtime;
using TechieRag.Local.Tests.TestDoubles;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// Local picture reading over a scripted runtime: the catalogue's picture models, pictures reaching the
/// model through <see cref="TechieRag.Abstractions.IMultimodalLlmProvider"/>, a text-only model refusing a
/// picture, and a graphics-card model refused before its download (REQ-RAG-145…151 / BRD-203…209).
/// </summary>
public sealed partial class LocalPictureReadingTests : IDisposable
{
    private const string VisionConfig = """
        {
          "model": {
            "type": "phi3v",
            "context_length": 131072,
            "decoder": { "filename": "text.onnx", "num_hidden_layers": 32, "num_attention_heads": 32, "num_key_value_heads": 32, "head_size": 96 },
            "embedding": { "filename": "embedding.onnx" },
            "vision": { "filename": "vision.onnx" }
          }
        }
        """;

    private const string TextConfig = """
        {
          "model": {
            "type": "phi3",
            "context_length": 4096,
            "decoder": { "filename": "model.onnx", "num_hidden_layers": 32, "num_attention_heads": 32, "hidden_size": 3072 }
          }
        }
        """;

    private static readonly byte[] FirstPicture = [0x89, 0x50, 0x4E, 0x47, 1];
    private static readonly byte[] SecondPicture = [0xFF, 0xD8, 0xFF, 2];

    private readonly string root = Path.Combine(Path.GetTempPath(), "techierag-local-tests", "pictures-" + Guid.NewGuid().ToString("N"));

    /// <summary>Removes the folders a test wrote.</summary>
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The laptop picture model is Microsoft's processor build of Phi-3.5 vision, pinned to one commit, every
    /// file sized and SHA-256 hashed, and the picker of a Hugging Face folder takes a picture model's
    /// processor settings and adapters as well.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-145 VisionModelIsPinnedAndHashed")]
    public void VisionModelIsPinnedAndHashed()
    {
        var model = LocalModel.Phi35VisionInstruct;
        var variant = Assert.Single(model.Variants);

        Assert.Equal(
            "https://huggingface.co/microsoft/Phi-3.5-vision-instruct-onnx/resolve/672d73375fa86f3d7787e40ac593e33a4f04a055/cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4",
            variant.DefaultBaseUrl);
        Assert.Equal(11, variant.Files.Count);
        Assert.All(variant.Files, f => Assert.Matches(Sha256(), f.Hash));
        Assert.Equal(3_220_612_860, variant.DownloadBytes);
        Assert.Contains(variant.Files, f => f.FileName == "processor_config.json");
        Assert.False(variant.RequiresGpu);
        Assert.False(model.RunsOnPhones);
        Assert.Equal("MIT", model.LicenceName);
        Assert.True(HuggingFaceFileSelector.IsRuntimeFile("processor_config.json"));
        Assert.True(HuggingFaceFileSelector.IsRuntimeFile("vision_processor.json"));
        Assert.True(HuggingFaceFileSelector.IsRuntimeFile("phi-4-mm-vision.onnx_adapter"));
    }

    /// <summary>
    /// An app that asks for pictures and names no model gets Phi-3.5 vision on a desktop; on a phone, and
    /// with a text model named, the provider says no when it is created (owner decision 2026-11-05).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-145 AskingForPicturesPicksPhi35Vision")]
    public void AskingForPicturesPicksPhi35Vision()
    {
        using var provider = new LocalLlmProvider(new LocalLlmOptions { ReadsImages = true }, null, new FakeLocalLlmRuntime(), new MemoryGate(() => null), LocalModelStore.Shared, isPhone: false);

        Assert.Same(LocalModel.Phi35VisionInstruct, provider.Model);
        Assert.Same(LocalModel.Phi35VisionInstruct, LocalModel.DefaultFor(isPhone: false, readsImages: true));
        Assert.Same(LocalModel.Phi3Mini4kInstruct, LocalModel.DefaultFor(isPhone: false, readsImages: false));
        Assert.Throws<NotSupportedException>(() => LocalModel.DefaultFor(isPhone: true, readsImages: true));
        var named = Assert.Throws<NotSupportedException>(() => new LocalLlmProvider(
            new LocalLlmOptions { ReadsImages = true, Model = LocalModel.Phi3Mini4kInstruct }, null, new FakeLocalLlmRuntime(), new MemoryGate(() => null), LocalModelStore.Shared, isPhone: false));
        Assert.Contains("Phi-3 mini", named.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The graphics-card picture model is in the catalogue, pinned, and refused on this package's processor
    /// engine before its terms are asked and before any file is requested, with a message saying why.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-146 GpuModelRefusedBeforeDownload")]
    public async Task GpuModelRefusedBeforeDownload()
    {
        var model = LocalModel.Phi4MultimodalInstruct;
        var variant = Assert.Single(model.Variants);
        var runtime = new FakeLocalLlmRuntime { Format = LocalModelFormat.OnnxGenAi };
        var termsAsked = 0;
        var directory = model.GetDirectory(variant);
        var filesBefore = Directory.Exists(directory) ? Directory.GetFiles(directory).Length : 0;
        using var provider = new LocalLlmProvider(
            new LocalLlmOptions
            {
                Model = model,
                ConfirmTermsAsync = (_, _) =>
                {
                    termsAsked++;
                    return Task.FromResult(true);
                }
            },
            null,
            runtime,
            new MemoryGate(() => null),
            LocalModelStore.Shared,
            isPhone: false);

        var refused = await Assert.ThrowsAsync<NotSupportedException>(() => provider.LoadAsync());

        Assert.True(model.RequiresGpu);
        Assert.True(variant.RequiresGpu);
        Assert.Matches(PinnedCommit(), variant.DefaultBaseUrl!);
        Assert.All(variant.Files, f => Assert.Matches(Sha256(), f.Hash));
        Assert.Contains("Phi-4 multimodal", refused.Message, StringComparison.Ordinal);
        Assert.Contains("graphics-card", refused.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was downloaded", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, termsAsked);
        Assert.Equal(0, runtime.LoadCount);
        Assert.Equal(filesBefore, Directory.Exists(directory) ? Directory.GetFiles(directory).Length : 0);
    }

    /// <summary>
    /// A turn with two pictures reaches a picture-reading model: both pictures, in order, each with its
    /// marker where it sat in the turn, and the answer comes back as text.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-147 TwoImagesReachVisionModel")]
    public async Task TwoImagesReachVisionModel()
    {
        var runtime = new FakeLocalLlmRuntime { Script = (_, _) => ["Two", " pictures", "."] };
        using var provider = CreateProvider(runtime, VisionConfig);
        var turn = ChatMessage.UserWithImages(
            "What do these show?",
            ChatImage.FromBytes(FirstPicture, "image/png"),
            ChatImage.FromBytes(SecondPicture, "image/jpeg"));

        var response = await provider.ChatAsync([turn]);

        var images = Assert.Single(runtime.Images);
        Assert.Equal(2, images.Count);
        Assert.Equal(FirstPicture, images[0].Bytes);
        Assert.Equal(SecondPicture, images[1].Bytes);
        Assert.Equal("image/jpeg", images[1].MediaType);
        var prompt = Assert.Single(runtime.Prompts);
        Assert.Contains("<|user|>\nWhat do these show?<|image_1|>\n<|image_2|>\n<|end|>", prompt, StringComparison.Ordinal);
        Assert.Equal("Two pictures.", response.Content);
        Assert.True(provider.SupportsVision());
    }

    /// <summary>
    /// A picture the engine counts past the context ends with the same <see cref="LocalPromptTooLongException"/>
    /// any over-long prompt gets, naming the model and the engine's count, never the engine's raw error.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-147 OversizedPicturePromptIsTooLong")]
    public async Task OversizedPicturePromptIsTooLong()
    {
        var runtime = new FakeLocalLlmRuntime { ImagePromptTooLong = 5_000 };
        using var provider = CreateProvider(runtime, VisionConfig);
        var turn = ChatMessage.UserWithImages("Read this.", ChatImage.FromBytes(FirstPicture, "image/png"));

        var refused = await Assert.ThrowsAsync<LocalPromptTooLongException>(() => provider.ChatAsync([turn]));

        Assert.Equal(5_000, refused.PromptTokens);
        Assert.Equal(provider.ContextSize, refused.ContextTokens);
        Assert.Equal(provider.Model.Id, refused.ModelId);
    }

    /// <summary>A picture given as a web address is refused: the local provider never fetches anything.</summary>
    [Fact(DisplayName = "REQ-RAG-147 WebAddressPictureRefused")]
    public async Task WebAddressPictureRefused()
    {
        var runtime = new FakeLocalLlmRuntime();
        using var provider = CreateProvider(runtime, VisionConfig);
        var turn = ChatMessage.UserWithImages("What is this?", ChatImage.FromUrl(new Uri("https://example.com/cal.png"), "image/png"));

        var refused = await Assert.ThrowsAsync<NotSupportedException>(() => provider.ChatAsync([turn]));

        Assert.Contains("never fetches", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, runtime.GenerateCount);
    }

    /// <summary>
    /// The catalogue says per model whether it reads pictures, and the provider's
    /// <c>SupportsInput(Image)</c> gives the same answer for each.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-148 CatalogueSaysWhichModelsReadPictures")]
    public void CatalogueSaysWhichModelsReadPictures()
    {
        Assert.True(LocalModel.Phi35VisionInstruct.ReadsImages);
        Assert.True(LocalModel.Phi4MultimodalInstruct.ReadsImages);
        Assert.False(LocalModel.Qwen25Instruct05B.ReadsImages);
        Assert.False(LocalModel.Phi3Mini4kInstruct.ReadsImages);
        Assert.Equal(4, LocalModel.All.Count);
        Assert.All(LocalModel.All, model =>
        {
            using var provider = new LocalLlmProvider(new LocalLlmOptions { Model = model }, null, new FakeLocalLlmRuntime(), new MemoryGate(() => null), LocalModelStore.Shared, isPhone: false);
            Assert.Equal(model.ReadsImages, provider.SupportsInput(ChatContentKind.Image));
            Assert.True(provider.SupportsInput(ChatContentKind.Text));
        });
    }

    /// <summary>
    /// A model named by its folder or its Hugging Face repository says whether it reads pictures from its own
    /// <c>genai_config.json</c>: a <c>model.vision</c> section means yes.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-148 ModelConfigurationSaysWhetherItReadsPictures")]
    public void ModelConfigurationSaysWhetherItReadsPictures()
    {
        var vision = GenAiConfigInfo.Parse(VisionConfig);
        var text = GenAiConfigInfo.Parse(TextConfig);

        Assert.True(vision.ReadsImages);
        Assert.Equal("phi3v", vision.ModelType);
        Assert.False(text.ReadsImages);
        Assert.True(LocalModel.FromFolder(WriteModelFolder("vision", VisionConfig), LocalChatTemplate.Phi3).ReadsImages);
        Assert.False(LocalModel.FromFolder(WriteModelFolder("text", TextConfig), LocalChatTemplate.Phi3).ReadsImages);
        var named = LocalModel.FromHuggingFace("someone/vision-model-onnx", null, "0123456789abcdef0123456789abcdef01234567", Path.Combine(root, "hf"));
        named.ApplyGenAiConfig(vision);
        Assert.True(named.ReadsImages);
    }

    /// <summary>
    /// A picture sent to Phi-3 mini, a text model, throws an error naming Phi-3 mini before the model is loaded
    /// or run; the picture is never dropped without notice.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-151 ImageToTextModelThrowsNamingModel")]
    public async Task ImageToTextModelThrowsNamingModel()
    {
        var runtime = new FakeLocalLlmRuntime();
        using var provider = new LocalLlmProvider(
            new LocalLlmOptions { Model = LocalModel.Phi3Mini4kInstruct }, null, runtime, new MemoryGate(() => null), LocalModelStore.Shared, isPhone: false);
        var turn = ChatMessage.UserWithImages("What is on this calendar?", ChatImage.FromBytes(FirstPicture, "image/png"));

        var refused = await Assert.ThrowsAsync<LocalModelInputNotSupportedException>(() => provider.ChatAsync([turn]));

        Assert.Contains("Phi-3 mini", refused.Message, StringComparison.Ordinal);
        Assert.Equal(LocalModel.Phi3Mini4kInstruct.Id, refused.ModelId);
        Assert.Equal(ChatContentKind.Image, refused.InputKind);
        Assert.Equal(1, refused.Count);
        Assert.Equal(0, runtime.LoadCount);
        Assert.Equal(0, runtime.GenerateCount);
        Assert.False(provider.SupportsVision());
    }

    /// <summary>
    /// A folder model whose configuration has no vision section refuses a picture too, and its streaming
    /// path refuses it the same way, before any token is generated.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-151 FolderTextModelRefusesPictureWhenStreaming")]
    public async Task FolderTextModelRefusesPictureWhenStreaming()
    {
        var runtime = new FakeLocalLlmRuntime();
        using var provider = CreateProvider(runtime, TextConfig);
        var turn = ChatMessage.UserWithImages("Read this.", ChatImage.FromBytes(FirstPicture, "image/png"));

        await Assert.ThrowsAsync<LocalModelInputNotSupportedException>(async () =>
        {
            await foreach (var _ in provider.ChatStreamEventsAsync([turn]))
            {
            }
        });

        Assert.Equal(0, runtime.GenerateCount);
    }

    /// <summary>A text-only turn on a picture-reading model is unchanged: no marker, the text path.</summary>
    [Fact(DisplayName = "REQ-RAG-147 TextTurnOnVisionModelUsesTextPath")]
    public async Task TextTurnOnVisionModelUsesTextPath()
    {
        var runtime = new FakeLocalLlmRuntime();
        using var provider = CreateProvider(runtime, VisionConfig);

        var response = await provider.CompleteAsync("Say hello.");

        Assert.Empty(runtime.Images);
        Assert.DoesNotContain("<|image_", Assert.Single(runtime.Prompts), StringComparison.Ordinal);
        Assert.Equal("Hello there.", response.Content);
    }

    /// <summary>
    /// The DevGuide's local picture sample is the code the live sample test runs, line for line, so the
    /// sample a developer copies is the one that was run (REQ-RAG-149 / BRD-207).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-149 DevGuideSampleIsTheTestedCode")]
    public void DevGuideSampleIsTheTestedCode()
    {
        var test = RepoFiles.Read(Path.Combine("tests", "TechieRag.Local.Tests", "Live", "LiveVisionTests.cs")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var guide = RepoFiles.Read(Path.Combine("docs", "TechieRag-P3-DevGuide.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var begin = test.IndexOf("// --- DevGuide sample: begin ---", StringComparison.Ordinal);
        var end = test.IndexOf("// --- DevGuide sample: end ---", StringComparison.Ordinal);
        Assert.True(begin > 0 && end > begin, "The live test has no DevGuide sample markers.");
        var sampleLines = test[begin..end].Split('\n').Skip(1).Select(line => line.Trim()).Where(line => line.Length > 0).ToList();

        var section = guide[guide.IndexOf("### Local picture reading", StringComparison.Ordinal)..];
        var block = section[(section.IndexOf("```csharp\n", StringComparison.Ordinal) + 10)..];
        var guideLines = block[..block.IndexOf("```", StringComparison.Ordinal)].Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();

        Assert.Equal(sampleLines, guideLines);
        Assert.Contains(guideLines, line => line.Contains("ChatMessage.UserWithImages(", StringComparison.Ordinal));
    }

    private LocalLlmProvider CreateProvider(FakeLocalLlmRuntime runtime, string config)
    {
        var folder = WriteModelFolder(Guid.NewGuid().ToString("N"), config);
        var options = new LocalLlmOptions { Model = LocalModel.FromFolder(folder, LocalChatTemplate.Phi3) };
        return new LocalLlmProvider(options, null, runtime, new MemoryGate(() => null), LocalModelStore.Shared, isPhone: false);
    }

    private string WriteModelFolder(string name, string config)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, GenAiConfigInfo.FileName), config);
        return folder;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();

    [GeneratedRegex("/resolve/[0-9a-f]{40}/")]
    private static partial Regex PinnedCommit();
}
