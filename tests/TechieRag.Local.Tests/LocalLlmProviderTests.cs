using TechieRag.Local.Tests.Conformance;
using TechieRag.Local.Tests.TestDoubles;
using TechieRag.Models;
using TechieRag.Services;
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

    /// <summary>The shipped catalogue models advertise tool calling; a plain folder model does not.</summary>
    [Fact]
    public void CatalogueModelsSupportToolCalling()
    {
        Assert.True(LocalModel.Qwen25Instruct05B.SupportsToolCalling);
        Assert.True(LocalModel.Phi3Mini4kInstruct.SupportsToolCalling);

        using var unsupported = FakeProviderFactory.Create(runtime);
        Assert.False(unsupported.SupportsToolCalling);
    }

    /// <summary>A model that does not support tool calling still refuses a request that carries tools.</summary>
    [Fact]
    public async Task UnsupportedModelRefusesTools()
    {
        using var provider = FakeProviderFactory.Create(runtime);
        var options = new LlmCompletionOptions
        {
            Tools = [new ToolDefinition { Name = "lookup", Description = "Looks things up.", ParametersSchema = """{"type":"object"}""" }]
        };

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => provider.ChatAsync([ChatMessage.User("Hi")], options));

        Assert.Contains("SupportsToolCalling is false", error.Message);
    }

    /// <summary>When tools are supplied, the prompt contains the tool definitions and the tool-call instruction.</summary>
    [Fact]
    public async Task ToolDefinitionsReachThePrompt()
    {
        using var provider = FakeProviderFactory.Create(runtime, supportsToolCalling: true);
        var options = new LlmCompletionOptions
        {
            SystemPrompt = "Be helpful.",
            Tools = [new ToolDefinition
            {
                Name = "get_weather",
                Description = "Gets the weather for a city.",
                ParametersSchema = """{"type":"object","properties":{"city":{"type":"string"}}}"""
            }]
        };

        await provider.ChatAsync([ChatMessage.User("What's the weather?")], options);

        var prompt = runtime.Prompts.Single();
        Assert.Contains("<tools>", prompt);
        Assert.Contains("get_weather", prompt);
        Assert.Contains("Be helpful.", prompt);
        Assert.Contains("<tool_call>", prompt);
    }

    /// <summary>A Qwen-style tool-call block is parsed into response tool calls with generated ids.</summary>
    [Fact]
    public async Task QwenToolCallBlockIsParsed()
    {
        runtime.Script = (_, _) =>
        [
            "<tool_call>\n",
            """{"name":"get_weather","arguments":{"city":"Paris"}}""",
            "\n</tool_call>"
        ];
        using var provider = FakeProviderFactory.Create(runtime, supportsToolCalling: true);
        var options = new LlmCompletionOptions
        {
            Tools = [new ToolDefinition
            {
                Name = "get_weather",
                Description = "Gets the weather for a city.",
                ParametersSchema = """{"type":"object","properties":{"city":{"type":"string"}}}"""
            }]
        };

        var response = await provider.ChatAsync([ChatMessage.User("Weather in Paris?")], options);

        Assert.True(response.HasToolCalls);
        Assert.Equal("tool_calls", response.FinishReason);
        Assert.Null(response.Content);
        var call = Assert.Single(response.ToolCalls);
        Assert.Equal("get_weather", call.Name);
        Assert.Contains("Paris", call.ArgumentsJson);
        Assert.False(string.IsNullOrEmpty(call.Id));
    }

    /// <summary>Multiple Qwen-style tool-call blocks in one answer become multiple tool calls.</summary>
    [Fact]
    public async Task MultipleQwenToolCallsAreParsed()
    {
        runtime.Script = (_, _) =>
        [
            "<tool_call>{\"name\":\"a\",\"arguments\":{}}</tool_call><tool_call>{\"name\":\"b\",\"arguments\":{}}</tool_call>"
        ];
        using var provider = FakeProviderFactory.Create(runtime, supportsToolCalling: true);
        var options = new LlmCompletionOptions
        {
            Tools =
            [
                new ToolDefinition { Name = "a", Description = "A.", ParametersSchema = "{}" },
                new ToolDefinition { Name = "b", Description = "B.", ParametersSchema = "{}" }
            ]
        };

        var response = await provider.ChatAsync([ChatMessage.User("Call both.")], options);

        Assert.Equal(2, response.ToolCalls!.Count);
        Assert.Equal("a", response.ToolCalls[0].Name);
        Assert.Equal("b", response.ToolCalls[1].Name);
    }

    /// <summary>A plain text answer when tools are present is not misidentified as a tool call.</summary>
    [Fact]
    public async Task PlainTextAnswerWithToolsIsNotParsedAsToolCall()
    {
        runtime.Script = (_, _) => ["I don't need a tool."];
        using var provider = FakeProviderFactory.Create(runtime, supportsToolCalling: true);
        var options = new LlmCompletionOptions
        {
            Tools = [new ToolDefinition { Name = "lookup", Description = "Looks things up.", ParametersSchema = "{}" }]
        };

        var response = await provider.ChatAsync([ChatMessage.User("Hi")], options);

        Assert.False(response.HasToolCalls);
        Assert.Equal("I don't need a tool.", response.Content);
        Assert.Equal("stop", response.FinishReason);
    }

    /// <summary>A tool result message is wrapped in the model's tool-response format for the next turn.</summary>
    [Fact]
    public async Task ToolResultIsFormattedForNextTurn()
    {
        runtime.Script = (_, _) => ["Done."];
        using var provider = FakeProviderFactory.Create(runtime, supportsToolCalling: true);

        await provider.ChatAsync(
        [
            ChatMessage.User("Call a tool."),
            new ChatMessage { Role = "tool", ToolCallId = "c1", Name = "get_weather", Content = "Sunny, 22 C." }
        ]);

        var prompt = runtime.Prompts.Single();
        Assert.Contains("<tool_response>", prompt);
        Assert.Contains("get_weather", prompt);
        Assert.Contains("Sunny, 22 C.", prompt);
    }

    /// <summary>A tool-call turn streamed from a supported model yields tool-call events, not text deltas.</summary>
    [Fact]
    public async Task StreamedToolCallYieldsToolCallEvents()
    {
        runtime.Script = (_, _) =>
        [
            "<tool_call>{\"name\":\"get_weather\",\"arguments\":{\"city\":\"Paris\"}}</tool_call>"
        ];
        using var provider = FakeProviderFactory.Create(runtime, supportsToolCalling: true);
        var options = new LlmCompletionOptions
        {
            Tools = [new ToolDefinition
            {
                Name = "get_weather",
                Description = "Gets the weather for a city.",
                ParametersSchema = """{"type":"object","properties":{"city":{"type":"string"}}}"""
            }]
        };

        var events = new List<LlmStreamEvent>();
        await foreach (var streamEvent in provider.ChatStreamEventsAsync([ChatMessage.User("Weather?")], options))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, e => e.Kind == LlmStreamEventKind.ToolCall);
        Assert.DoesNotContain(events, e => e.Kind == LlmStreamEventKind.TextDelta);
        Assert.Equal("tool_calls", events[^1].FinishReason);
    }

    /// <summary>The agent loop runs a tool through the local provider and receives the final answer.</summary>
    [Fact]
    public async Task AgentLoopRunsToolCallAndReturnsFinalAnswer()
    {
        var registry = new ToolRegistry();
        registry.Register("get_weather", "Gets the weather for a city.",
            """{"type":"object","properties":{"city":{"type":"string"}}}""",
            arguments => "Sunny, 22 C in Paris.");

        runtime.Script = (prompt, _) =>
            prompt.Contains("<tool_response>", StringComparison.Ordinal)
                ? ["The weather in Paris is sunny and 22 C."]
                : ["<tool_call>{\"name\":\"get_weather\",\"arguments\":{\"city\":\"Paris\"}}</tool_call>"];

        using var provider = FakeProviderFactory.Create(runtime, supportsToolCalling: true);
        var runner = new AgentLoopRunner(provider, registry);

        var response = await runner.RunAsync([ChatMessage.User("What's the weather in Paris?")]);

        Assert.False(response.HasToolCalls);
        Assert.Contains("22 C", response.Content);
    }
}
