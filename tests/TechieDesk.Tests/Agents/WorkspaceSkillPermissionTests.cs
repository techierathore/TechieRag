using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 — the two-level permission rule applied to the WHOLE catalogue, now that all six
/// skills have a tool behind them. Implementing the five missing skills must not have made any of
/// them reachable without the workspace catalogue saying so: the catalogue is still the outer
/// boundary, and the agent selects only from inside it.
/// </summary>
public class WorkspaceSkillPermissionTests
{
    /// <summary>
    /// A stock workspace permits only RAG search. Every skill that leaves the machine or reaches
    /// past the workspace stays off until the owner turns it on, which is what keeps the new web
    /// skills compatible with the REQ-NFR-008 zero-egress default.
    /// </summary>
    [Fact]
    public void AStockWorkspacePermitsOnlyRagSearch()
    {
        var permitted = AgentSkillResolver.Permitted(
            SkillCatalog.Defaults(), selected: null, usesEveryEnabledSkill: true);

        Assert.Equal([SkillCatalog.RagSearch], permitted);
    }

    /// <summary>
    /// With the catalogue turned fully on and an agent that follows it, every skill is registered —
    /// the intersection is complete, so no implemented skill is silently dropped.
    /// </summary>
    [Fact]
    public void EverySkillIsRegisteredWhenTheCatalogueAllowsThemAll()
    {
        var registry = AgentToolPlanner.BuildRegistry(
            AgentSkillResolver.Permitted(AllOn(), null, usesEveryEnabledSkill: true),
            WorkspaceSkillTools.All((_, _) => Task.FromResult("hits"), WorkspaceSkillOptions.None));

        Assert.Equal(
            SkillCatalog.Skills.Select(skill => skill.Name),
            registry.ToolDefinitions.Select(definition => definition.Name));
    }

    /// <summary>
    /// THE rule, checked one skill at a time: a catalogue that forbids a skill overrides an agent
    /// that asks for it, so the tool is never even offered to the model.
    /// </summary>
    [Theory]
    [InlineData(SkillCatalog.WebSearch)]
    [InlineData(SkillCatalog.WebScrape)]
    [InlineData(SkillCatalog.SqlQuery)]
    [InlineData(SkillCatalog.ChartGenerate)]
    [InlineData(SkillCatalog.FileOperations)]
    public void TheCatalogueOverridesAnAgentThatAsksForASkill(string skillName)
    {
        var catalogue = AllOn();
        catalogue[skillName] = false;

        var registry = AgentToolPlanner.BuildRegistry(
            AgentSkillResolver.Permitted(catalogue, [skillName], usesEveryEnabledSkill: false),
            WorkspaceSkillTools.All((_, _) => Task.FromResult("hits"), WorkspaceSkillOptions.None));

        Assert.Empty(registry.ToolDefinitions);
    }

    /// <summary>
    /// A skill the catalogue forbids renders as Blocked in the agent editor rather than as merely
    /// unselected, so the owner can see the reason instead of guessing at it.
    /// </summary>
    [Fact]
    public void AForbiddenSkillRendersAsBlockedNotDisabled()
    {
        var catalogue = AllOn();
        catalogue[SkillCatalog.FileOperations] = false;

        var states = AgentSkillResolver.Describe(
            catalogue, [SkillCatalog.FileOperations], false, WorkspaceSkillTools.ImplementedSkillNames);

        var row = states.Single(state => state.Skill.Name == SkillCatalog.FileOperations);
        Assert.Equal(AgentSkillAvailability.Blocked, row.Availability);
        Assert.False(row.IsAgentToggleEditable);
    }

    /// <summary>
    /// Now that every catalogue skill is implemented, no row can render Unavailable on the basis of
    /// a missing implementation — the editor's honest "not built yet" state applies to nothing in
    /// this build. The path itself is still exercised below, because it must keep working if a
    /// future catalogue entry lands ahead of its tool.
    /// </summary>
    [Fact]
    public void NoRowIsUnavailableInThisBuild()
    {
        var states = AgentSkillResolver.Describe(
            AllOn(), null, true, WorkspaceSkillTools.ImplementedSkillNames);

        Assert.DoesNotContain(states, state => state.Availability == AgentSkillAvailability.Unavailable);
    }

    /// <summary>
    /// The regression this REQ's tests were written around: a permitted skill with no implementation
    /// is reported UNAVAILABLE, not Blocked. Permission and availability are different questions,
    /// and collapsing them would make a missing tool look like a denied permission.
    /// </summary>
    [Fact]
    public void APermittedButUnimplementedSkillIsUnavailableNotBlocked()
    {
        var states = AgentSkillResolver.Describe(
            AllOn(), null, true, [SkillCatalog.RagSearch]);

        var row = states.Single(state => state.Skill.Name == SkillCatalog.SqlQuery);
        Assert.Equal(AgentSkillAvailability.Unavailable, row.Availability);
        Assert.True(row.IsPermitted);
    }

    /// <summary>Builds a catalogue with every skill permitted.</summary>
    /// <returns>The skill-name to enabled map.</returns>
    private static Dictionary<string, bool> AllOn() =>
        SkillCatalog.Skills.ToDictionary(skill => skill.Name, _ => true, StringComparer.OrdinalIgnoreCase);
}
