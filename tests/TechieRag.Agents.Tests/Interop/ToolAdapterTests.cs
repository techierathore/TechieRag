using Microsoft.Extensions.AI;
using TechieRag.Abstractions;
using TechieRag.Agents.Interop;
using TechieRag.Agents.Tests.TestDoubles;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;
using static TechieRag.Agents.Tests.TestDoubles.ScriptedLlmProvider;

namespace TechieRag.Agents.Tests.Interop;

/// <summary>
/// Tests for adapters 2a and 2b: <see cref="ToolHandlerFunctions"/> (IToolHandler → AITool) and
/// <see cref="AIToolHandler"/> (AITool / AIAgent → IToolHandler) (REQ-RAG-017 / BRD-85).
/// </summary>
public class ToolAdapterTests
{
    /// <summary>
    /// Acceptance for adapter 2a: a <see cref="ToolRegistry"/> handed to the builder is called by the
    /// Agent Framework loop unchanged — the handler receives the model's arguments and its result is what
    /// the model reads next.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-017 AgentExecutesToolHandlerUnchanged")]
    public async Task AgentExecutesToolHandlerUnchanged()
    {
        var received = new List<string>();
        var registry = new ToolRegistry();
        registry.Register("get_weather", "Gets the weather.", """{"type":"object","properties":{"city":{"type":"string"}}}""", args =>
        {
            received.Add(args);
            return "22 C";
        });
        var model = new ScriptedChatClient(
            ScriptedChatClient.ToolCall("c1", "get_weather", new() { ["city"] = "Paris" }),
            ScriptedChatClient.Answer("It is 22 C."));
        var agent = new TechieRagAgentBuilder(AgentTestRag.Create()).UseCustomChatClient(() => model).WithToolHandler(registry).Build();

        var response = await agent.AskAsync("Weather in Paris?");

        Assert.Equal("""{"city":"Paris"}""", Assert.Single(received));
        var result = model.Calls[1].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        Assert.Equal("22 C", result.Result);
        Assert.Equal("It is 22 C.", response.Answer);
    }

    /// <summary>A tool that requires confirmation is wrapped as approval-required; others are plain functions with the raw schema.</summary>
    [Fact]
    public void ConfirmationToolsBecomeApprovalRequired()
    {
        var handler = new FixedHandler(
            new ToolDefinition { Name = "send_mail", Description = "Sends mail.", ParametersSchema = """{"type":"object","properties":{}}""", RequiresConfirmation = true },
            new ToolDefinition { Name = "read_file", Description = "Reads.", ParametersSchema = """{"type":"object","properties":{"path":{"type":"string"}}}""" });

        var tools = ToolHandlerFunctions.From(handler);

        Assert.IsType<ApprovalRequiredAIFunction>(tools[0]);
        var plain = Assert.IsType<ToolHandlerAIFunction>(tools[1]);
        Assert.Equal("string", plain.JsonSchema.GetProperty("properties").GetProperty("path").GetProperty("type").GetString());
    }

    /// <summary>A malformed schema fails when the tools are adapted, not when the model first calls them.</summary>
    [Fact]
    public void MalformedSchemaFailsAtAdaptation()
    {
        var handler = new FixedHandler(new ToolDefinition { Name = "bad", Description = "Bad.", ParametersSchema = "{not json" });

        var ex = Assert.Throws<ArgumentException>(() => ToolHandlerFunctions.From(handler));

        Assert.Contains("bad", ex.Message);
    }

    /// <summary>
    /// Adapter 2b: an MEAI function becomes a tool of the classic <see cref="AgentLoopRunner"/> — offered
    /// with its schema, executed with the model's arguments.
    /// </summary>
    [Fact]
    public async Task FunctionRunsInClassicLoop()
    {
        var weather = AIFunctionFactory.Create((string city) => $"22 C in {city}", "get_weather", "Gets the weather.");
        var provider = new ScriptedLlmProvider(
            [Call("c1", "get_weather", """{"city":"Paris"}"""), Done("tool_calls")],
            [Text("Warm."), Done()]);

        await new AgentLoopRunner(provider, new AIToolHandler([weather])).RunAsync([Models.ChatMessage.User("Weather?")]);

        Assert.Equal("get_weather", Assert.Single(provider.Options[0]!.Tools!).Name);
        Assert.Equal("22 C in Paris", provider.Calls[1][^1].Content);
    }

    /// <summary>Adapter 2b over a whole agent: a MAF agent is one tool of the classic loop and its answer is the tool result.</summary>
    [Fact]
    public async Task AgentBecomesToolOfClassicLoop()
    {
        var innerModel = new ScriptedChatClient(ScriptedChatClient.Answer("Inner answer."));
        var inner = new TechieRagAgentBuilder(AgentTestRag.Create()).UseCustomChatClient(() => innerModel).WithName("researcher").Build();
        var handler = AIToolHandler.FromAgent(inner.Agent);

        var result = await handler.ExecuteToolAsync(new ToolCall { Id = "c1", Name = handler.ToolDefinitions[0].Name, ArgumentsJson = """{"query":"anything"}""" });

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains("Inner answer.", result.Content);
    }

    /// <summary>An unknown tool name is reported to the model, not thrown.</summary>
    [Fact]
    public async Task UnknownToolIsReported()
    {
        var result = await new AIToolHandler([]).ExecuteToolAsync(new ToolCall { Id = "c1", Name = "nope", ArgumentsJson = "{}" });

        Assert.False(result.IsSuccess);
    }

    /// <summary>A handler with fixed definitions that returns a canned result.</summary>
    private sealed class FixedHandler(params ToolDefinition[] definitions) : IToolHandler
    {
        public IReadOnlyList<ToolDefinition> ToolDefinitions => definitions;

        public Task<ToolResult> ExecuteToolAsync(ToolCall toolCall, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ToolResult { ToolCallId = toolCall.Id, Content = "ok" });
    }
}
