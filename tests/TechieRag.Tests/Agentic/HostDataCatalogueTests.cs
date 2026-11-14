using TechieRag.Agentic.HostData;
using TechieRag.Models;
using Xunit;
using Xunit.Abstractions;

namespace TechieRag.Tests.Agentic;

/// <summary>
/// The host-data skill describes each entity once and lets a host pick the entities for a turn
/// (REQ-RAG-160, Sevak TR-RAG-061).
/// </summary>
public sealed class HostDataCatalogueTests(ITestOutputHelper output)
{
    /// <summary>
    /// Sevak's setup: fifteen entities with 2 to 9 fields. Every field's description appears exactly once across
    /// the five tool definitions, where it appeared five times before.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-160 FifteenEntitiesAreDescribedOnce")]
    public void FifteenEntitiesAreDescribedOnce()
    {
        var skill = new HostDataSkill();
        foreach (var entity in SevakEntities.All())
        {
            skill.Register(entity);
        }

        var tools = skill.ToolDefinitions;
        var allText = string.Concat(tools.Select(tool => tool.Description + "\n" + tool.ParametersSchema));

        Assert.Equal(5, tools.Count);
        foreach (var entity in SevakEntities.All())
        {
            Assert.Equal(1, Occurrences(allText, "- " + entity.Name + ": " + entity.Description));
        }

        // Some entities share a field text ("The project it belongs to, by name."): each appears once per entity that has it.
        var fieldTexts = SevakEntities.All().SelectMany(entity => entity.Fields).GroupBy(field => field.Description);
        foreach (var group in fieldTexts)
        {
            Assert.Equal(group.Count(), Occurrences(allText, ") " + group.Key));
        }

        Assert.All(tools.Skip(1), tool => Assert.Contains("listed on host_data_list", tool.Description, StringComparison.Ordinal));
    }

    /// <summary>
    /// The five definitions for fifteen entities stay small: 23,527 characters when every tool repeated every entity
    /// (TechieRag 1.1.9), about 7,100 now; the bound is about half the old size.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-160 FifteenEntityToolSetIsSmall")]
    public void FifteenEntityToolSetIsSmall()
    {
        var skill = new HostDataSkill();
        foreach (var entity in SevakEntities.All())
        {
            skill.Register(entity);
        }

        var characters = skill.ToolDefinitions.Sum(tool => tool.Name.Length + tool.Description.Length + tool.ParametersSchema.Length);

        output.WriteLine($"fifteen-entity tool set: {characters} characters");
        Assert.True(characters < 12_000, $"the tool set is {characters} characters");
    }

    /// <summary>
    /// A host offers only the entities a turn needs: the tools name only those, the catalogue describes only those,
    /// a call naming another is refused with its code, and null offers all of them again.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-160 HostPicksTheEntitiesForATurn")]
    public async Task HostPicksTheEntitiesForATurn()
    {
        var skill = new HostDataSkill();
        foreach (var entity in SevakEntities.All())
        {
            skill.Register(entity);
        }

        skill.BeginTurn("turn-1", ["task", "project"]);

        Assert.Equal(["task", "project"], skill.OfferedEntities.Select(entity => entity.Name));
        var list = skill.ToolDefinitions[0];
        Assert.Contains("\"task\"", list.ParametersSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("\"person\"", list.ParametersSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("Someone the user knows.", list.Description, StringComparison.Ordinal);

        var refused = await skill.ExecuteToolAsync(new ToolCall { Id = "c1", Name = "host_data_list", ArgumentsJson = """{"entity":"person"}""" });
        Assert.False(refused.IsSuccess);
        Assert.Equal(HostDataMessageCodes.EntityNotOffered, refused.Message!.Code);

        var allowed = await skill.ExecuteToolAsync(new ToolCall { Id = "c2", Name = "host_data_list", ArgumentsJson = """{"entity":"task"}""" });
        Assert.True(allowed.IsSuccess);

        skill.OfferEntities(null);
        Assert.Equal(SevakEntities.Count, skill.OfferedEntities.Count);
        Assert.Contains("\"person\"", skill.ToolDefinitions[0].ParametersSchema, StringComparison.Ordinal);
    }

    /// <summary>Offering a name that is not registered is refused at once, so a typo cannot hide every entity.</summary>
    [Fact(DisplayName = "REQ-RAG-160 OfferingAnUnknownEntityThrows")]
    public void OfferingAnUnknownEntityThrows()
    {
        var skill = new HostDataSkill();
        skill.Register(SevakEntities.All()[0]);

        Assert.Throws<ArgumentException>(() => skill.OfferEntities(["tasks"]));
        Assert.Single(skill.OfferedEntities);
    }

    /// <summary>An entity that offers only some operations says which, in its one description.</summary>
    [Fact(DisplayName = "REQ-RAG-160 PartialEntityNamesItsOperations")]
    public void PartialEntityNamesItsOperations()
    {
        var skill = new HostDataSkill()
            .Register(SevakEntities.All()[0])
            .Register(new HostEntity("weather", "Today's weather.", [new HostEntityField("city", HostFieldType.Text, "The city.")])
            {
                List = (_, _) => Task.FromResult<IReadOnlyList<HostEntityRecord>>([])
            });

        var catalogue = skill.ToolDefinitions[0].Description;

        Assert.Contains("- weather: Today's weather. Can: list.", catalogue, StringComparison.Ordinal);
        Assert.DoesNotContain("Can:", catalogue.Split('\n')[1], StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string part)
    {
        var count = 0;
        for (var index = text.IndexOf(part, StringComparison.Ordinal); index >= 0; index = text.IndexOf(part, index + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
