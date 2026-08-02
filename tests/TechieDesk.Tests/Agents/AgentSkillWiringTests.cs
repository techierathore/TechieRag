using Microsoft.Extensions.DependencyInjection;
using TechieDesk.Services.Agents;
using TechieDesk.Services.Web;
using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieDesk.Tests.Agents;

/// <summary>
/// REQ-RAG-022 — the wiring the chat page performs for an agent turn, tested apart from the page.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> <c>WorkspaceChat.razor</c> composes the turn's tool set from
/// <see cref="WorkspaceSkillTools.RagSearch"/> plus <see cref="WorkspaceSkillTools.Standard"/>, and
/// injects <see cref="IWebContentFetcherFactory"/> to do it. A Razor page cannot be unit tested
/// here, but both halves of what it depends on can: that the injected dependency actually resolves
/// from the registration the host calls, and that the composed set behaves. A missing DI
/// registration would otherwise surface only as a blank screen at run time.</para>
/// <para><b>The load-bearing test is <see cref="AStockInstallRegistersNoSkillThatLeavesTheMachine"/>.</b>
/// Wiring the web skills into the running loop is the moment REQ-NFR-008's zero-egress default is
/// most at risk, so it is asserted directly rather than reasoned about.</para>
/// </remarks>
public class AgentSkillWiringTests
{
    /// <summary>
    /// The fetcher the chat page injects resolves from the registration the host actually calls, so
    /// the page cannot fail to construct for want of a service nobody registered.
    /// </summary>
    [Fact]
    public void TheInjectedFetcherFactoryResolvesFromTheHostRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTechieDeskWebIngestion();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IWebContentFetcherFactory>();

        Assert.NotNull(factory.Create(blockPrivateNetworkTargets: true));
    }

    /// <summary>
    /// THE zero-egress guard for this wiring. A stock workspace catalogue permits only RAG search,
    /// so after the composition the running loop is handed exactly one tool and neither web skill is
    /// registered — the model is never shown them and no request can leave the machine without the
    /// owner having turned a catalogue toggle on first.
    /// </summary>
    [Fact]
    public void AStockInstallRegistersNoSkillThatLeavesTheMachine()
    {
        var registry = ComposeTurn(SkillCatalog.Defaults());

        Assert.Equal([SkillCatalog.RagSearch], registry.ToolDefinitions.Select(definition => definition.Name));
        Assert.DoesNotContain(registry.ToolDefinitions, definition => definition.Name == SkillCatalog.WebSearch);
        Assert.DoesNotContain(registry.ToolDefinitions, definition => definition.Name == SkillCatalog.WebScrape);
    }

    /// <summary>
    /// Every skill the catalogue permits reaches the loop. This is the regression the wiring was for:
    /// before it, the page spelled out RAG search alone and the other five were built but unreachable.
    /// </summary>
    [Fact]
    public void APermissiveCatalogueRegistersEverySkill()
    {
        var registry = ComposeTurn(
            SkillCatalog.Skills.ToDictionary(skill => skill.Name, _ => true, StringComparer.OrdinalIgnoreCase));

        Assert.Equal(
            SkillCatalog.Skills.Select(skill => skill.Name),
            registry.ToolDefinitions.Select(definition => definition.Name));
    }

    /// <summary>
    /// The skills the shipping page has no dependency for are registered and RUN, returning an
    /// honest unavailability through the registry the agent loop uses — not an exception, not an
    /// empty result that would read as a real answer.
    /// </summary>
    [Theory]
    [InlineData(SkillCatalog.WebSearch, """{"query":"anything"}""")]
    [InlineData(SkillCatalog.SqlQuery, """{"sql":"SELECT 1"}""")]
    [InlineData(SkillCatalog.FileOperations, """{"operation":"list"}""")]
    public async Task TheSkillsWithNoDependencyReportUnavailableThroughTheRegistry(
        string skillName, string arguments)
    {
        var registry = ComposeTurn(
            SkillCatalog.Skills.ToDictionary(skill => skill.Name, _ => true, StringComparer.OrdinalIgnoreCase));

        var result = await registry.ExecuteToolAsync(
            new ToolCall { Id = "1", Name = skillName, ArgumentsJson = arguments });

        Assert.True(result.IsSuccess);
        Assert.True(SkillUnavailable.IsUnavailable(result.Content), result.Content);
    }

    /// <summary>
    /// Chart generation needs nothing, so the shipping wiring makes it genuinely usable the moment
    /// the catalogue permits it.
    /// </summary>
    [Fact]
    public async Task ChartGenerationIsUsableWithTheShippingWiring()
    {
        var registry = ComposeTurn(
            SkillCatalog.Skills.ToDictionary(skill => skill.Name, _ => true, StringComparer.OrdinalIgnoreCase));

        var result = await registry.ExecuteToolAsync(new ToolCall
        {
            Id = "1",
            Name = SkillCatalog.ChartGenerate,
            ArgumentsJson = """{"labels":["Acme","Globex"],"values":[120.5,80]}"""
        });

        Assert.StartsWith("<svg", result.Content!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Composes an agent turn's tool set exactly as the chat page does: the catalogue narrowed to
    /// what the agent may call, RAG search bound to the turn, and the five standard skills built
    /// from the dependencies the shipping page can honestly supply.
    /// </summary>
    /// <param name="catalogue">The workspace catalogue to compose against.</param>
    /// <returns>The registry the agent loop would be handed.</returns>
    private static ToolRegistry ComposeTurn(Dictionary<string, bool> catalogue)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTechieDeskWebIngestion();
        using var provider = services.BuildServiceProvider();

        var options = new WorkspaceSkillOptions
        {
            WebFetcher = provider.GetRequiredService<IWebContentFetcherFactory>()
                .Create(blockPrivateNetworkTargets: true),
            WebSearch = null,
            SqlTarget = null,
            Files = null
        };

        return AgentToolPlanner.BuildRegistry(
            AgentSkillResolver.Permitted(catalogue, null, usesEveryEnabledSkill: true),
            [WorkspaceSkillTools.RagSearch((_, _) => Task.FromResult("hits")), .. WorkspaceSkillTools.Standard(options)]);
    }
}
