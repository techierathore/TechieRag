using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using TechieRag.Models;
using TechieRag.Orchestration;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// Proves the library never asks a consumer to show a user English it cannot translate
/// (REQ-RAG-050 / BRD-91).
/// </summary>
/// <remarks>
/// <para><b>What "user-visible" means here, and why it is not "every string".</b> A host paints two
/// things: the detail line of a trace row — <c>FlowStep.Content</c> / <c>FlowStep.ErrorMessage</c> —
/// and the alert that says why a run stopped — <c>FlowRunResult.BlockReason</c> /
/// <c>FailureReason</c>. Those are what these tests cover. The <c>unavailable: …</c> content of a
/// blocked tool result is read by the MODEL and is deliberately left in English, which
/// <see cref="AgentToolHandler"/> documents; asserting a code on it would be asserting the wrong
/// design.</para>
/// <para><b>Every assertion is on a real run.</b> The codes are read off the steps a
/// <see cref="FlowRunner"/> actually emitted, not off a factory method called directly, because the
/// claim being tested is that the code REACHES the surface a host renders — a code that exists but
/// never gets stamped is exactly the defect this REQ was raised over, one layer up.</para>
/// </remarks>
public sealed class FlowMessageLocalizationTests
{
    /// <summary>
    /// The headline case. A host guardrail refuses a tool call an agent asked for; the trace row a
    /// user reads carries a stable code plus the guardrail id and the tool name, so the sentence can
    /// be written in Hindi with the two invariant values dropped in wherever the grammar wants them.
    /// </summary>
    [Fact]
    public async Task ARefusedToolCallGivesTheTraceRowACodeAndItsArguments()
    {
        var provider = new ScriptedLlmProvider(
            "agent",
            ScriptedLlmProvider.CallsTool("web-search", """{"query":"anything"}"""),
            ScriptedLlmProvider.Says("I could not search."));

        var tools = new RecordingToolHandler().Register("web-search", "Searches the web", _ => "REMOTE RESULT");
        var runtime = AgentRuntime(provider, tools);
        runtime.HostGuardrails.Add(BlockingTool("egress-gate", "declined"));

        var result = await new FlowRunner(AgentFlow(), runtime).RunAsync("what is the weather");

        Assert.Empty(tools.Executed);

        var blocked = Assert.Single(result.Steps, step => step.Kind == AgentStepKind.GuardrailBlocked);
        var message = Assert.IsType<FlowMessage>(blocked.FailureMessage);

        Assert.Equal(FlowMessageCodes.ToolCallBlockedByGuardrail, message.Code);
        Assert.Equal(["egress-gate", "web-search"], message.Arguments);

        // The English is still there, unchanged, for a consumer that has no translation yet.
        Assert.Equal("Blocked by guardrail 'egress-gate' before 'web-search' ran.", blocked.ErrorMessage);
        Assert.Equal(blocked.ErrorMessage, message.Text);
    }

    /// <summary>
    /// The refusal's REASON travels separately from the framing around it, because the reason
    /// belongs to whichever guardrail refused and the framing belongs to the library. A host that
    /// blocks with a <see cref="FlowMessage"/> gets its own code all the way onto the trace row and
    /// onto the run result the alert is drawn from.
    /// </summary>
    [Fact]
    public async Task AHostsOwnBlockCodeSurvivesOntoTheTraceRowAndTheRunResult()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("the answer"));
        var runtime = AgentRuntime(provider, tools: null);

        runtime.HostGuardrails.Add(new DelegateFlowGuardrail(
            "egress-gate", "The host's own gate", [GuardrailStage.Output],
            (_, _) => Task.FromResult(GuardrailDecision.Block(FlowMessage.Create(
                "HostEgressDeclined", "Sending '{0}' off this machine was declined.", "weather-api")))));

        var result = await new FlowRunner(AgentFlow(), runtime).RunAsync("hello");

        Assert.Equal(FlowRunOutcome.Blocked, result.Outcome);

        var block = Assert.IsType<FlowMessage>(result.BlockMessage);
        Assert.Equal("HostEgressDeclined", block.Code);
        Assert.Equal(["weather-api"], block.Arguments);
        Assert.Equal(result.BlockReason, block.Text);

        // And on the row, as the reason under the "Blocked by …" title.
        var blocked = Assert.Single(result.Steps, step => step.Kind == AgentStepKind.GuardrailBlocked);
        Assert.Equal("HostEgressDeclined", blocked.ContentMessage?.Code);
        Assert.Equal(FlowMessageCodes.NodeBlockedByGuardrail, blocked.FailureMessage?.Code);
    }

    /// <summary>
    /// A deterministic tool node's refusal is coded too. This is the path a flow author reaches by
    /// dropping a Tool node on the canvas rather than asking an agent, and it renders through the
    /// tool row rather than the guardrail row, so it needs its own carrier.
    /// </summary>
    [Fact]
    public async Task ARefusedToolNodeCodesTheToolRowAsWellAsTheGuardrailRow()
    {
        var tools = new RecordingToolHandler().Register("web-search", "Searches the web", _ => "REMOTE RESULT");

        var flow = new FlowDefinition
        {
            Id = "direct",
            Name = "Direct tool call",
            StartNodeId = "call",
            Nodes =
            [
                new FlowNode { Id = "call", Kind = FlowNodeKind.Tool, ToolName = "web-search" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
            ],
            Edges = [new FlowEdge { Id = "e", FromNodeId = "call", ToNodeId = "end" }]
        };

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver()) { Tools = tools };
        runtime.HostGuardrails.Add(BlockingTool("egress-gate", "approval was declined"));

        var result = await new FlowRunner(flow, runtime).RunAsync("go");

        Assert.Empty(tools.Executed);

        var toolRow = Assert.Single(result.Steps, step => step.Kind == AgentStepKind.ToolExecuted);
        var message = Assert.IsType<FlowMessage>(toolRow.FailureMessage);

        Assert.Equal(FlowMessageCodes.ToolCallRefusedByGuardrail, message.Code);
        Assert.Equal(["egress-gate", "approval was declined"], message.Arguments);
        Assert.Equal(toolRow.ErrorMessage, message.Text);
    }

    /// <summary>
    /// A guardrail that throws denies, and says so in a sentence naming the exception type and its
    /// message — which a Hindi user was previously shown in English.
    /// </summary>
    [Fact]
    public async Task AFaultedGuardrailCodesTheDenialWithTheExceptionTypeAndMessage()
    {
        var result = await RunWithFlowGuardrail(new DelegateFlowGuardrail(
            "faulty", "Throws on every call", [GuardrailStage.Input],
            (_, _) => throw new InvalidOperationException("the policy service is down")));

        var block = Assert.IsType<FlowMessage>(result.BlockMessage);
        Assert.Equal(FlowMessageCodes.GuardrailFaulted, block.Code);
        Assert.Equal(["InvalidOperationException", "the policy service is down"], block.Arguments);
        Assert.Equal(result.BlockReason, block.Text);
    }

    /// <summary>A guardrail id the host cannot produce denies under a code naming the id.</summary>
    [Fact]
    public async Task AnUnresolvableGuardrailCodesTheDenialWithTheIdItCouldNotLoad()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("unreachable"));
        var runtime = AgentRuntime(provider, tools: null);
        runtime.Guardrails = new InMemoryFlowGuardrailResolver(
            new DelegateFlowGuardrail("some-other-check", "Unrelated", [GuardrailStage.Input],
                (_, _) => Task.FromResult(GuardrailDecision.Allow())));

        var result = await new FlowRunner(AgentFlow(["compliance-check"]), runtime).RunAsync("hello");

        var block = Assert.IsType<FlowMessage>(result.BlockMessage);
        Assert.Equal(FlowMessageCodes.GuardrailUnresolvable, block.Code);
        Assert.Equal(["compliance-check"], block.Arguments);
        Assert.Equal(0, provider.TurnCount);
    }

    /// <summary>
    /// A node naming a guardrail on a host with no resolver at all is a DIFFERENT failure from an id
    /// that missed, and gets its own code — the two need different wording and different fixes.
    /// </summary>
    [Fact]
    public async Task AMissingResolverCodesDifferentlyFromAMissingGuardrail()
    {
        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("unreachable"));
        var runtime = AgentRuntime(provider, tools: null);

        var result = await new FlowRunner(AgentFlow(["compliance-check"]), runtime).RunAsync("hello");

        var block = Assert.IsType<FlowMessage>(result.BlockMessage);
        Assert.Equal(FlowMessageCodes.GuardrailResolverMissing, block.Code);
        Assert.NotEqual(FlowMessageCodes.GuardrailUnresolvable, block.Code);
        Assert.Equal(["compliance-check"], block.Arguments);
    }

    /// <summary>
    /// A flow refused by the validator carries a code, and — better — every underlying issue keeps
    /// its own <see cref="FlowValidationCodes"/> value, so a consumer can translate each one rather
    /// than the joined English argument.
    /// </summary>
    [Fact]
    public async Task AFlowRefusedByValidationCodesTheFailureAndKeepsEveryIssueCode()
    {
        var flow = new FlowDefinition
        {
            Id = "broken",
            Name = "Broken flow",
            StartNodeId = "work",
            Nodes = [new FlowNode { Id = "work", Kind = FlowNodeKind.Agent }],
            Edges = []
        };

        var result = await new FlowRunner(flow, new FlowRuntime(new InMemoryFlowAgentResolver())).RunAsync("hello");

        Assert.Equal(FlowRunOutcome.Failed, result.Outcome);

        var failure = Assert.IsType<FlowMessage>(result.FailureMessage);
        Assert.Equal(FlowMessageCodes.FlowNotValidated, failure.Code);
        Assert.Equal(result.FailureReason, failure.Text);

        Assert.Contains(result.ValidationIssues, issue => issue.Code == FlowValidationCodes.MissingAgentId);
        Assert.All(result.ValidationIssues, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Code)));
    }

    /// <summary>
    /// An exhausted step budget codes both halves of what a user is shown: the row's content names
    /// the budget and the node it stopped before, the row's detail names the budget alone.
    /// </summary>
    [Fact]
    public async Task AnExhaustedStepBudgetCodesTheBudgetAndTheNodeItStoppedBefore()
    {
        var provider = new ScriptedLlmProvider(
            "agent", ScriptedLlmProvider.Says("one"), ScriptedLlmProvider.Says("two"));

        var flow = new FlowDefinition
        {
            Id = "looping",
            Name = "Looping flow",
            StartNodeId = "work",
            AllowCycles = true,
            MaxSteps = 1,
            Nodes = [new FlowNode { Id = "work", Kind = FlowNodeKind.Agent, Name = "Work", AgentId = "agent" }],
            Edges = [new FlowEdge { Id = "loop", FromNodeId = "work", ToNodeId = "work" }]
        };

        var result = await new FlowRunner(flow, AgentRuntime(provider, tools: null)).RunAsync("hello");

        Assert.Equal(FlowRunOutcome.StepBudgetExhausted, result.Outcome);

        var row = Assert.Single(result.Steps, step => step.Kind == AgentStepKind.StepBudgetExhausted);

        Assert.Equal(FlowMessageCodes.StepBudgetExhausted, row.ContentMessage?.Code);
        Assert.Equal(["1", "Work"], row.ContentMessage?.Arguments);

        Assert.Equal(FlowMessageCodes.StepBudgetReached, row.FailureMessage?.Code);
        Assert.Equal(["1"], row.FailureMessage?.Arguments);
    }

    /// <summary>An agent the host cannot produce is reported by id, not by an English sentence.</summary>
    [Fact]
    public async Task AnUnresolvableAgentCodesBothTheOutputAndTheFailure()
    {
        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver());

        var result = await new FlowRunner(AgentFlow(), runtime).RunAsync("hello");

        // The terminal node completes too, so the row of interest is the agent node's.
        var completed = Assert.Single(
            result.Steps, step => step.Kind == AgentStepKind.NodeCompleted && step.NodeId == "work");

        Assert.Equal(FlowMessageCodes.AgentUnavailable, completed.ContentMessage?.Code);
        Assert.Equal(["agent"], completed.ContentMessage?.Arguments);

        Assert.Equal(FlowMessageCodes.AgentUnresolvable, completed.FailureMessage?.Code);
        Assert.Equal(["agent"], completed.FailureMessage?.Arguments);
    }

    /// <summary>
    /// A route the runner had to name itself is coded; a route the AUTHOR labelled is not. Their
    /// label is their words in their language and this library has no business claiming it.
    /// </summary>
    [Fact]
    public async Task OnlyAGeneratedEdgeLabelCarriesALibraryCode()
    {
        var generated = await RunTwoNodeFlow(edgeLabel: null);
        var authored = await RunTwoNodeFlow(edgeLabel: "आगे बढ़ें");

        var generatedRoute = Assert.Single(generated.Steps, step => step.Kind == AgentStepKind.RouteTaken);
        Assert.Equal(FlowMessageCodes.RouteToNode, generatedRoute.ContentMessage?.Code);
        Assert.Equal(["Work", "Done"], generatedRoute.ContentMessage?.Arguments);

        var authoredRoute = Assert.Single(authored.Steps, step => step.Kind == AgentStepKind.RouteTaken);
        Assert.Null(authoredRoute.ContentMessage);
        Assert.Equal("आगे बढ़ें", authoredRoute.Content);
    }

    /// <summary>
    /// Every message a real run produced renders its own arguments into its own English, and names
    /// no more placeholders than it has arguments. A code whose sentence needs a value it was never
    /// given cannot be translated at all — the translator would have nothing to put there.
    /// </summary>
    [Fact]
    public async Task EveryMessageARunEmitsHasAnArgumentForEveryPlaceholder()
    {
        var messages = (await EveryScenarioAsync()).ToList();
        Assert.NotEmpty(messages);

        foreach (var message in messages)
        {
            Assert.False(string.IsNullOrWhiteSpace(message.Code));
            Assert.False(string.IsNullOrWhiteSpace(message.Format));

            var highest = Regex.Matches(message.Format, @"\{(\d+)\}")
                .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
                .DefaultIfEmpty(-1)
                .Max();

            Assert.True(
                highest < message.Arguments.Count,
                $"'{message.Code}' names {{{highest}}} but was given {message.Arguments.Count} argument(s).");

            Assert.Equal(
                message.Arguments.Count == 0
                    ? message.Format
                    : string.Format(CultureInfo.InvariantCulture, message.Format, [.. message.Arguments]),
                message.Text);
        }
    }

    /// <summary>
    /// Every code the library itself emits is declared on <see cref="FlowMessageCodes"/>. A code
    /// invented at a call site is one a consumer can never discover, so it would ship as English
    /// forever — which is the defect, wearing a code's clothes.
    /// </summary>
    [Fact]
    public async Task EveryCodeTheLibraryEmitsIsDeclaredOnFlowMessageCodes()
    {
        var declared = typeof(FlowMessageCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);

        // "HostEgressDeclined" is minted by a TEST host to prove a host may use its own codes, so it
        // is excluded rather than added — a host's codes are not this library's to declare.
        foreach (var message in await EveryScenarioAsync())
        {
            if (message.Code == "HostEgressDeclined") continue;

            Assert.Contains(message.Code, declared);
        }
    }

    /// <summary>
    /// Every declared code names itself, so a consumer's resource key can be derived from the
    /// constant rather than from a separately maintained list that can drift out of step with it.
    /// </summary>
    [Fact]
    public void EveryDeclaredCodeIsItsOwnName()
    {
        foreach (var field in typeof(FlowMessageCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            Assert.Equal(field.Name, (string)field.GetRawConstantValue()!);
        }
    }

    /// <summary>
    /// The English every existing consumer already reads is byte-identical to what it was. The codes
    /// are additional; a NuGet consumer that renders <c>ErrorMessage</c> and knows nothing about
    /// <see cref="FlowMessage"/> sees no change at all.
    /// </summary>
    [Fact]
    public async Task TheEnglishEveryExistingConsumerReadsIsUnchanged()
    {
        var provider = new ScriptedLlmProvider(
            "agent",
            ScriptedLlmProvider.CallsTool("web-search"),
            ScriptedLlmProvider.Says("done"));

        var tools = new RecordingToolHandler().Register("web-search", "Searches the web", _ => "REMOTE");
        var runtime = AgentRuntime(provider, tools);
        runtime.HostGuardrails.Add(BlockingTool("egress-gate", "approval was declined"));

        var result = await new FlowRunner(AgentFlow(), runtime).RunAsync("hello");

        var blocked = Assert.Single(result.Steps, step => step.Kind == AgentStepKind.GuardrailBlocked);
        Assert.Equal("Blocked by guardrail 'egress-gate' before 'web-search' ran.", blocked.ErrorMessage);
        Assert.Equal("approval was declined", blocked.Content);

        var toolRow = Assert.Single(
            result.Steps, step => step.Kind == AgentStepKind.ToolExecuted && step.ToolName == "web-search");
        Assert.Equal("unavailable: 'web-search' was not run. approval was declined", toolRow.Content);
        Assert.Equal("Blocked by guardrail 'egress-gate': approval was declined", toolRow.ErrorMessage);
    }

    /// <summary>
    /// A refusal that named no reason at all still produces a code rather than a bare English
    /// fallback, so even the degenerate case is translatable.
    /// </summary>
    [Fact]
    public void ARefusalWithNoReasonStillCarriesACode()
    {
        var verdict = new GuardrailVerdict(false, GuardrailStage.ToolCall, "egress-gate", null);
        var message = GuardedToolHandler.RefusalMessage(verdict);

        Assert.Equal(FlowMessageCodes.ToolCallRefusedByGuardrail, message.Code);
        Assert.Equal(["egress-gate", "The call was refused by a guardrail."], message.Arguments);
    }

    /// <summary>Collects every message a representative set of runs produced.</summary>
    /// <returns>Each message emitted onto a step or a run result, deduplicated by nothing.</returns>
    private static async Task<IReadOnlyList<FlowMessage>> EveryScenarioAsync()
    {
        var results = new List<FlowRunResult>();

        // Refused tool call, inside an agent node.
        var caller = new ScriptedLlmProvider(
            "agent", ScriptedLlmProvider.CallsTool("web-search"), ScriptedLlmProvider.Says("done"));
        var toolRuntime = AgentRuntime(caller, new RecordingToolHandler().Register("web-search", "Searches", _ => "x"));
        toolRuntime.HostGuardrails.Add(BlockingTool("egress-gate", "approval was declined"));
        results.Add(await new FlowRunner(AgentFlow(), toolRuntime).RunAsync("hello"));

        // Node output refused by a host code, and by a faulted guardrail.
        var blocking = AgentRuntime(new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("answer")), null);
        blocking.HostGuardrails.Add(new DelegateFlowGuardrail(
            "egress-gate", "The host's gate", [GuardrailStage.Output],
            (_, _) => Task.FromResult(GuardrailDecision.Block(FlowMessage.Create(
                "HostEgressDeclined", "Sending '{0}' off this machine was declined.", "weather-api")))));
        results.Add(await new FlowRunner(AgentFlow(), blocking).RunAsync("hello"));

        results.Add(await RunWithFlowGuardrail(new DelegateFlowGuardrail(
            "faulty", "Throws", [GuardrailStage.Input],
            (_, _) => throw new InvalidOperationException("down"))));

        // Unresolvable guardrail, and no resolver at all.
        var missing = AgentRuntime(new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("x")), null);
        missing.Guardrails = new InMemoryFlowGuardrailResolver();
        results.Add(await new FlowRunner(AgentFlow(["compliance-check"]), missing).RunAsync("hello"));
        results.Add(await new FlowRunner(
            AgentFlow(["compliance-check"]),
            AgentRuntime(new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("x")), null)).RunAsync("hello"));

        // Unresolvable agent, generated route, and a two-node completion.
        results.Add(await new FlowRunner(AgentFlow(), new FlowRuntime(new InMemoryFlowAgentResolver())).RunAsync("hello"));
        results.Add(await RunTwoNodeFlow(edgeLabel: null));

        // Refused deterministic tool node, and a runtime with no tool handler at all.
        var toolNodeFlow = new FlowDefinition
        {
            Id = "direct",
            Name = "Direct",
            StartNodeId = "call",
            Nodes =
            [
                new FlowNode { Id = "call", Kind = FlowNodeKind.Tool, ToolName = "web-search" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
            ],
            Edges = [new FlowEdge { Id = "e", FromNodeId = "call", ToNodeId = "end" }]
        };

        var refusedNode = new FlowRuntime(new InMemoryFlowAgentResolver())
        {
            Tools = new RecordingToolHandler().Register("web-search", "Searches", _ => "x")
        };
        refusedNode.HostGuardrails.Add(BlockingTool("egress-gate", "approval was declined"));
        results.Add(await new FlowRunner(toolNodeFlow, refusedNode).RunAsync("go"));

        results.Add(await new FlowRunner(
            toolNodeFlow, new FlowRuntime(new InMemoryFlowAgentResolver())).RunAsync("go"));

        // A flow the validator refuses outright.
        results.Add(await new FlowRunner(
            new FlowDefinition
            {
                Id = "broken",
                Name = "Broken",
                StartNodeId = "work",
                Nodes = [new FlowNode { Id = "work", Kind = FlowNodeKind.Agent }],
                Edges = []
            },
            new FlowRuntime(new InMemoryFlowAgentResolver())).RunAsync("hello"));

        // An exhausted step budget.
        results.Add(await new FlowRunner(
            new FlowDefinition
            {
                Id = "looping",
                Name = "Looping",
                StartNodeId = "work",
                AllowCycles = true,
                MaxSteps = 1,
                Nodes = [new FlowNode { Id = "work", Kind = FlowNodeKind.Agent, Name = "Work", AgentId = "agent" }],
                Edges = [new FlowEdge { Id = "loop", FromNodeId = "work", ToNodeId = "work" }]
            },
            AgentRuntime(new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("one")), null)).RunAsync("hello"));

        return
        [
            .. results.SelectMany(result => result.Steps)
                .SelectMany(step => new[] { step.ContentMessage, step.FailureMessage })
                .Concat(results.SelectMany(result => new[] { result.BlockMessage, result.FailureMessage }))
                .OfType<FlowMessage>()
        ];
    }

    /// <summary>Runs the shared one-agent flow with one guardrail the node names.</summary>
    /// <param name="guardrail">The guardrail to resolve.</param>
    /// <returns>The run result.</returns>
    private static Task<FlowRunResult> RunWithFlowGuardrail(IFlowGuardrail guardrail)
    {
        var runtime = AgentRuntime(new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("unreachable")), null);
        runtime.Guardrails = new InMemoryFlowGuardrailResolver(guardrail);

        return new FlowRunner(AgentFlow([guardrail.Id]), runtime).RunAsync("hello");
    }

    /// <summary>Runs a two-node flow so one edge is actually followed.</summary>
    /// <param name="edgeLabel">The author's label for the edge, or null to let the runner name it.</param>
    /// <returns>The run result.</returns>
    private static Task<FlowRunResult> RunTwoNodeFlow(string? edgeLabel)
    {
        var flow = new FlowDefinition
        {
            Id = "two",
            Name = "Two nodes",
            StartNodeId = "work",
            Nodes =
            [
                new FlowNode { Id = "work", Kind = FlowNodeKind.Agent, Name = "Work", AgentId = "agent" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal, Name = "Done" }
            ],
            Edges = [new FlowEdge { Id = "e", FromNodeId = "work", ToNodeId = "end", Label = edgeLabel }]
        };

        var provider = new ScriptedLlmProvider("agent", ScriptedLlmProvider.Says("the answer"));
        return new FlowRunner(flow, AgentRuntime(provider, tools: null)).RunAsync("hello");
    }

    /// <summary>Builds a runtime whose single agent runs the given scripted model.</summary>
    /// <param name="provider">The scripted model.</param>
    /// <param name="tools">The agent's tools, or null for none.</param>
    /// <returns>The runtime.</returns>
    private static FlowRuntime AgentRuntime(ScriptedLlmProvider provider, RecordingToolHandler? tools) =>
        new(new InMemoryFlowAgentResolver(new FlowAgent("agent", provider, tools)));

    /// <summary>Builds the one-agent flow these tests share.</summary>
    /// <param name="guardrailIds">The guardrails the node names.</param>
    /// <returns>The flow.</returns>
    private static FlowDefinition AgentFlow(string[]? guardrailIds = null) => new()
    {
        Id = "guarded",
        Name = "Guarded flow",
        StartNodeId = "work",
        Nodes =
        [
            new FlowNode
            {
                Id = "work",
                Kind = FlowNodeKind.Agent,
                Name = "Work",
                AgentId = "agent",
                GuardrailIds = [.. guardrailIds ?? []]
            },
            new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal, Name = "Done" }
        ],
        Edges = [new FlowEdge { Id = "e", FromNodeId = "work", ToNodeId = "end" }]
    };

    /// <summary>Builds a guardrail that refuses every tool call with a fixed plain-text reason.</summary>
    /// <param name="id">The guardrail id.</param>
    /// <param name="reason">The English reason, supplied WITHOUT a code, as a host may.</param>
    /// <returns>The guardrail.</returns>
    private static IFlowGuardrail BlockingTool(string id, string reason) =>
        new DelegateFlowGuardrail(id, "Refuses every tool call", [GuardrailStage.ToolCall],
            (_, _) => Task.FromResult(GuardrailDecision.Block(reason)));
}
