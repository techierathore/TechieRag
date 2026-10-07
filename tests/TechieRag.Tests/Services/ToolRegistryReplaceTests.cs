using TechieRag.Models;
using TechieRag.Services;
using Xunit;

namespace TechieRag.Tests.Services;

/// <summary>
/// Registering a tool name again replaces its definition as well as its handler (REQ-RAG-121 /
/// BRD-181, Sevak feedback TR-RAG-039).
/// </summary>
public sealed class ToolRegistryReplaceTests
{
    /// <summary>
    /// A developer registers the same tool name twice: the tool list sent to the model contains that
    /// name once, with the second description, and the second handler runs.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-121 SecondRegistrationReplacesDefinition")]
    public async Task SecondRegistrationReplacesDefinition()
    {
        var registry = new ToolRegistry();
        registry.Register("get_weather", "Old description", "{\"type\":\"object\"}", _ => "old");
        registry.Register("lookup", "Looks things up", "{\"type\":\"object\"}", _ => "lookup");
        registry.Register("get_weather", "New description", "{\"type\":\"object\",\"properties\":{}}", _ => "new");

        var weather = Assert.Single(registry.ToolDefinitions, d => d.Name == "get_weather");
        Assert.Equal("New description", weather.Description);
        Assert.Equal(["get_weather", "lookup"], registry.ToolDefinitions.Select(d => d.Name));

        var result = await registry.ExecuteToolAsync(new ToolCall { Id = "1", Name = "get_weather", ArgumentsJson = "{}" });
        Assert.Equal("new", result.Content);
    }

    /// <summary>Names match case-insensitively, as handler lookup always did, so a case variant is the same tool.</summary>
    [Fact(DisplayName = "REQ-RAG-121 CaseVariantReplacesDefinition")]
    public void CaseVariantReplacesDefinition()
    {
        var registry = new ToolRegistry();
        registry.Register("Search", "first", "{}", _ => "1");
        registry.Register("search", "second", "{}", _ => "2");

        var definition = Assert.Single(registry.ToolDefinitions);
        Assert.Equal("search", definition.Name);
    }
}
