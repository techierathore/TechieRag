using TechieRag.Orchestration;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// Host-named flow steps (REQ-RAG-123 / BRD-183, TR-RAG-042) and asking a flow whether it calls a
/// language model (REQ-RAG-124 / BRD-184, TR-RAG-043).
/// </summary>
public sealed class FlowStepNameAndModelUseTests
{
    /// <summary>
    /// A developer creates a node with a step name: the node carries that name instead of the
    /// catalogue's English one.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-123 CreateNodeCarriesHostStepName")]
    public void CreateNodeCarriesHostStepName()
    {
        var node = FlowNodeCatalog.CreateNode(FlowNodeKind.Agent, "triage", "Anfrage einordnen");

        Assert.Equal("Anfrage einordnen", node.Name);
        Assert.NotEqual(FlowNodeCatalog.Describe(FlowNodeKind.Agent).DisplayName, node.Name);
        Assert.Equal("triage", node.Id);
    }

    /// <summary>With no step name, or the original two-argument call, the English default stays.</summary>
    [Fact(DisplayName = "REQ-RAG-123 CreateNodeWithoutStepNameKeepsDefault")]
    public void CreateNodeWithoutStepNameKeepsDefault()
    {
        Assert.Equal("Tool", FlowNodeCatalog.CreateNode(FlowNodeKind.Tool, "t1").Name);
        Assert.Equal("Tool", FlowNodeCatalog.CreateNode(FlowNodeKind.Tool, "t2", null).Name);
    }

    /// <summary>
    /// A developer asks a flow with one model step whether it uses a model: it answers yes and lists
    /// that step.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-124 FlowReportsItsModelStep")]
    public void FlowReportsItsModelStep()
    {
        var flow = new FlowDefinition
        {
            Id = "f1",
            Name = "Triage",
            Nodes =
            [
                FlowNodeCatalog.CreateNode(FlowNodeKind.Tool, "fetch"),
                FlowNodeCatalog.CreateNode(FlowNodeKind.Agent, "answer"),
                FlowNodeCatalog.CreateNode(FlowNodeKind.Condition, "route"),
                FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "end")
            ]
        };

        Assert.True(flow.UsesLanguageModel());
        Assert.Equal("answer", Assert.Single(flow.GetLanguageModelSteps()).Id);
    }

    /// <summary>A flow made only of tool, condition and end steps answers no, and the answer is not persisted.</summary>
    [Fact(DisplayName = "REQ-RAG-124 FlowWithoutModelStepsAnswersNo")]
    public void FlowWithoutModelStepsAnswersNo()
    {
        var flow = new FlowDefinition
        {
            Id = "f2",
            Name = "Glue",
            Nodes = [FlowNodeCatalog.CreateNode(FlowNodeKind.Tool, "a"), FlowNodeCatalog.CreateNode(FlowNodeKind.Terminal, "end")]
        };

        Assert.False(flow.UsesLanguageModel());
        Assert.Empty(flow.GetLanguageModelSteps());
        Assert.DoesNotContain("UsesLanguageModel", FlowSerializer.ToJson(flow), StringComparison.Ordinal);
    }
}
