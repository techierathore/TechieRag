using TechieRag.Local.Tests.Conformance;
using TechieRag.Local.Tests.TestDoubles;
using TechieRag.Models;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// What the provider itself does above any runtime — template, options, stop sequences, JSON,
/// memory and platform checks — observed through the scripted runtime (REQ-RAG-059, REQ-RAG-060,
/// REQ-RAG-063, REQ-RAG-065).
/// </summary>
public sealed class LocalLlmProviderTests
{
    private readonly FakeLocalLlmRuntime runtime = new();

    /// <summary>The model receives the conversation in its own chat template, assistant turn opened.</summary>
    [Fact]
    public async Task ChatTemplateIsAppliedExactly()
    {
        using var provider = FakeProviderFactory.Create(runtime);

        await provider.ChatAsync([ChatMessage.User("Say hello.")]);

        Assert.Equal("<|im_start|>user\nSay hello.<|im_end|>\n<|im_start|>assistant\n", runtime.Prompts.Single());
    }

    /// <summary>The SystemPrompt option opens the conversation as a system turn.</summary>
    [Fact]
    public async Task SystemPromptOptionOpensTheConversation()
    {
        using var provider = FakeProviderFactory.Create(runtime);

        await provider.CompleteAsync("Hi", new LlmCompletionOptions { SystemPrompt = "Be brief." });

        Assert.StartsWith("<|im_start|>system\nBe brief.<|im_end|>\n<|im_start|>user\nHi<|im_end|>", runtime.Prompts.Single());
    }

    /// <summary>Temperature, TopP, Seed, penalties, MaxTokens and stop sequences all reach the runtime.</summary>
    [Fact(DisplayName = "REQ-RAG-059 OptionsReachTheRuntime")]
    public async Task OptionsReachTheRuntime()
    {
        using var provider = FakeProviderFactory.Create(runtime);

        await provider.CompleteAsync("Hi", new LlmCompletionOptions
        {
            Temperature = 0.2f,
            TopP = 0.5f,
            Seed = 9,
            FrequencyPenalty = 0.1f,
            PresencePenalty = 0.3f,
            MaxTokens = 7,
            StopSequences = ["END"]
        });

        var settings = runtime.Settings.Single();
        Assert.Equal((0.2f, 0.5f, 9, 0.1f, 0.3f, 7), (settings.Temperature, settings.TopP, settings.Seed, settings.FrequencyPenalty, settings.PresencePenalty, settings.MaxTokens));
        Assert.Equal(["END", "<|im_end|>"], settings.StopSequences);
    }

    /// <summary>Without per-call options the provider's defaults apply.</summary>
    [Fact]
    public async Task ProviderDefaultsApplyWhenCallSetsNone()
    {
        using var provider = FakeProviderFactory.Create(runtime, o => { o.Temperature = 0.4f; o.TopP = 0.8f; o.MaxTokens = 11; });

        await provider.CompleteAsync("Hi");

        var settings = runtime.Settings.Single();
        Assert.Equal((0.4f, 0.8f, 11), (settings.Temperature, settings.TopP, settings.MaxTokens));
    }

    /// <summary>A caller's stop sequence ends the answer, and no part of it is ever streamed.</summary>
    [Fact]
    public async Task CallerStopSequenceEndsTheAnswer()
    {
        runtime.Script = (_, _) => ["Hello", " ST", "OP", " world"];
        using var provider = FakeProviderFactory.Create(runtime);
        var stop = new LlmCompletionOptions { StopSequences = ["STOP"] };

        var deltas = await provider.ChatStreamAsync([ChatMessage.User("Hi")], stop).ToListAsync();
        var response = await provider.ChatAsync([ChatMessage.User("Hi")], stop);

        Assert.Equal("Hello ", string.Concat(deltas));
        Assert.DoesNotContain(deltas, d => d.Contains("ST", StringComparison.Ordinal));
        Assert.Equal("stop", response.FinishReason);
    }

    /// <summary>The template's end-of-turn marker ends the answer even when the runtime keeps going.</summary>
    [Fact]
    public async Task EndOfTurnMarkerEndsTheAnswer()
    {
        runtime.Script = (_, _) => ["Hi", "<|im_end|>", "junk"];
        using var provider = FakeProviderFactory.Create(runtime);

        var response = await provider.CompleteAsync("Hello");

        Assert.Equal("Hi", response.Content);
    }

    /// <summary>MaxTokens is clamped to the room the prompt leaves in the context.</summary>
    [Fact]
    public async Task MaxTokensClampedToContextRoom()
    {
        using var provider = FakeProviderFactory.Create(runtime, o => o.ContextSize = 64);

        await provider.CompleteAsync("Hi", new LlmCompletionOptions { MaxTokens = 1_000 });

        var prompt = runtime.Prompts.Single();
        Assert.Equal(64 - FakeLocalModel.Count(prompt), runtime.Settings.Single().MaxTokens);
    }

    /// <summary>The model is loaded once however many calls follow.</summary>
    [Fact]
    public async Task ModelLoadsOnce()
    {
        using var provider = FakeProviderFactory.Create(runtime);

        await provider.CompleteAsync("One");
        await provider.CompleteAsync("Two");

        Assert.Equal(1, runtime.LoadCount);
        Assert.True(provider.IsLoaded);
    }

    /// <summary>A per-call model other than the loaded one is refused; the loaded one, bare or routed, is accepted.</summary>
    [Fact]
    public async Task PerCallModelOverrideIsRefused()
    {
        using var provider = FakeProviderFactory.Create(runtime);

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.CompleteAsync("Hi", new LlmCompletionOptions { Model = "other-model" }));
        var routed = await provider.CompleteAsync("Hi", new LlmCompletionOptions { Model = "local/" + provider.ModelName });

        Assert.NotNull(routed.Content);
    }

    /// <summary>CompleteAsync&lt;T&gt; sends T's JSON schema with JSON mode so a runtime can constrain to it.</summary>
    [Fact]
    public async Task TypedAnswerSendsSchema()
    {
        runtime.Script = (_, _) => ["""{"name":"Ada","age":36}"""];
        using var provider = FakeProviderFactory.Create(runtime);

        await provider.CompleteAsync<LocalLlmConformanceTests.PersonAnswer>("Who?");

        var settings = runtime.Settings.Single();
        Assert.True(settings.JsonMode);
        Assert.Contains("\"Name\"", settings.JsonSchema);
        Assert.Contains("\"Age\"", settings.JsonSchema);
        Assert.Equal(0f, settings.Temperature);
    }

    /// <summary>An answer wrapped in a markdown code fence still parses.</summary>
    [Fact]
    public async Task TypedAnswerStripsCodeFence()
    {
        runtime.Script = (_, _) => ["```json\n", """{"name":"Ada","age":36}""", "\n```"];
        using var provider = FakeProviderFactory.Create(runtime);

        var person = await provider.CompleteAsync<LocalLlmConformanceTests.PersonAnswer>("Who?");

        Assert.Equal("Ada", person.Name);
    }

    /// <summary>An answer that is not JSON fails with a message naming the target type.</summary>
    [Fact]
    public async Task TypedAnswerRejectsInvalidJson()
    {
        runtime.Script = (_, _) => ["Sorry, I cannot."];
        using var provider = FakeProviderFactory.Create(runtime);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CompleteAsync<LocalLlmConformanceTests.PersonAnswer>("Who?"));

        Assert.Contains(nameof(LocalLlmConformanceTests.PersonAnswer), error.Message);
    }

    /// <summary>Before the model is on disk there is no tokenizer, and the count falls back to the estimate.</summary>
    [Fact]
    public void EstimateBeforeDownloadFallsBack()
    {
        using var provider = new LocalLlmProvider(
            new LocalLlmOptions { Model = LocalModel.Qwen25Instruct05B }, null, runtime, new MemoryGate(() => null), LocalModelStore.Shared, isPhone: false);

        Assert.Equal(3, provider.EstimateTokenCount("twelve chars"));
    }

    /// <summary>With no runtime chosen for the platform, a load is refused with the reason and nothing else happens.</summary>
    [Fact]
    public async Task NoRuntimeRefusesWithReason()
    {
        using var provider = FakeProviderFactory.Create(runtime: null);

        var error = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => provider.LoadAsync());

        Assert.Contains("runtime", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// With less free memory than the model needs, the load throws a message naming the shortfall,
    /// nothing is loaded, and the provider stays usable (the app keeps running).
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-060 MemoryShortfallRefusesBeforeLoad")]
    public async Task MemoryShortfallRefusesBeforeLoad()
    {
        using var provider = FakeProviderFactory.Create(runtime, freeMemory: 1_000_000);

        var error = await Assert.ThrowsAsync<LocalModelMemoryException>(() => provider.LoadAsync());
        await Assert.ThrowsAsync<LocalModelMemoryException>(() => provider.CompleteAsync("again"));

        Assert.Equal(1_000_000, error.AvailableBytes);
        Assert.True(error.ShortfallBytes > 0);
        Assert.Contains("short", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, runtime.LoadCount);
    }

    /// <summary>When the platform gives no memory reading the load goes ahead.</summary>
    [Fact]
    public async Task UnknownMemoryDoesNotBlock()
    {
        using var provider = FakeProviderFactory.Create(runtime, freeMemory: null);

        await provider.LoadAsync();

        Assert.Equal(1, runtime.LoadCount);
    }

    /// <summary>On a phone the desktop model is refused at configuration time, naming the phone model.</summary>
    [Fact]
    public void PhoneRefusesDesktopModel()
    {
        var error = Assert.Throws<NotSupportedException>(() => new LocalLlmProvider(
            new LocalLlmOptions { Model = LocalModel.Phi3Mini4kInstruct }, null, runtime, new MemoryGate(() => null), LocalModelStore.Shared, isPhone: true));

        Assert.Contains(LocalModel.Qwen25Instruct05B.Id, error.Message);
    }

    /// <summary>The platform default is the small model on a phone (2,048-token context) and Phi-3 mini on a desktop.</summary>
    [Fact]
    public void PlatformDefaultsDiffer()
    {
        using var phone = new LocalLlmProvider(new LocalLlmOptions(), null, runtime, new MemoryGate(() => null), LocalModelStore.Shared, isPhone: true);
        using var desktop = new LocalLlmProvider(new LocalLlmOptions(), null, runtime, new MemoryGate(() => null), LocalModelStore.Shared, isPhone: false);

        Assert.Equal((LocalModel.Qwen25Instruct05B.Id, 2_048), (phone.ModelName, phone.ContextSize));
        Assert.Equal((LocalModel.Phi3Mini4kInstruct.Id, 4_096), (desktop.ModelName, desktop.ContextSize));
    }

    /// <summary>The memory estimate is the weights, the context cache per token and the runtime's working memory.</summary>
    [Fact]
    public void MemoryEstimateCountsWeightsAndContext()
    {
        var required = LocalModel.Qwen25Instruct05B.EstimateMemoryBytes(491_400_032, 2_048);

        Assert.Equal(491_400_032 + (12_288L * 2_048) + LocalModel.RuntimeOverheadBytes, required);
    }
}
