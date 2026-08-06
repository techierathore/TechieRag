using TechieDesk.Services.Agents;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 — which catalogue skills this build actually implements, and the argument parsing
/// behind them. A model can emit a malformed tool-call payload; that has to become a reportable bad
/// call in the trace, never an unhandled exception that kills the turn.
/// </summary>
public class WorkspaceSkillToolsTests
{
    /// <summary>The RAG-search skill binds to the catalogue name the toggles and resolver use.</summary>
    [Fact]
    public void RagSearchBindsToTheCatalogueName()
    {
        var implementation = WorkspaceSkillTools.RagSearch((_, _) => Task.FromResult("hits"));

        Assert.Equal(SkillCatalog.RagSearch, implementation.SkillName);
        Assert.Contains(SkillCatalog.RagSearch, WorkspaceSkillTools.ImplementedSkillNames);
    }

    /// <summary>
    /// THE TRIPWIRE (kept, not deleted — it was <c>OnlyRagSearchIsImplemented</c> while five skills
    /// had no tool behind them). It exists to keep the catalogue and the implementation list honest
    /// in BOTH directions: adding a catalogue entry with nothing behind it fails here, and so does
    /// an implementation whose name is not a catalogue skill. On 2026-07-30 the five missing skills
    /// were built, so the expected set is now the whole catalogue, in catalogue order.
    /// </summary>
    [Fact]
    public void EveryCatalogueSkillIsImplemented()
    {
        Assert.Equal(
            SkillCatalog.Skills.Select(skill => skill.Name),
            WorkspaceSkillTools.ImplementedSkillNames);
    }

    /// <summary>
    /// The implemented list is derived from the factories rather than hand-maintained, so a name
    /// cannot be added to it without a tool actually being built. Building the whole set against an
    /// unconfigured install still yields one implementation per catalogue skill.
    /// </summary>
    [Fact]
    public void TheImplementedListIsDerivedFromRealImplementations()
    {
        var built = WorkspaceSkillTools.All(
            (_, _) => Task.FromResult("hits"), WorkspaceSkillOptions.None);

        Assert.Equal(SkillCatalog.Skills.Count, built.Count);
        Assert.Equal(
            WorkspaceSkillTools.ImplementedSkillNames,
            built.Select(implementation => implementation.SkillName));
    }

    /// <summary>
    /// The five self-contained skills come from one factory, so a page wiring the agent loop cannot
    /// accidentally offer four of them.
    /// </summary>
    [Fact]
    public void StandardCoversEverySkillExceptRagSearch()
    {
        var standard = WorkspaceSkillTools.Standard(WorkspaceSkillOptions.None);

        Assert.Equal(
            SkillCatalog.Skills.Where(skill => skill.Name != SkillCatalog.RagSearch).Select(skill => skill.Name),
            standard.Select(implementation => implementation.SkillName));
    }

    /// <summary>
    /// On a stock install the four skills with an external dependency say why they cannot run
    /// instead of throwing, faking a result or returning an empty one that reads as a real answer.
    /// Chart generation needs nothing, so it is deliberately not in this list.
    /// </summary>
    [Theory]
    [InlineData(SkillCatalog.WebSearch, """{"query":"anything"}""")]
    [InlineData(SkillCatalog.WebScrape, """{"url":"https://example.com"}""")]
    [InlineData(SkillCatalog.SqlQuery, """{"sql":"SELECT 1"}""")]
    [InlineData(SkillCatalog.FileOperations, """{"operation":"list"}""")]
    public async Task AnUnconfiguredSkillDegradesToAnUnavailabilityReport(string skillName, string arguments)
    {
        var implementation = WorkspaceSkillTools.Standard(WorkspaceSkillOptions.None)
            .Single(candidate => candidate.SkillName == skillName);

        var result = await implementation.Invoke(arguments, CancellationToken.None);

        Assert.True(SkillUnavailable.IsUnavailable(result), result);
    }

    /// <summary>Chart generation has no external dependency, so it runs on any install.</summary>
    [Fact]
    public async Task ChartGenerationRunsWithNothingConfigured()
    {
        var implementation = WorkspaceSkillTools.Standard(WorkspaceSkillOptions.None)
            .Single(candidate => candidate.SkillName == SkillCatalog.ChartGenerate);

        var result = await implementation.Invoke(
            """{"labels":["a"],"values":[1]}""", CancellationToken.None);

        Assert.False(SkillUnavailable.IsUnavailable(result));
        Assert.StartsWith("<svg", result, StringComparison.Ordinal);
    }

    /// <summary>The query the model produced reaches the search delegate unchanged.</summary>
    [Fact]
    public async Task QueryReachesTheSearchDelegate()
    {
        string? seen = null;
        var implementation = WorkspaceSkillTools.RagSearch((query, _) =>
        {
            seen = query;
            return Task.FromResult("five chunks");
        });

        var result = await implementation.Invoke("""{"query":"Acme liability cap"}""", CancellationToken.None);

        Assert.Equal("Acme liability cap", seen);
        Assert.Equal("five chunks", result);
    }

    /// <summary>
    /// A tool call with no usable query does not reach the search at all, and says why — the agent
    /// loop can act on that, where a thrown exception would end the turn.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"query":""}""")]
    [InlineData("""{"query":null}""")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public async Task AMalformedCallIsReportedNotThrown(string argumentsJson)
    {
        var searched = false;
        var implementation = WorkspaceSkillTools.RagSearch((_, _) =>
        {
            searched = true;
            return Task.FromResult("hits");
        });

        var result = await implementation.Invoke(argumentsJson, CancellationToken.None);

        Assert.False(searched);
        Assert.Contains("No query supplied", result, StringComparison.Ordinal);
    }

    /// <summary>A non-string property is treated as absent rather than coerced into a query.</summary>
    [Fact]
    public void NonStringPropertyReadsAsAbsent()
    {
        Assert.Equal(string.Empty, WorkspaceSkillTools.ReadString("""{"query":42}""", "query"));
        Assert.Equal("caps", WorkspaceSkillTools.ReadString("""{"query":"caps"}""", "query"));
    }
}
