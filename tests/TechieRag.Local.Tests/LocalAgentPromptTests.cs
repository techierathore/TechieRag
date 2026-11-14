using TechieRag.Agentic.HostData;
using TechieRag.Local.Tests.TestDoubles;
using TechieRag.Models;
using TechieRag.Services;
using TechieRag.Tests.Agentic;
using Xunit;
using Xunit.Abstractions;

namespace TechieRag.Local.Tests;

/// <summary>
/// Sevak's two local-model agent reports, run through <see cref="LocalLlmProvider"/> over the scripted runtime:
/// the host-data tools fit a 4,096-token model (REQ-RAG-160, TR-RAG-061) and a tool request the model writes as text
/// is run by the agent loop (REQ-RAG-159, TR-RAG-062).
/// </summary>
/// <remarks>
/// The scripted runtime counts words and punctuation as tokens, close to a real tokenizer for this English text.
/// The model is a folder model with Qwen 2.5's ChatML template and Sevak's 4,096-token context; the Qwen weights
/// themselves are not on this machine.
/// </remarks>
public sealed class LocalAgentPromptTests(ITestOutputHelper output)
{
    /// <summary>
    /// Sevak's repro: fifteen entities on a <see cref="HostDataSkill"/>, one agent call to a 4,096-token ChatML model.
    /// The first call used to throw <see cref="LocalPromptTooLongException"/> (7,805 tokens); now it fits and answers.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-160 FifteenEntitiesFitAFourThousandTokenModel")]
    public async Task FifteenEntitiesFitAFourThousandTokenModel()
    {
        var runtime = new FakeLocalLlmRuntime();
        using var provider = FakeProviderFactory.Create(runtime, supportsToolCalling: true);
        var skill = new HostDataSkill();
        foreach (var entity in SevakEntities.All())
        {
            skill.Register(entity);
        }

        var response = await new AgentLoopRunner(provider, skill).RunAsync([ChatMessage.User("what is on my plan today?")]);

        Assert.Equal("Hello there.", response.Content);
        var promptTokens = FakeLocalModel.Count(Assert.Single(runtime.Prompts));
        output.WriteLine($"fifteen-entity agent prompt: {promptTokens} tokens of a 4,096-token context");
        Assert.True(promptTokens < 4_096, $"the prompt is {promptTokens} tokens");
    }

    /// <summary>
    /// A Phi-3 model writes its tool request in <c>&lt;tool_call&gt;</c> tags, which its own template does not use;
    /// the agent loop runs it and the final answer is the model's second reply.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-159 LocalModelWrittenRequestIsRun")]
    public async Task LocalModelWrittenRequestIsRun()
    {
        var runtime = new FakeLocalLlmRuntime
        {
            Script = (prompt, _) => prompt.Contains("Leave: 20 days", StringComparison.Ordinal)
                ? ["Twenty", " days", "."]
                : ["<tool_call>", "{\"name\": \"rag-search\", \"arguments\": {\"query\": \"leave policy\"}}", "</tool_call>"]
        };
        using var provider = FakeProviderFactory.Create(runtime, options => options.Model = LocalModel.FromFolder(
            Path.Combine(Path.GetTempPath(), "techierag-local-tests", "fake-model"), LocalChatTemplate.Phi3, 4_096, 131_072, true));
        var registry = new ToolRegistry();
        var queries = new List<string>();
        registry.Register("rag-search", "Searches the user's documents.",
            """{"type":"object","properties":{"query":{"type":"string"}}}""",
            arguments =>
            {
                queries.Add(arguments);
                return "Leave: 20 days";
            });

        var response = await new AgentLoopRunner(provider, registry).RunAsync([ChatMessage.User("How many leave days do I get?")]);

        Assert.Equal("Twenty days.", response.Content);
        Assert.Single(queries);
    }
}
