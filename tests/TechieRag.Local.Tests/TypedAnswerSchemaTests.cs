using System.Text.Json.Nodes;
using TechieRag.Local.Tests.Conformance;
using Xunit;

namespace TechieRag.Local.Tests;

/// <summary>
/// The schema <c>CompleteAsync&lt;T&gt;</c> constrains a local answer to is strict at every level
/// (REQ-RAG-063 / BRD-103).
/// </summary>
public sealed class TypedAnswerSchemaTests
{
    /// <summary>
    /// The nested test schema's root and nested object both require every property and allow no
    /// others, and the root is an object, never null — so a constrained model cannot flatten the
    /// nested object into its parent or answer null.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-063 TypedSchemaIsStrictAtEveryLevel")]
    public void TypedSchemaIsStrictAtEveryLevel()
    {
        var schema = JsonNode.Parse(TypedAnswerSchema.For(typeof(LocalLlmConformanceTests.PlaceAnswer)))!;
        var location = schema["properties"]!["Location"]!;

        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.Equal(["City", "Location"], schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(["Country", "Continent"], location["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(location["additionalProperties"]!.GetValue<bool>());
    }

    /// <summary>A schema's own required list is kept, and a property named "properties" is not mistaken for a keyword.</summary>
    [Fact]
    public void ExistingRequiredIsKept()
    {
        var schema = JsonNode.Parse("""{"type":"object","properties":{"properties":{"type":"string"},"b":{"type":"integer"}},"required":["b"]}""");

        TypedAnswerSchema.MakeStrict(schema);

        Assert.Equal(["b"], schema!["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Null(schema["properties"]!["properties"]!["required"]);
    }
}
