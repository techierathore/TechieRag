using TechieRag.Models;

namespace TechieDesk.Services.Workspaces;

/// <summary>
/// The answering mode a user picks for a single chat turn (BRD-137 / REQ-UI-044).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BRD-48 defines chat-vs-query answering, but before REQ-UI-044 the only
/// way to reach it was the workspace settings screen, so it could not be chosen for one question.
/// These are the five modes the composer offers.</para>
/// </remarks>
public enum ChatAnswerMode
{
    /// <summary>Retrieve from the workspace, then answer with citations using the workspace's own mode.</summary>
    AutoRag,

    /// <summary>Retrieve, and answer strictly from the retrieved documents.</summary>
    Query,

    /// <summary>Retrieve, and let the model combine the documents with general knowledge.</summary>
    Chat,

    /// <summary>Skip retrieval entirely and talk to the model directly.</summary>
    DirectLlm,

    /// <summary>Run the tool-calling agent loop, with workspace retrieval offered as a tool.</summary>
    Agent
}

/// <summary>
/// The resolved instructions for one chat turn: what the turn retrieves, which model answers it,
/// and whether tools are available.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-UI-044. Produced by <see cref="ChatComposerState.TakeTurn"/> so the
/// page never re-derives per-turn behaviour from mutable UI fields while a turn is in flight.</para>
/// </remarks>
public sealed class ChatTurnPlan
{
    /// <summary>Gets the answering mode this turn runs under.</summary>
    public required ChatAnswerMode Mode { get; init; }

    /// <summary>Gets whether the turn retrieves workspace context before answering.</summary>
    public required bool RetrievalEnabled { get; init; }

    /// <summary>Gets whether the turn may call tools.</summary>
    public required bool ToolsEnabled { get; init; }

    /// <summary>Gets the per-turn overrides handed to the library workspace manager.</summary>
    public required WorkspaceTurnOverrides Overrides { get; init; }

    /// <summary>
    /// Gets the model that answers this turn — the per-turn override when one was chosen,
    /// otherwise the workspace's model, otherwise null for the provider's configured model.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets completion options carrying <see cref="Model"/>, or null when no model override
    /// applies. Used by the Direct-LLM and Agent paths, which call the provider themselves.
    /// </summary>
    public LlmCompletionOptions? CompletionOptions =>
        string.IsNullOrEmpty(Model) ? null : new LlmCompletionOptions { Model = Model };
}

/// <summary>A reusable prompt a user can insert into the composer (REQ-UI-044).</summary>
/// <param name="TitleKey">Resource key for the short label shown in the saved-prompts menu.</param>
/// <param name="TextKey">Resource key for the prompt text inserted into the composer.</param>
/// <remarks>
/// REQ-UI-051: the BODY is a key as well as the title. The text is inserted into the composer as
/// the user's own question, so an English body is an English question for the model to answer —
/// the one case where leaving a service string untranslated changes the answer, not just the label.
/// </remarks>
public sealed record SavedPrompt(string TitleKey, string TextKey);

/// <summary>
/// The chat composer's per-turn selections: answering mode, model override and retrieval scope.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BRD-137 / REQ-UI-044 — the composer control bar. Holding this state in a
/// plain class keeps the mode/model/scope rules unit-testable without rendering the page.</para>
/// <para><b>Per-turn lifetime:</b> the model override is explicitly "this turn only" and is
/// cleared by <see cref="TakeTurn"/> once a turn has been planned, so it can never silently apply
/// to the next question. Mode and scope are sticky selections and persist until changed.</para>
/// </remarks>
public sealed class ChatComposerState
{
    /// <summary>The number of text rows the composer grows to before it starts scrolling.</summary>
    public const int MaxComposerRows = 12;

    /// <summary>The prompts offered by the composer's saved-prompts menu, as resource keys.</summary>
    public static readonly IReadOnlyList<SavedPrompt> SavedPrompts =
    [
        new("ChatPromptSummariseTitle", "ChatPromptSummariseText"),
        new("ChatPromptCompareTitle", "ChatPromptCompareText"),
        new("ChatPromptObligationsTitle", "ChatPromptObligationsText"),
        new("ChatPromptExplainTitle", "ChatPromptExplainText")
    ];

    /// <summary>Gets or sets the answering mode for the next turn.</summary>
    public ChatAnswerMode Mode { get; set; } = ChatAnswerMode.AutoRag;

    /// <summary>Gets or sets the model override for the next turn only; null uses the workspace model.</summary>
    public string? TurnModel { get; set; }

    /// <summary>Gets or sets the retrieval scope applied to the next turn.</summary>
    public WorkspaceRetrievalScope Scope { get; set; } = WorkspaceRetrievalScope.WholeWorkspace;

    /// <summary>Gets the documents chosen when <see cref="Scope"/> is
    /// <see cref="WorkspaceRetrievalScope.SelectedDocuments"/>.</summary>
    public List<string> SelectedDocumentIds { get; } = [];

    /// <summary>
    /// Builds the plan for a turn without consuming the per-turn state, so the UI can describe
    /// what the next turn will do.
    /// </summary>
    /// <param name="workspace">The workspace whose stored settings the plan layers over.</param>
    /// <returns>The plan for the turn as currently configured.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> is null.</exception>
    public ChatTurnPlan PlanTurn(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var turnModel = string.IsNullOrWhiteSpace(TurnModel) ? null : TurnModel.Trim();

        // Auto-RAG is the only mode that defers to the workspace's own chat-vs-query setting;
        // every other mode states the answer mode for this turn explicitly.
        WorkspaceChatMode? chatMode = Mode switch
        {
            ChatAnswerMode.Query => WorkspaceChatMode.Query,
            ChatAnswerMode.Chat => WorkspaceChatMode.Chat,
            ChatAnswerMode.DirectLlm => WorkspaceChatMode.Chat,
            ChatAnswerMode.Agent => WorkspaceChatMode.Chat,
            _ => null
        };

        return new ChatTurnPlan
        {
            Mode = Mode,
            RetrievalEnabled = Mode != ChatAnswerMode.DirectLlm,
            ToolsEnabled = Mode == ChatAnswerMode.Agent,
            Model = turnModel ?? (string.IsNullOrEmpty(workspace.LlmModel) ? null : workspace.LlmModel),
            Overrides = new WorkspaceTurnOverrides
            {
                ChatMode = chatMode,
                LlmModel = turnModel,
                Scope = Scope,
                DocumentIds = SelectedDocumentIds.ToList()
            }
        };
    }

    /// <summary>
    /// Builds the plan for the turn about to be sent and clears the state that is valid for one
    /// turn only.
    /// </summary>
    /// <param name="workspace">The workspace whose stored settings the plan layers over.</param>
    /// <returns>The plan governing the turn being sent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> is null.</exception>
    /// <remarks>
    /// <para><b>No leakage (REQ-UI-044):</b> the model override is dropped here, so the following
    /// turn falls back to the workspace model unless the user picks a model again.</para>
    /// </remarks>
    public ChatTurnPlan TakeTurn(Workspace workspace)
    {
        var plan = PlanTurn(workspace);
        TurnModel = null;
        return plan;
    }

    /// <summary>Gets the resource key for an answering mode's short label.</summary>
    /// <param name="mode">The mode to describe.</param>
    /// <returns>The key for the label shown on the mode selector and the composer hint.</returns>
    /// <remarks>
    /// REQ-UI-051: the ENUM is the invariant identity — it is what the turn plan carries and what
    /// the workspace stores — and the key is only how it is spelled for a reader.
    /// </remarks>
    public static string ModeLabelKey(ChatAnswerMode mode) => mode switch
    {
        ChatAnswerMode.Query => "ChatModeLabelQuery",
        ChatAnswerMode.Chat => "ChatModeLabelChat",
        ChatAnswerMode.DirectLlm => "ChatModeLabelDirectLlm",
        ChatAnswerMode.Agent => "ChatModeLabelAgent",
        _ => "ChatModeLabelAutoRag"
    };

    /// <summary>Gets the resource key for the one-line explanation of what an answering mode does.</summary>
    /// <param name="mode">The mode to describe.</param>
    /// <returns>The key for the explanation shown beside the mode label.</returns>
    public static string ModeDescriptionKey(ChatAnswerMode mode) => mode switch
    {
        ChatAnswerMode.Query => "ChatModeDescQuery",
        ChatAnswerMode.Chat => "ChatModeDescChat",
        ChatAnswerMode.DirectLlm => "ChatModeDescDirectLlm",
        ChatAnswerMode.Agent => "ChatModeDescAgent",
        _ => "ChatModeDescAutoRag"
    };

    /// <summary>Gets the resource key for a retrieval scope's short label.</summary>
    /// <param name="scope">The scope to describe.</param>
    /// <returns>The key for the label shown on the scope picker.</returns>
    /// <remarks>
    /// The selected-documents key carries a <c>{0}</c> for the chosen count, so the caller resolves
    /// it as <c>localizer[key, chosenCount]</c>. The count is NOT taken here: pluralising in the
    /// service would mean spelling "document"/"documents" in code, which is precisely the English
    /// this requirement removes — and English pluralisation is not a rule Hindi shares.
    /// </remarks>
    public static string ScopeLabelKey(WorkspaceRetrievalScope scope) => scope switch
    {
        WorkspaceRetrievalScope.PinnedOnly => "ChatScopeLabelPinnedOnly",
        WorkspaceRetrievalScope.SelectedDocuments => "ChatScopeLabelSelectedDocuments",
        _ => "ChatScopeLabelWholeWorkspace"
    };
}
