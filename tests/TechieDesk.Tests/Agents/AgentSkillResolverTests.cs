using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 + REQ-UI-045 — the two-level permission model. The workspace skill catalogue is the
/// OUTER boundary; an agent selects from within it and can never widen it. The checklist calls this
/// "the hard part", and these tests are what hold it: they assert the effective set is an
/// intersection computed at run time, not a copy taken when the agent was saved.
/// </summary>
public class AgentSkillResolverTests
{
    /// <summary>
    /// A skill both the catalogue and the agent permit is in the permitted set — the baseline the
    /// other cases are measured against.
    /// </summary>
    [Fact]
    public void CatalogueAndAgentAgreeingPermitsTheSkill()
    {
        var agent = AgentWith(SkillCatalog.RagSearch, SkillCatalog.WebSearch);
        var catalogue = Catalogue((SkillCatalog.RagSearch, true), (SkillCatalog.WebSearch, true));

        var permitted = AgentSkillResolver.Permitted(catalogue, agent);

        Assert.Equal([SkillCatalog.RagSearch, SkillCatalog.WebSearch], permitted);
    }

    /// <summary>
    /// The catalogue overrides the agent. An agent that has a skill selected still does NOT get it
    /// when the workspace catalogue forbids it — the whole point of the outer boundary.
    /// </summary>
    [Fact]
    public void CatalogueOverridesTheAgentsOwnSelection()
    {
        var agent = AgentWith(SkillCatalog.RagSearch, SkillCatalog.SqlQuery);
        var catalogue = Catalogue((SkillCatalog.RagSearch, true), (SkillCatalog.SqlQuery, false));

        var permitted = AgentSkillResolver.Permitted(catalogue, agent);

        Assert.Equal([SkillCatalog.RagSearch], permitted);
        Assert.DoesNotContain(SkillCatalog.SqlQuery, permitted);
    }

    /// <summary>
    /// Revoking a catalogue skill takes effect on the very next resolution for an unchanged, unsaved
    /// agent. This is why the effective set is computed rather than stored: a snapshot taken at save
    /// time would keep granting the revoked skill.
    /// </summary>
    [Fact]
    public void RevokingACatalogueSkillTakesEffectImmediately()
    {
        var agent = AgentWith(SkillCatalog.RagSearch, SkillCatalog.WebSearch);

        var before = AgentSkillResolver.Permitted(
            Catalogue((SkillCatalog.RagSearch, true), (SkillCatalog.WebSearch, true)), agent);
        var after = AgentSkillResolver.Permitted(
            Catalogue((SkillCatalog.RagSearch, true), (SkillCatalog.WebSearch, false)), agent);

        Assert.Contains(SkillCatalog.WebSearch, before);
        Assert.DoesNotContain(SkillCatalog.WebSearch, after);
        Assert.Contains(SkillCatalog.WebSearch, agent.SelectedSkills);
    }

    /// <summary>
    /// The catalogue permitting a skill does not by itself grant it: an agent that has not selected
    /// it still cannot call it. Both levels must agree.
    /// </summary>
    [Fact]
    public void CataloguePermissionAloneDoesNotGrantTheSkill()
    {
        var agent = AgentWith(SkillCatalog.RagSearch);
        var catalogue = Catalogue((SkillCatalog.RagSearch, true), (SkillCatalog.ChartGenerate, true));

        Assert.DoesNotContain(SkillCatalog.ChartGenerate, AgentSkillResolver.Permitted(catalogue, agent));
    }

    /// <summary>
    /// The built-in agent means "all enabled skills": it follows the catalogue as the catalogue
    /// changes, so a newly enabled skill reaches it without the agent being re-saved.
    /// </summary>
    [Fact]
    public void FollowEveryEnabledSkillTracksTheCatalogue()
    {
        var builtIn = AgentDefinition.BuiltIn("ws-1", DateTime.UtcNow);

        var before = AgentSkillResolver.Permitted(
            Catalogue((SkillCatalog.RagSearch, true), (SkillCatalog.ChartGenerate, false)), builtIn);
        var after = AgentSkillResolver.Permitted(
            Catalogue((SkillCatalog.RagSearch, true), (SkillCatalog.ChartGenerate, true)), builtIn);

        Assert.DoesNotContain(SkillCatalog.ChartGenerate, before);
        Assert.Contains(SkillCatalog.ChartGenerate, after);
        Assert.Empty(builtIn.SelectedSkills);
    }

    /// <summary>
    /// A workspace that has never been configured falls back to the shipped defaults, so RAG search
    /// still works and nothing that leaves the machine is on.
    /// </summary>
    [Fact]
    public void UntouchedWorkspaceUsesTheShippedDefaults()
    {
        var builtIn = AgentDefinition.BuiltIn("ws-1", DateTime.UtcNow);

        var permitted = AgentSkillResolver.Permitted(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase), builtIn);

        Assert.Equal([SkillCatalog.RagSearch], permitted);
    }

    /// <summary>
    /// Every skill that can reach outside the workspace ships off, per the Agents screen design —
    /// they only run when the owner turns them on deliberately.
    /// </summary>
    [Fact]
    public void EverySkillThatLeavesTheWorkspaceShipsOff()
    {
        var risky = SkillCatalog.Skills.Where(s => s.Exposure != SkillExposure.Local);

        Assert.All(risky, skill => Assert.False(skill.DefaultEnabled));
    }

    /// <summary>
    /// A skill the catalogue forbids is described as <c>Blocked</c> rather than omitted, so the
    /// editor can grey it with the reason visible instead of hiding it and leaving the user
    /// wondering where it went.
    /// </summary>
    [Fact]
    public void ForbiddenSkillIsDescribedAsBlockedNotHidden()
    {
        var catalogue = Catalogue((SkillCatalog.RagSearch, true), (SkillCatalog.SqlQuery, false));

        var states = AgentSkillResolver.Describe(
            catalogue, [SkillCatalog.RagSearch, SkillCatalog.SqlQuery], false, [SkillCatalog.RagSearch]);

        Assert.Equal(SkillCatalog.Skills.Count, states.Count);

        var blocked = states.Single(s => s.Skill.Name == SkillCatalog.SqlQuery);
        Assert.Equal(AgentSkillAvailability.Blocked, blocked.Availability);
        Assert.False(blocked.IsPermitted);
        Assert.False(blocked.IsAgentToggleEditable);
    }

    /// <summary>
    /// A permitted skill with no implementation on this install reports <c>Unavailable</c>, not
    /// <c>Blocked</c> — a missing implementation is a different fact from a denied permission and
    /// must not be reported as one.
    /// </summary>
    [Fact]
    public void PermittedButUnimplementedSkillIsUnavailableNotBlocked()
    {
        var catalogue = Catalogue((SkillCatalog.RagSearch, true), (SkillCatalog.ChartGenerate, true));

        var states = AgentSkillResolver.Describe(
            catalogue, [SkillCatalog.RagSearch, SkillCatalog.ChartGenerate], false, [SkillCatalog.RagSearch]);

        var chart = states.Single(s => s.Skill.Name == SkillCatalog.ChartGenerate);
        Assert.Equal(AgentSkillAvailability.Unavailable, chart.Availability);
        Assert.True(chart.IsPermitted);
        Assert.True(chart.IsAgentToggleEditable);

        var rag = states.Single(s => s.Skill.Name == SkillCatalog.RagSearch);
        Assert.Equal(AgentSkillAvailability.Enabled, rag.Availability);
    }

    /// <summary>
    /// A catalogue-permitted skill the agent has simply not selected reads as <c>Disabled</c>, and
    /// its toggle stays editable — nothing about the catalogue is stopping the user turning it on.
    /// </summary>
    [Fact]
    public void UnselectedButPermittedSkillIsDisabledAndEditable()
    {
        var catalogue = Catalogue((SkillCatalog.RagSearch, true), (SkillCatalog.WebSearch, true));

        var states = AgentSkillResolver.Describe(catalogue, [SkillCatalog.RagSearch], false, []);

        var web = states.Single(s => s.Skill.Name == SkillCatalog.WebSearch);
        Assert.Equal(AgentSkillAvailability.Disabled, web.Availability);
        Assert.True(web.IsAgentToggleEditable);
    }

    /// <summary>Builds a catalogue over the shipped defaults.</summary>
    /// <param name="overrides">The toggles to apply.</param>
    /// <returns>A full catalogue map.</returns>
    private static Dictionary<string, bool> Catalogue(params (string Name, bool Enabled)[] overrides)
    {
        var catalogue = SkillCatalog.Defaults();
        foreach (var (name, enabled) in overrides)
        {
            catalogue[name] = enabled;
        }
        return catalogue;
    }

    /// <summary>Builds an agent that has selected the given skills.</summary>
    /// <param name="skills">The skills the agent asks for.</param>
    /// <returns>An agent definition.</returns>
    private static AgentDefinition AgentWith(params string[] skills)
    {
        var agent = new AgentDefinition
        {
            WorkspaceId = "ws-1",
            Handle = "analyst",
            DisplayName = "Contract Analyst"
        };
        foreach (var skill in skills)
        {
            agent.SelectedSkills.Add(skill);
        }
        return agent;
    }
}
