namespace TechieDesk.Services.Agents;

/// <summary>
/// What an agent is allowed to read when it runs (BRD-138 / REQ-UI-045).
/// </summary>
public enum AgentKnowledgeScope
{
    /// <summary>The workspace the agent was called from. The only scope honored today.</summary>
    CallingWorkspace,

    /// <summary>A named set of workspaces. Persisted, not yet honored at run time.</summary>
    SpecificWorkspaces,

    /// <summary>A named set of documents. Persisted, not yet honored at run time.</summary>
    SpecificDocuments
}

/// <summary>
/// A named, user-defined agent: a saved set of instructions, a model, the skills it may use, what
/// it is allowed to read, and its guardrails (BRD-138 / REQ-UI-045).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BRD-83/84 described one anonymous agent with workspace-level toggles, so
/// there was no way to define an agent for a task. This is the persisted registry entry, invoked
/// from any workspace chat as <c>@Handle</c> (REQ-RAG-021).</para>
/// <para><b>Permission note:</b> <see cref="SelectedSkills"/> is a REQUEST, not a grant. What the
/// agent may actually call is the intersection of this selection with the workspace skill
/// catalogue, computed at run time by <see cref="AgentSkillResolver"/>. Nothing here can widen the
/// catalogue.</para>
/// <para><b>Dapper:</b> the settable properties map one-for-one onto the <c>WorkspaceAgent</c>
/// columns; <see cref="SelectedSkills"/> is loaded separately from <c>WorkspaceAgentSkill</c>.</para>
/// </remarks>
public sealed class AgentDefinition
{
    /// <summary>Gets or sets the surrogate key; 0 for an agent that has never been saved.</summary>
    public long WorkspaceAgentId { get; set; }

    /// <summary>Gets or sets the owning workspace identifier.</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the chat handle, stored without its leading '@' and lowercased.</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name shown in the agent list.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the one-line description shown under the handle.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the plain-language instructions used as the agent's system prompt.</summary>
    public string? Instructions { get; set; }

    /// <summary>Gets or sets the model override; null follows the workspace default.</summary>
    public string? Model { get; set; }

    /// <summary>Gets or sets what the agent is allowed to read.</summary>
    public AgentKnowledgeScope KnowledgeScope { get; set; } = AgentKnowledgeScope.CallingWorkspace;

    /// <summary>
    /// Gets or sets whether the agent follows the whole catalogue instead of a fixed selection.
    /// This is what "all enabled skills" means for the built-in agent: enabling a new catalogue
    /// skill reaches it immediately rather than leaving it on a stale snapshot.
    /// </summary>
    public bool UsesEveryEnabledSkill { get; set; }

    /// <summary>Gets or sets whether retrieval is restricted to pinned documents.</summary>
    public bool RestrictToPinned { get; set; }

    /// <summary>
    /// Gets or sets whether the agent may fall back on general knowledge when retrieval finds
    /// nothing. Off means an unanswerable question gets "the documents do not cover this".
    /// </summary>
    public bool AllowGeneralKnowledge { get; set; }

    /// <summary>Gets or sets the maximum tool calls the agent loop may make in one turn.</summary>
    public int MaxToolCalls { get; set; } = DefaultMaxToolCalls;

    /// <summary>Gets or sets the wall-clock limit for one run, in seconds.</summary>
    public int TimeLimitSeconds { get; set; } = DefaultTimeLimitSeconds;

    /// <summary>Gets or sets whether the execution trace is rendered in chat (REQ-UI-034).</summary>
    public bool ShowTrace { get; set; } = true;

    /// <summary>Gets or sets whether a skill that leaves the machine must be confirmed first.</summary>
    public bool ConfirmEgress { get; set; } = true;

    /// <summary>Gets or sets whether the agent may start follow-up runs on its own.</summary>
    public bool AllowFollowUp { get; set; }

    /// <summary>Gets or sets whether this is the built-in agent, which cannot be deleted.</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Gets or sets when the agent was created (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Gets or sets when the agent was last edited (UTC).</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Gets or sets when the agent last answered a turn (UTC); null when never used.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Gets the catalogue skills this agent asks for, subject to the catalogue.</summary>
    public HashSet<string> SelectedSkills { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shipped tool-call ceiling for one turn, matching the Agents screen design.</summary>
    public const int DefaultMaxToolCalls = 8;

    /// <summary>The shipped per-run time limit in seconds, matching the Agents screen design.</summary>
    public const int DefaultTimeLimitSeconds = 90;

    /// <summary>The handle of the built-in agent that is always available and never deletable.</summary>
    public const string BuiltInHandle = "agent";

    /// <summary>The shipped display name of the built-in agent.</summary>
    /// <remarks>
    /// <b>Invariant English on purpose, REQ-UI-055 / BRD-91.</b> <see cref="DisplayName"/> is a
    /// user-entered, persisted column: an operator types their own agent's name into it and it is
    /// written to <c>WorkspaceAgent</c> verbatim. Localizing the field would mean rewriting somebody's
    /// name for them. This constant is the seed the built-in row is created with and the value stored
    /// if it is ever saved, so it has to be one string, not one per culture — the reader-facing half
    /// is <see cref="BuiltInDisplayNameKey"/>, resolved only while the name is still the shipped one.
    /// </remarks>
    public const string BuiltInDisplayName = "General assistant";

    /// <summary>The shipped one-line description of the built-in agent. Invariant, as above.</summary>
    public const string BuiltInDescription = "The built-in default, always available";

    /// <summary>Resource key for the built-in agent's display name.</summary>
    public const string BuiltInDisplayNameKey = "AgentsBuiltInDisplayName";

    /// <summary>Resource key for the built-in agent's description.</summary>
    public const string BuiltInDescriptionKey = "AgentsBuiltInDescription";

    /// <summary>
    /// Gets whether this is the built-in agent still carrying its shipped name and description.
    /// </summary>
    /// <remarks>
    /// The precondition for rendering <see cref="BuiltInDisplayNameKey"/> instead of
    /// <see cref="DisplayName"/>. An operator who renamed <c>@agent</c> gets the name they typed back
    /// in every language; only the untouched shipped wording is translated, because only that wording
    /// is TechieDesk's to translate.
    /// </remarks>
    public bool HasShippedBuiltInWording =>
        IsBuiltIn
        && string.Equals(DisplayName, BuiltInDisplayName, StringComparison.Ordinal)
        && string.Equals(Description, BuiltInDescription, StringComparison.Ordinal);

    /// <summary>
    /// Creates the built-in <c>@agent</c> for a workspace: no stored row yet, follows the workspace
    /// model and the whole enabled catalogue.
    /// </summary>
    /// <param name="workspaceId">The workspace the agent belongs to.</param>
    /// <param name="createdAt">The timestamp to stamp it with.</param>
    /// <returns>An unsaved built-in agent definition.</returns>
    public static AgentDefinition BuiltIn(string workspaceId, DateTime createdAt) => new()
    {
        WorkspaceId = workspaceId,
        Handle = BuiltInHandle,
        DisplayName = BuiltInDisplayName,
        Description = BuiltInDescription,
        Model = null,
        KnowledgeScope = AgentKnowledgeScope.CallingWorkspace,
        UsesEveryEnabledSkill = true,
        AllowGeneralKnowledge = true,
        IsBuiltIn = true,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };

    /// <summary>Creates a deep copy, so an editor can be cancelled without mutating the list.</summary>
    /// <returns>An independent copy carrying the same values and skill selection.</returns>
    public AgentDefinition Copy()
    {
        var copy = new AgentDefinition
        {
            WorkspaceAgentId = WorkspaceAgentId,
            WorkspaceId = WorkspaceId,
            Handle = Handle,
            DisplayName = DisplayName,
            Description = Description,
            Instructions = Instructions,
            Model = Model,
            KnowledgeScope = KnowledgeScope,
            UsesEveryEnabledSkill = UsesEveryEnabledSkill,
            RestrictToPinned = RestrictToPinned,
            AllowGeneralKnowledge = AllowGeneralKnowledge,
            MaxToolCalls = MaxToolCalls,
            TimeLimitSeconds = TimeLimitSeconds,
            ShowTrace = ShowTrace,
            ConfirmEgress = ConfirmEgress,
            AllowFollowUp = AllowFollowUp,
            IsBuiltIn = IsBuiltIn,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            LastUsedAt = LastUsedAt
        };

        foreach (var skill in SelectedSkills)
        {
            copy.SelectedSkills.Add(skill);
        }

        return copy;
    }
}
