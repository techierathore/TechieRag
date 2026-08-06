using TechieDesk.Services.Agents;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 — every skill is a library <c>ITool</c>, and the per-workspace toggles decide which
/// ones the agent loop is even offered. These tests assert the enforcement is by ABSENCE: a
/// forbidden skill is never registered, so it cannot be called by accident and there is no run-time
/// permission check to forget.
/// </summary>
public class AgentToolPlannerTests
{
    /// <summary>A permitted, implemented skill reaches the registry the agent loop is handed.</summary>
    [Fact]
    public void PermittedSkillIsRegistered()
    {
        var registry = AgentToolPlanner.BuildRegistry(
            [SkillCatalog.RagSearch], [Implementation(SkillCatalog.RagSearch, "hits")]);

        var definition = Assert.Single(registry.ToolDefinitions);
        Assert.Equal(SkillCatalog.RagSearch, definition.Name);
    }

    /// <summary>
    /// A skill outside the permitted set is not registered even though an implementation exists —
    /// the model is never shown the tool, so it cannot ask for it.
    /// </summary>
    [Fact]
    public void ForbiddenSkillIsNeverOfferedToTheModel()
    {
        var registry = AgentToolPlanner.BuildRegistry(
            [SkillCatalog.RagSearch],
            [Implementation(SkillCatalog.RagSearch, "hits"), Implementation(SkillCatalog.WebSearch, "results")]);

        Assert.Equal([SkillCatalog.RagSearch], registry.ToolDefinitions.Select(d => d.Name));
    }

    /// <summary>
    /// Even if the model somehow names an unregistered tool, the library registry refuses it rather
    /// than executing something. The absence is the primary defence; this is the backstop behind it.
    /// </summary>
    [Fact]
    public async Task CallingAnUnregisteredToolFails()
    {
        var registry = AgentToolPlanner.BuildRegistry(
            [SkillCatalog.RagSearch], [Implementation(SkillCatalog.WebSearch, "results")]);

        var result = await registry.ExecuteToolAsync(
            new ToolCall { Id = "1", Name = SkillCatalog.WebSearch, ArgumentsJson = "{}" });

        Assert.False(result.IsSuccess);
        Assert.Contains("not registered", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A permitted skill with no implementation on this install simply does not appear. It is never
    /// stubbed with plausible-looking text, which would be indistinguishable from a real answer.
    /// </summary>
    [Fact]
    public void PermittedSkillWithNoImplementationIsAbsent()
    {
        var registry = AgentToolPlanner.BuildRegistry(
            [SkillCatalog.RagSearch, SkillCatalog.SqlQuery], [Implementation(SkillCatalog.RagSearch, "hits")]);

        Assert.Equal([SkillCatalog.RagSearch], registry.ToolDefinitions.Select(d => d.Name));
    }

    /// <summary>An empty permitted set yields a registry with no tools at all.</summary>
    [Fact]
    public void NoPermittedSkillsYieldsNoTools()
    {
        var registry = AgentToolPlanner.BuildRegistry([], [Implementation(SkillCatalog.RagSearch, "hits")]);

        Assert.Empty(registry.ToolDefinitions);
    }

    /// <summary>A registered skill actually runs its implementation when the model calls it.</summary>
    [Fact]
    public async Task RegisteredSkillExecutesItsImplementation()
    {
        var registry = AgentToolPlanner.BuildRegistry(
            [SkillCatalog.RagSearch], [Implementation(SkillCatalog.RagSearch, "five chunks")]);

        var result = await registry.ExecuteToolAsync(
            new ToolCall { Id = "1", Name = SkillCatalog.RagSearch, ArgumentsJson = """{"query":"caps"}""" });

        Assert.True(result.IsSuccess);
        Assert.Equal("five chunks", result.Content);
    }

    /// <summary>
    /// Only implementations that name a real catalogue skill are reported as implemented, so a
    /// typo'd name cannot make the editor claim a skill is available.
    /// </summary>
    [Fact]
    public void ImplementedNamesIgnoreAnythingOutsideTheCatalogue()
    {
        var names = AgentToolPlanner.ImplementedNames(
            [Implementation(SkillCatalog.RagSearch, "hits"), Implementation("rag_search", "typo")]);

        Assert.Equal([SkillCatalog.RagSearch], names);
    }

    /// <summary>Builds a skill implementation returning a fixed result.</summary>
    /// <param name="skillName">The catalogue skill name.</param>
    /// <param name="result">What the tool returns.</param>
    /// <returns>The implementation.</returns>
    private static SkillImplementation Implementation(string skillName, string result) =>
        new(skillName, $"Test tool for {skillName}",
            """{"type":"object","properties":{"query":{"type":"string"}}}""",
            (_, _) => Task.FromResult<SkillOutcome>(result));
}
