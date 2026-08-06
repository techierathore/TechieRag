namespace TechieDesk.Services.Agents;

/// <summary>
/// Why a skill is or is not offered to a given agent, as the agent editor renders it.
/// </summary>
public enum AgentSkillAvailability
{
    /// <summary>Permitted by the catalogue, selected by the agent, and implemented — it runs.</summary>
    Enabled,

    /// <summary>Permitted by the catalogue but this agent has not selected it.</summary>
    Disabled,

    /// <summary>Forbidden by the workspace catalogue. The agent cannot turn it on.</summary>
    Blocked,

    /// <summary>Permitted and selected, but no implementation exists on this install yet.</summary>
    Unavailable
}

/// <summary>
/// One row of the agent editor's Skills tab: the catalogue entry plus the three independent facts
/// that decide whether it runs.
/// </summary>
/// <param name="Skill">The catalogue definition.</param>
/// <param name="CatalogueEnabled">Whether the workspace catalogue permits it (the outer boundary).</param>
/// <param name="AgentSelected">Whether this agent asks for it.</param>
/// <param name="IsImplemented">Whether this install can actually execute it.</param>
public sealed record AgentSkillState(
    SkillDefinition Skill,
    bool CatalogueEnabled,
    bool AgentSelected,
    bool IsImplemented)
{
    /// <summary>Gets how the editor should render this row.</summary>
    public AgentSkillAvailability Availability =>
        !CatalogueEnabled ? AgentSkillAvailability.Blocked
        : !AgentSelected ? AgentSkillAvailability.Disabled
        : IsImplemented ? AgentSkillAvailability.Enabled
        : AgentSkillAvailability.Unavailable;

    /// <summary>
    /// Gets whether the skill is inside the agent's permitted set. Deliberately independent of
    /// <see cref="IsImplemented"/>: permission and availability are different questions, and
    /// conflating them would make a missing implementation look like a denied permission.
    /// </summary>
    public bool IsPermitted => CatalogueEnabled && AgentSelected;

    /// <summary>Gets whether the agent's own toggle may be changed, or is overruled by the catalogue.</summary>
    public bool IsAgentToggleEditable => CatalogueEnabled;
}

/// <summary>
/// Computes what an agent may actually call, as the intersection of the workspace skill catalogue
/// and the agent's own selection (BRD-84 + BRD-138 / REQ-RAG-022 + REQ-UI-045).
/// </summary>
/// <remarks>
/// <para><b>The rule this type exists to enforce:</b> the workspace catalogue is the OUTER boundary.
/// An agent selects from within it and can never widen it. Turning a catalogue skill off must take
/// effect for every agent immediately.</para>
/// <para><b>Why it is computed, never stored:</b> a saved copy of the effective set taken when the
/// agent was written would keep granting a skill the catalogue has since revoked. Every caller
/// therefore asks for the intersection at the moment of the turn, against the catalogue as it is
/// right now.</para>
/// </remarks>
public static class AgentSkillResolver
{
    /// <summary>
    /// Resolves the skills an agent is permitted to call right now.
    /// </summary>
    /// <param name="catalogue">The workspace catalogue: skill name to enabled.</param>
    /// <param name="agent">The agent whose selection is being narrowed by the catalogue.</param>
    /// <returns>The permitted skill names, in catalogue order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public static IReadOnlyList<string> Permitted(
        IReadOnlyDictionary<string, bool> catalogue, AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(agent);

        return Permitted(catalogue, agent.SelectedSkills, agent.UsesEveryEnabledSkill);
    }

    /// <summary>
    /// Resolves the permitted set from a raw selection, for callers that do not have a full
    /// <see cref="AgentDefinition"/> (the editor, previewing an unsaved change).
    /// </summary>
    /// <param name="catalogue">The workspace catalogue: skill name to enabled.</param>
    /// <param name="selected">The skills the agent asks for.</param>
    /// <param name="usesEveryEnabledSkill">
    /// True when the agent follows the whole catalogue rather than <paramref name="selected"/>.
    /// </param>
    /// <returns>The permitted skill names, in catalogue order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="catalogue"/> is null.</exception>
    public static IReadOnlyList<string> Permitted(
        IReadOnlyDictionary<string, bool> catalogue,
        IReadOnlyCollection<string>? selected,
        bool usesEveryEnabledSkill)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var asked = new HashSet<string>(selected ?? [], StringComparer.OrdinalIgnoreCase);

        return SkillCatalog.Skills
            .Where(skill => IsCatalogueEnabled(catalogue, skill))
            .Where(skill => usesEveryEnabledSkill || asked.Contains(skill.Name))
            .Select(skill => skill.Name)
            .ToList();
    }

    /// <summary>
    /// Describes every catalogue skill for the agent editor, so a skill the catalogue forbids is
    /// rendered greyed and marked <c>Blocked</c> rather than hidden — the reason stays legible.
    /// </summary>
    /// <param name="catalogue">The workspace catalogue: skill name to enabled.</param>
    /// <param name="selected">The skills the agent asks for.</param>
    /// <param name="usesEveryEnabledSkill">True when the agent follows the whole catalogue.</param>
    /// <param name="implemented">The skill names this install can actually execute.</param>
    /// <returns>One state per catalogue entry, in catalogue order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="catalogue"/> is null.</exception>
    public static IReadOnlyList<AgentSkillState> Describe(
        IReadOnlyDictionary<string, bool> catalogue,
        IReadOnlyCollection<string>? selected,
        bool usesEveryEnabledSkill,
        IReadOnlyCollection<string>? implemented)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var asked = new HashSet<string>(selected ?? [], StringComparer.OrdinalIgnoreCase);
        var runnable = new HashSet<string>(implemented ?? [], StringComparer.OrdinalIgnoreCase);

        return SkillCatalog.Skills
            .Select(skill =>
            {
                var enabled = IsCatalogueEnabled(catalogue, skill);
                return new AgentSkillState(
                    skill,
                    enabled,
                    AgentSelected: usesEveryEnabledSkill ? enabled : asked.Contains(skill.Name),
                    IsImplemented: runnable.Contains(skill.Name));
            })
            .ToList();
    }

    /// <summary>
    /// Reads a catalogue entry, falling back to the skill's shipped default when the workspace has
    /// never toggled it — an untouched workspace still behaves rather than having no skills at all.
    /// </summary>
    /// <param name="catalogue">The workspace catalogue.</param>
    /// <param name="skill">The catalogue definition.</param>
    /// <returns>True when the workspace permits the skill.</returns>
    private static bool IsCatalogueEnabled(IReadOnlyDictionary<string, bool> catalogue, SkillDefinition skill) =>
        catalogue.TryGetValue(skill.Name, out var enabled) ? enabled : skill.DefaultEnabled;
}
