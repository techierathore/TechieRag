using TechieDesk.Services.Flows;
using TechieRag.Orchestration;
using Xunit;

namespace TechieDesk.Tests.Flows;

/// <summary>
/// A flow is only refused for a missing model when it actually calls one (REQ-UI-040).
/// </summary>
/// <remarks>
/// Observed on the live Mac Catalyst head on 2026-08-01: a branch-and-end flow — no agent step, no
/// tokens, no request — was refused with "no LLM provider is configured". That is a false refusal,
/// and it is the kind that teaches a user their install is broken when it is not.
/// </remarks>
public sealed class FlowCapabilityTests
{
    /// <summary>A flow whose only steps are a branch and an end needs no model.</summary>
    [Fact]
    public void ABranchAndEndFlowNeedsNoModel()
    {
        var branch = FlowNodeCatalog.CreateNode(FlowNodeKind.Condition, "step-branch");
        var end = FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "step-end");

        Assert.False(FlowCapabilities.NeedsLlmProvider(Flow(branch, end)));
    }

    /// <summary>A deterministic tool step needs no model either — that is what makes it deterministic.</summary>
    [Fact]
    public void AToolOnlyFlowNeedsNoModel()
    {
        var tool = FlowNodeCatalog.CreateNode(FlowNodeKind.Tool, "step-tool");
        tool.ToolName = "chart-generate";
        var end = FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "step-end");

        Assert.False(FlowCapabilities.NeedsLlmProvider(Flow(tool, end)));
    }

    /// <summary>One agent step anywhere in the flow means the run will call a model.</summary>
    [Fact]
    public void AnyAgentStepMeansTheFlowNeedsAModel()
    {
        var branch = FlowNodeCatalog.CreateNode(FlowNodeKind.Condition, "step-branch");
        var agent = FlowNodeCatalog.CreateNode(FlowNodeKind.Agent, "step-agent");
        var end = FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "step-end");

        Assert.True(FlowCapabilities.NeedsLlmProvider(Flow(branch, agent, end)));
    }

    /// <summary>
    /// The answer comes from the catalogue, so every kind the library publishes is covered without a
    /// list here that could go stale.
    /// </summary>
    [Fact]
    public void EveryCatalogueKindIsAnswered()
    {
        foreach (var descriptor in FlowNodeCatalog.Kinds)
        {
            var node = FlowNodeCatalog.CreateNode(descriptor.Kind, "step-only");
            Assert.Equal(descriptor.UsesLlm, FlowCapabilities.NeedsLlmProvider(Flow(node)));
        }
    }

    /// <summary>Wraps nodes into the smallest flow that carries them.</summary>
    /// <param name="nodes">The steps.</param>
    /// <returns>The flow.</returns>
    private static FlowDefinition Flow(params FlowNode[] nodes) => new()
    {
        Id = "flow-capability",
        Name = "Capability",
        StartNodeId = nodes[0].Id,
        Nodes = [.. nodes]
    };
}
