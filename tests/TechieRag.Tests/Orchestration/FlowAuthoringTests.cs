using TechieRag.Orchestration;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// Covers the four things a no-code flow builder does before it ever runs anything — enumerate the
/// node kinds, compose a graph, persist it, and validate it (REQ-RAG-042 / BRD-123, prerequisite for
/// REQ-UI-040 / BRD-92).
/// </summary>
public sealed class FlowAuthoringTests
{
    /// <summary>Every node kind the model defines has a catalogue entry, so a palette built by enumerating it is complete.</summary>
    [Fact]
    public void TheCatalogueDescribesEveryNodeKind()
    {
        var described = FlowNodeCatalog.Kinds.Select(descriptor => descriptor.Kind).ToArray();

        Assert.Equal(Enum.GetValues<FlowNodeKind>().Length, described.Length);
        Assert.Equal(Enum.GetValues<FlowNodeKind>().OrderBy(kind => kind), described.OrderBy(kind => kind));
        Assert.All(FlowNodeCatalog.Kinds, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
        });
    }

    /// <summary>
    /// A field the catalogue marks required is a field the validator errors on, so the editor and
    /// the validator cannot disagree about what a valid node is.
    /// </summary>
    [Theory]
    [InlineData(FlowNodeKind.Agent, FlowValidationCodes.MissingAgentId)]
    [InlineData(FlowNodeKind.Tool, FlowValidationCodes.MissingToolName)]
    [InlineData(FlowNodeKind.Handoff, FlowValidationCodes.MissingHandoffTarget)]
    public void ARequiredFieldLeftBlankIsAValidationError(FlowNodeKind kind, string expectedCode)
    {
        Assert.Contains(FlowNodeCatalog.Describe(kind).Fields, field => field.IsRequired);

        var node = FlowNodeCatalog.CreateNode(kind, "subject");
        var flow = new FlowDefinition
        {
            Id = "f",
            Name = "F",
            StartNodeId = "subject",
            Nodes = [node, new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }],
            Edges = [new FlowEdge { Id = "e", FromNodeId = "subject", ToNodeId = "end" }]
        };

        var result = FlowValidator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Code == expectedCode && issue.NodeId == "subject");
    }

    /// <summary>A composed flow with everything filled in validates cleanly, with no errors.</summary>
    [Fact]
    public void AWellFormedFlowValidatesWithNoErrors()
    {
        var result = FlowValidator.Validate(SampleFlow());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    /// <summary>Everything a builder sets survives a store-and-reopen, including uninterpreted canvas metadata.</summary>
    [Fact]
    public void AFlowRoundTripsThroughTheSerializerUnchanged()
    {
        var original = SampleFlow();
        original.Metadata["canvasZoom"] = "1.25";
        original.Nodes[0].Metadata["x"] = "120";
        original.Nodes[0].Metadata["y"] = "48";

        var json = FlowSerializer.ToJson(original);
        var reopened = FlowSerializer.FromJson(json);

        Assert.Equal(original.Id, reopened.Id);
        Assert.Equal(original.Name, reopened.Name);
        Assert.Equal(original.StartNodeId, reopened.StartNodeId);
        Assert.Equal(original.MaxSteps, reopened.MaxSteps);
        Assert.Equal(original.Nodes.Count, reopened.Nodes.Count);
        Assert.Equal(original.Edges.Count, reopened.Edges.Count);

        // Layout the library never interprets still comes back.
        Assert.Equal("1.25", reopened.Metadata["canvasZoom"]);
        Assert.Equal("120", reopened.Nodes[0].Metadata["x"]);

        // Kind-specific detail comes back typed, not as loose strings.
        var handoff = reopened.Nodes.Single(node => node.Kind == FlowNodeKind.Handoff).Handoff;
        Assert.NotNull(handoff);
        Assert.Equal(HandoffContextMode.OriginalInputAndLastOutput, handoff.ContextMode);
        Assert.Equal(new[] { "accountId" }, handoff.CarryVariables);

        var conditional = reopened.Edges.Single(edge => edge.Condition is not null);
        Assert.Equal(FlowConditionKind.Contains, conditional.Condition!.Kind);
        Assert.Equal("refund", conditional.Condition.Operand);
    }

    /// <summary>Enums are written as names, so a stored flow stays readable and survives a reordered enum.</summary>
    [Fact]
    public void TheStoredDocumentWritesEnumsAsNames()
    {
        var json = FlowSerializer.ToJson(SampleFlow());

        Assert.Contains("\"Kind\": \"Agent\"", json);
        Assert.Contains("\"Kind\": \"Handoff\"", json);
        Assert.DoesNotContain("\"Kind\": 0", json);
    }

    /// <summary>A document from a newer schema is refused outright rather than partly loaded.</summary>
    [Fact]
    public void AFutureSchemaVersionIsRefusedRatherThanPartlyLoaded()
    {
        var json = FlowSerializer.ToJson(SampleFlow())
            .Replace($"\"SchemaVersion\": {FlowSerializer.CurrentSchemaVersion}", "\"SchemaVersion\": 99", StringComparison.Ordinal);

        var error = Assert.Throws<FlowSerializationException>(() => FlowSerializer.FromJson(json));
        Assert.Contains("99", error.Message);

        Assert.False(FlowSerializer.TryFromJson(json, out var flow, out var reason));
        Assert.Null(flow);
        Assert.Contains("99", reason);
    }

    /// <summary>Malformed stored data fails loudly, naming the problem, instead of producing a half-flow.</summary>
    [Fact]
    public void AMalformedDocumentFailsLoudly()
    {
        Assert.Throws<FlowSerializationException>(() => FlowSerializer.FromJson("{ this is not json"));
        Assert.False(FlowSerializer.TryFromJson("{ this is not json", out _, out var reason));
        Assert.NotNull(reason);
    }

    /// <summary>An edge pointing at a node that does not exist is an error, naming the edge.</summary>
    [Fact]
    public void ADanglingEdgeIsAnError()
    {
        var flow = SampleFlow();
        flow.Edges.Add(new FlowEdge { Id = "broken", FromNodeId = "classify", ToNodeId = "nowhere" });

        var result = FlowValidator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Code == FlowValidationCodes.DanglingEdge && issue.EdgeId == "broken");
    }

    /// <summary>A cycle is an error by default and a warning once the flow opts in — the documented choice, both ways.</summary>
    [Fact]
    public void ACycleIsAnErrorByDefaultAndAWarningWhenAllowed()
    {
        var flow = SampleFlow();
        flow.Edges.Add(new FlowEdge { Id = "loop", FromNodeId = "end", ToNodeId = "classify" });

        var refused = FlowValidator.Validate(flow);
        Assert.False(refused.IsValid);
        Assert.Contains(refused.Errors, issue => issue.Code == FlowValidationCodes.CycleDetected);

        flow.AllowCycles = true;
        var permitted = FlowValidator.Validate(flow);
        Assert.True(permitted.IsValid);
        Assert.Contains(permitted.Warnings, issue => issue.Code == FlowValidationCodes.CycleDetected);
    }

    /// <summary>An unreachable node is reported, so a builder can grey it out rather than shipping dead canvas.</summary>
    [Fact]
    public void AnUnreachableNodeIsReported()
    {
        var flow = SampleFlow();
        flow.Nodes.Add(new FlowNode { Id = "orphan", Kind = FlowNodeKind.Terminal, Name = "Orphan" });

        var result = FlowValidator.Validate(flow);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, issue => issue.Code == FlowValidationCodes.UnreachableNode && issue.NodeId == "orphan");
    }

    /// <summary>
    /// A branch whose unconditional edge is evaluated first hides every later edge, which the
    /// validator says out loud instead of leaving the author to wonder why one branch never fires.
    /// </summary>
    [Fact]
    public void AnUnconditionalEdgeAheadOfAConditionalOneIsReported()
    {
        var flow = SampleFlow();
        flow.Edges.Single(edge => edge.Id == "toEscalate").Order = 5;

        var result = FlowValidator.Validate(flow);

        Assert.Contains(result.Warnings, issue => issue.Code == FlowValidationCodes.UnreachableBranch);
    }

    /// <summary>A regular expression that will not compile is caught at edit time, not at run time.</summary>
    [Fact]
    public void AMalformedPatternIsCaughtAtEditTime()
    {
        var flow = SampleFlow();
        flow.Edges.Single(edge => edge.Id == "toEscalate").Condition =
            new FlowCondition { Kind = FlowConditionKind.Matches, Operand = "([unclosed" };

        var result = FlowValidator.Validate(flow);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Code == FlowValidationCodes.InvalidPattern);
    }

    /// <summary>
    /// The binding pass catches what the structural pass cannot: an agent, a guardrail or a tool
    /// this host does not have. A guardrail miss is an ERROR because the node would be blocked.
    /// </summary>
    [Fact]
    public async Task TheBindingPassReportsWhatThisHostCannotResolve()
    {
        var flow = SampleFlow();
        flow.Nodes.Single(node => node.Id == "classify").GuardrailIds.Add("compliance");

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver());

        var result = await FlowValidator.ValidateAsync(flow, runtime);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, issue => issue.Code == FlowValidationCodes.UnresolvableAgent);
        Assert.Contains(result.Errors, issue => issue.Code == FlowValidationCodes.UnresolvableGuardrail);
        Assert.Contains(result.Errors, issue => issue.Code == FlowValidationCodes.NoToolHandler);
    }

    /// <summary>The same flow binds cleanly once the host actually supplies its agents, guardrails and tools.</summary>
    [Fact]
    public async Task TheBindingPassPassesOnAHostThatHasEverything()
    {
        var flow = SampleFlow();
        flow.Nodes.Single(node => node.Id == "classify").GuardrailIds.Add("compliance");

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(
            new FlowAgent("classifier", new ScriptedLlmProvider("classifier")),
            new FlowAgent("closer", new ScriptedLlmProvider("closer"))))
        {
            Guardrails = new InMemoryFlowGuardrailResolver(new DelegateFlowGuardrail(
                "compliance", "Checks compliance", null, (_, _) => Task.FromResult(GuardrailDecision.Allow()))),
            Tools = new RecordingToolHandler().Register("escalate", "Raises a ticket", _ => "raised")
        };

        var result = await FlowValidator.ValidateAsync(flow, runtime);

        Assert.True(result.IsValid);
    }

    /// <summary>A run refuses an invalid flow and hands back the issues rather than throwing.</summary>
    [Fact]
    public async Task RunningAnInvalidFlowReturnsTheIssuesInsteadOfThrowing()
    {
        var flow = SampleFlow();
        flow.StartNodeId = "does-not-exist";

        var result = await new FlowRunner(flow, new FlowRuntime(new InMemoryFlowAgentResolver())).RunAsync("hello");

        Assert.Equal(FlowRunOutcome.Failed, result.Outcome);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == FlowValidationCodes.UnknownStartNode);
        Assert.Empty(result.Steps);
    }

    /// <summary>A freshly created node carries its kind's palette label and a unique id.</summary>
    [Fact]
    public void CreatingANodeFromTheCatalogueGivesItAUniqueIdAndItsPaletteLabel()
    {
        var first = FlowNodeCatalog.CreateNode(FlowNodeKind.Agent);
        var second = FlowNodeCatalog.CreateNode(FlowNodeKind.Agent);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("Agent", first.Name);
        Assert.Equal(FlowNodeKind.Agent, first.Kind);
    }

    /// <summary>A condition can be previewed against a sample state without running anything.</summary>
    [Fact]
    public void AConditionCanBeEvaluatedAgainstASampleStateForAPreview()
    {
        var condition = new FlowCondition { Kind = FlowConditionKind.Contains, Operand = "refund" };

        Assert.True(condition.IsSatisfiedBy(new FlowState("I want a REFUND please")));
        Assert.False(condition.IsSatisfiedBy(new FlowState("I forgot my password")));
    }

    /// <summary>Builds the two-agent support flow the authoring tests share.</summary>
    /// <returns>A structurally valid flow exercising every node kind except a bare condition branch.</returns>
    private static FlowDefinition SampleFlow() => new()
    {
        Id = "support",
        Name = "Support triage",
        Description = "Classify, escalate refunds, hand the rest to the closer.",
        StartNodeId = "classify",
        Nodes =
        [
            new FlowNode { Id = "classify", Kind = FlowNodeKind.Agent, Name = "Classify", AgentId = "classifier", OutputVariable = "accountId" },
            new FlowNode { Id = "escalate", Kind = FlowNodeKind.Tool, Name = "Escalate", ToolName = "escalate" },
            new FlowNode
            {
                Id = "transfer",
                Kind = FlowNodeKind.Handoff,
                Name = "Hand to closer",
                Handoff = new FlowHandoff
                {
                    TargetNodeId = "closer",
                    ContextMode = HandoffContextMode.OriginalInputAndLastOutput,
                    CarryVariables = ["accountId"],
                    Reason = "the classifier could not resolve it"
                }
            },
            new FlowNode { Id = "closer", Kind = FlowNodeKind.Agent, Name = "Closer", AgentId = "closer" },
            new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal, Name = "End", TerminalStatus = "resolved" }
        ],
        Edges =
        [
            new FlowEdge
            {
                Id = "toEscalate", FromNodeId = "classify", ToNodeId = "escalate", Order = 0, Label = "refund",
                Condition = new FlowCondition { Kind = FlowConditionKind.Contains, Operand = "refund" }
            },
            new FlowEdge { Id = "toTransfer", FromNodeId = "classify", ToNodeId = "transfer", Order = 1, Label = "everything else" },
            new FlowEdge { Id = "escalateToEnd", FromNodeId = "escalate", ToNodeId = "end" },
            new FlowEdge { Id = "closerToEnd", FromNodeId = "closer", ToNodeId = "end" }
        ]
    };
}
