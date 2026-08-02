using TechieRag.Models;
using TechieRag.Orchestration;
using Xunit;

namespace TechieRag.Tests.Orchestration;

/// <summary>
/// Proves the handoff contract by reading the conversation the RECEIVING model was actually sent
/// (REQ-RAG-042 / BRD-123).
/// </summary>
/// <remarks>
/// <para>Everything asserted here is a claim about messages, so every assertion reads the messages
/// the receiver's provider recorded. "The handoff carried a summary" is not checkable from an
/// output string; it is checkable from what the second model saw.</para>
/// <para>Each test also asserts what did NOT cross. A contract stating that only the declared
/// context passes is only tested if something undeclared is present in the run and proven absent
/// from the receiver's context — so the sender always has a distinctive system prompt, a
/// distinctive tool result, and a variable that is deliberately left off the allowlist.</para>
/// </remarks>
public sealed class FlowHandoffTests
{
    private const string SenderSecretPrompt = "SENDERSYSTEMPROMPT you are the intake specialist";
    private const string ToolSecret = "TOOLRESULTSECRET ledger row 42";

    /// <summary>
    /// The default narrow mode hands over the sender's answer and the named variable, and nothing
    /// else: not the sender's system prompt, not its tool result, not an uncarried variable.
    /// </summary>
    [Fact]
    public async Task ANarrowHandoffCarriesTheAnswerAndTheNamedVariableAndNothingElse()
    {
        var (sender, receiver, flow, runtime) = BuildHandoffFlow(HandoffContextMode.LastOutputOnly, ["accountId"]);

        var result = await new FlowRunner(flow, runtime).RunAsync("ORIGINALREQUEST close my account", InitialVariables());

        Assert.Equal(FlowRunOutcome.Completed, result.Outcome);
        Assert.Equal(1, receiver.TurnCount);

        var seen = receiver.AllSeenText;

        // What crosses.
        Assert.Contains("SENDERANSWER", seen);
        Assert.Contains("accountId: A-1", seen);
        Assert.Contains("RECEIVERSYSTEMPROMPT", seen);
        Assert.Contains("Control has been handed to you", seen);
        Assert.Contains("Reason: the customer wants to cancel", seen);

        // What does not.
        Assert.DoesNotContain("SENDERSYSTEMPROMPT", seen);
        Assert.DoesNotContain("TOOLRESULTSECRET", seen);
        Assert.DoesNotContain("UNCARRIED", seen);
        Assert.DoesNotContain("ORIGINALREQUEST", seen);

        // The sender really did have all of it, so the absences above are the boundary working
        // rather than the run never producing the material.
        Assert.Contains("SENDERSYSTEMPROMPT", sender.AllSeenText);
        Assert.Contains("TOOLRESULTSECRET", sender.AllSeenText);
        Assert.Equal("UNCARRIEDSECRET", result.Variables["internalNote"]);
    }

    /// <summary>
    /// The wider mode adds the original request — and still not the sender's prompt, its tool
    /// result, or an uncarried variable.
    /// </summary>
    [Fact]
    public async Task TheOriginalInputModeAddsTheRequestAndStillNothingUndeclared()
    {
        var (_, receiver, flow, runtime) =
            BuildHandoffFlow(HandoffContextMode.OriginalInputAndLastOutput, ["accountId"]);

        await new FlowRunner(flow, runtime).RunAsync("ORIGINALREQUEST close my account", InitialVariables());

        var seen = receiver.AllSeenText;
        Assert.Contains("ORIGINALREQUEST", seen);
        Assert.Contains("SENDERANSWER", seen);
        Assert.DoesNotContain("SENDERSYSTEMPROMPT", seen);
        Assert.DoesNotContain("TOOLRESULTSECRET", seen);
        Assert.DoesNotContain("UNCARRIED", seen);
    }

    /// <summary>
    /// The explicit transcript mode is the one that hands over everything — including the sender's
    /// system prompt and tool results — which is exactly why it is opt-in and named for what it does.
    /// </summary>
    [Fact]
    public async Task TheFullTranscriptModeCarriesTheSendersWholeConversation()
    {
        var (_, receiver, flow, runtime) = BuildHandoffFlow(HandoffContextMode.FullTranscript, []);

        await new FlowRunner(flow, runtime).RunAsync("ORIGINALREQUEST close my account", InitialVariables());

        var seen = receiver.AllSeenText;
        Assert.Contains("SENDERSYSTEMPROMPT", seen);
        Assert.Contains("TOOLRESULTSECRET", seen);

        // Even here, a variable that was not on the allowlist is not rendered into the context. Flow
        // state is not agent context, in every mode.
        Assert.DoesNotContain("UNCARRIEDSECRET", seen);
    }

    /// <summary>An empty carry list means no variable is shown to the receiver, not "all of them".</summary>
    [Fact]
    public async Task AnEmptyCarryListCarriesNoVariables()
    {
        var (_, receiver, flow, runtime) = BuildHandoffFlow(HandoffContextMode.LastOutputOnly, []);

        await new FlowRunner(flow, runtime).RunAsync("ORIGINALREQUEST close my account", InitialVariables());

        Assert.DoesNotContain("accountId: A-1", receiver.AllSeenText);
        Assert.DoesNotContain("UNCARRIED", receiver.AllSeenText);
    }

    /// <summary>The transfer appears in the trace, naming both ends and what crossed.</summary>
    [Fact]
    public async Task TheHandoffIsRecordedInTheTrace()
    {
        var (_, _, flow, runtime) = BuildHandoffFlow(HandoffContextMode.LastOutputOnly, ["accountId"]);

        var result = await new FlowRunner(flow, runtime).RunAsync("ORIGINALREQUEST close my account", InitialVariables());

        var handoff = Assert.Single(result.Steps, step => step.Kind == AgentStepKind.HandoffPerformed);
        Assert.Equal("transfer", handoff.FromNodeId);
        Assert.Equal("closer", handoff.ToNodeId);
        Assert.Contains("LastOutputOnly", handoff.Content);
        Assert.Contains("accountId", handoff.Content);
    }

    /// <summary>
    /// The variables every run in this class starts with: one the handoffs may carry, one they never do.
    /// </summary>
    /// <returns>The initial flow variables.</returns>
    /// <remarks>
    /// Seeded on the run rather than produced by a tool node on purpose. A variable written by an
    /// earlier node would also be the running OUTPUT at some point, and would then reach the
    /// receiver legitimately — making every "did not cross" assertion below unfalsifiable.
    /// </remarks>
    private static Dictionary<string, string> InitialVariables() => new()
    {
        ["accountId"] = "A-1",
        ["internalNote"] = "UNCARRIEDSECRET"
    };

    /// <summary>
    /// Builds a two-agent flow where the sender has a distinctive prompt, calls a tool with a
    /// distinctive result, and then hands control to a second agent.
    /// </summary>
    /// <param name="mode">The handoff context mode under test.</param>
    /// <param name="carry">The variables the handoff declares.</param>
    /// <returns>The sender's provider, the receiver's provider, the flow and its runtime.</returns>
    private static (ScriptedLlmProvider Sender, ScriptedLlmProvider Receiver, FlowDefinition Flow, FlowRuntime Runtime)
        BuildHandoffFlow(HandoffContextMode mode, string[] carry)
    {
        var sender = new ScriptedLlmProvider(
            "sender",
            ScriptedLlmProvider.CallsTool("ledger", """{"account":"A-1"}"""),
            ScriptedLlmProvider.Says("SENDERANSWER the account is in good standing"));

        var receiver = new ScriptedLlmProvider("receiver", ScriptedLlmProvider.Says("closed the account"));

        var tools = new RecordingToolHandler().Register("ledger", "Reads the ledger", _ => ToolSecret);

        var senderAgent = new FlowAgent("sender", sender, tools) { SystemPrompt = SenderSecretPrompt };
        var receiverAgent = new FlowAgent("receiver", receiver)
        {
            SystemPrompt = "RECEIVERSYSTEMPROMPT you close accounts",
            DisplayName = "Closer"
        };

        var flow = new FlowDefinition
        {
            Id = "handoff",
            Name = "Intake then close",
            StartNodeId = "intake",
            Nodes =
            [
                new FlowNode { Id = "intake", Kind = FlowNodeKind.Agent, Name = "Intake", AgentId = "sender" },
                new FlowNode
                {
                    Id = "transfer",
                    Kind = FlowNodeKind.Handoff,
                    Name = "Transfer",
                    Handoff = new FlowHandoff
                    {
                        TargetNodeId = "closer",
                        ContextMode = mode,
                        CarryVariables = [.. carry],
                        Reason = "the customer wants to cancel"
                    }
                },
                new FlowNode { Id = "closer", Kind = FlowNodeKind.Agent, Name = "Closer", AgentId = "receiver" },
                new FlowNode { Id = "end", Kind = FlowNodeKind.Terminal }
            ],
            Edges =
            [
                new FlowEdge { Id = "e2", FromNodeId = "intake", ToNodeId = "transfer" },
                new FlowEdge { Id = "e3", FromNodeId = "closer", ToNodeId = "end" }
            ]
        };

        var runtime = new FlowRuntime(new InMemoryFlowAgentResolver(senderAgent, receiverAgent));

        return (sender, receiver, flow, runtime);
    }
}
