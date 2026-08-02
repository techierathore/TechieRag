using TechieDesk.Services.Workspaces;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Workspaces;

/// <summary>
/// Tests for the chat composer's per-turn selections (REQ-UI-044 / BRD-137): the five answering
/// modes, the "this turn only" model override, and the retrieval-scope picker.
/// </summary>
public class ChatComposerTests
{
    /// <summary>
    /// Auto-RAG is the only mode that defers to the workspace's own chat-vs-query setting, so it
    /// leaves the per-turn chat mode unset and lets the library apply the stored workspace mode.
    /// </summary>
    [Fact]
    public void AutoRagDefersToTheWorkspaceMode()
    {
        var composer = new ChatComposerState { Mode = ChatAnswerMode.AutoRag };

        var plan = composer.PlanTurn(WorkspaceWith(WorkspaceChatMode.Query));

        Assert.Null(plan.Overrides.ChatMode);
        Assert.True(plan.RetrievalEnabled);
        Assert.False(plan.ToolsEnabled);
    }

    /// <summary>
    /// Picking Query answers this one turn strictly from the documents even though the workspace
    /// itself is a Chat workspace — the point of REQ-UI-044, since BRD-48's modes were previously
    /// reachable only by editing the workspace.
    /// </summary>
    [Fact]
    public void QueryModeForcesQueryForThisTurnOnly()
    {
        var workspace = WorkspaceWith(WorkspaceChatMode.Chat);
        var composer = new ChatComposerState { Mode = ChatAnswerMode.Query };

        var plan = composer.PlanTurn(workspace);

        Assert.Equal(WorkspaceChatMode.Query, plan.Overrides.ChatMode);
        Assert.Equal(WorkspaceChatMode.Chat, workspace.ChatMode);
    }

    /// <summary>
    /// Picking Chat relaxes a Query workspace for this turn, so the model may combine the
    /// documents with general knowledge without the workspace being changed.
    /// </summary>
    [Fact]
    public void ChatModeRelaxesAQueryWorkspaceForThisTurn()
    {
        var workspace = WorkspaceWith(WorkspaceChatMode.Query);
        var composer = new ChatComposerState { Mode = ChatAnswerMode.Chat };

        var plan = composer.PlanTurn(workspace);

        Assert.Equal(WorkspaceChatMode.Chat, plan.Overrides.ChatMode);
        Assert.Equal(WorkspaceChatMode.Query, workspace.ChatMode);
    }

    /// <summary>
    /// Direct-LLM turns off retrieval entirely, which is what makes it different from Chat mode:
    /// the workspace documents are never consulted and no citations can be produced.
    /// </summary>
    [Fact]
    public void DirectLlmDisablesRetrieval()
    {
        var composer = new ChatComposerState { Mode = ChatAnswerMode.DirectLlm };

        var plan = composer.PlanTurn(WorkspaceWith(WorkspaceChatMode.Chat));

        Assert.False(plan.RetrievalEnabled);
        Assert.False(plan.ToolsEnabled);
    }

    /// <summary>
    /// Agent mode keeps retrieval available but hands it to the model as a tool rather than
    /// running it unconditionally before generation.
    /// </summary>
    [Fact]
    public void AgentModeEnablesTools()
    {
        var composer = new ChatComposerState { Mode = ChatAnswerMode.Agent };

        var plan = composer.PlanTurn(WorkspaceWith(WorkspaceChatMode.Chat));

        Assert.True(plan.ToolsEnabled);
        Assert.True(plan.RetrievalEnabled);
    }

    /// <summary>
    /// Every one of the five modes the acceptance names produces a distinct plan, so the selector
    /// cannot be a decorative dropdown where several options do the same thing.
    /// </summary>
    [Fact]
    public void AllFiveModesProduceDistinctPlans()
    {
        var workspace = WorkspaceWith(WorkspaceChatMode.Chat);
        ChatAnswerMode[] modes =
        [
            ChatAnswerMode.AutoRag, ChatAnswerMode.Query, ChatAnswerMode.Chat,
            ChatAnswerMode.DirectLlm, ChatAnswerMode.Agent
        ];

        var shapes = modes
            .Select(mode => new ChatComposerState { Mode = mode }.PlanTurn(workspace))
            .Select(plan => $"{plan.Overrides.ChatMode}|{plan.RetrievalEnabled}|{plan.ToolsEnabled}|{plan.Mode}")
            .ToList();

        Assert.Equal(5, modes.Length);
        Assert.Equal(5, shapes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The per-turn model override reaches the turn it was chosen for and is then dropped, so the
    /// next turn falls back to the workspace model. This is the "this turn only" contract.
    /// </summary>
    [Fact]
    public void TurnModelAppliesToThatTurnOnly()
    {
        var workspace = WorkspaceWith(WorkspaceChatMode.Chat);
        workspace.LlmModel = "workspace-model";
        var composer = new ChatComposerState { TurnModel = "one-off-model" };

        var first = composer.TakeTurn(workspace);
        var second = composer.TakeTurn(workspace);

        Assert.Equal("one-off-model", first.Overrides.LlmModel);
        Assert.Equal("one-off-model", first.Model);

        Assert.Null(second.Overrides.LlmModel);
        Assert.Equal("workspace-model", second.Model);
        Assert.Null(composer.TurnModel);
    }

    /// <summary>
    /// With no per-turn model chosen the plan carries no model override at all, so the workspace
    /// or provider model decides — the override never fabricates a model.
    /// </summary>
    [Fact]
    public void NoTurnModelLeavesTheOverrideUnset()
    {
        var workspace = WorkspaceWith(WorkspaceChatMode.Chat);

        var plan = new ChatComposerState().TakeTurn(workspace);

        Assert.Null(plan.Overrides.LlmModel);
        Assert.Null(plan.Model);
        Assert.Null(plan.CompletionOptions);
    }

    /// <summary>
    /// The mode and scope are sticky selections, unlike the model: sending a turn must not silently
    /// reset the answering mode or the retrieval scope the user chose.
    /// </summary>
    [Fact]
    public void ModeAndScopeSurviveTheTurn()
    {
        var composer = new ChatComposerState
        {
            Mode = ChatAnswerMode.Query,
            Scope = WorkspaceRetrievalScope.PinnedOnly
        };

        composer.TakeTurn(WorkspaceWith(WorkspaceChatMode.Chat));

        Assert.Equal(ChatAnswerMode.Query, composer.Mode);
        Assert.Equal(WorkspaceRetrievalScope.PinnedOnly, composer.Scope);
    }

    /// <summary>
    /// The chosen-documents scope carries the picked document ids into the overrides, and copies
    /// them, so editing the picker afterwards cannot mutate a turn already in flight.
    /// </summary>
    [Fact]
    public void ChosenDocumentsFlowIntoTheOverridesAsACopy()
    {
        var composer = new ChatComposerState { Scope = WorkspaceRetrievalScope.SelectedDocuments };
        composer.SelectedDocumentIds.Add("doc-a");

        var plan = composer.TakeTurn(WorkspaceWith(WorkspaceChatMode.Chat));
        composer.SelectedDocumentIds.Add("doc-b");

        Assert.Equal(WorkspaceRetrievalScope.SelectedDocuments, plan.Overrides.Scope);
        Assert.Equal(["doc-a"], plan.Overrides.DocumentIds);
    }

    /// <summary>
    /// The pinned-only scope needs no document list, so the picker can offer it without the user
    /// having to tick anything.
    /// </summary>
    [Fact]
    public void PinnedOnlyScopeNeedsNoDocumentList()
    {
        var composer = new ChatComposerState { Scope = WorkspaceRetrievalScope.PinnedOnly };

        var plan = composer.TakeTurn(WorkspaceWith(WorkspaceChatMode.Chat));

        Assert.Equal(WorkspaceRetrievalScope.PinnedOnly, plan.Overrides.Scope);
        Assert.Empty(plan.Overrides.DocumentIds!);
    }

    /// <summary>Builds a workspace with the given stored answer mode.</summary>
    /// <param name="mode">The workspace's stored chat-vs-query mode.</param>
    /// <returns>A workspace usable as the plan's baseline.</returns>
    private static Workspace WorkspaceWith(WorkspaceChatMode mode) =>
        new() { Name = "Contracts", ChatMode = mode };
}
