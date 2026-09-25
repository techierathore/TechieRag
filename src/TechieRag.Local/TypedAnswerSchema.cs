using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace TechieRag.Local;

/// <summary>
/// The JSON schema <c>CompleteAsync&lt;T&gt;</c> constrains a local answer to (REQ-RAG-063 / BRD-103).
/// </summary>
/// <remarks>
/// Strict, like OpenAI's structured outputs: the root is never <c>null</c>, and every object lists all
/// its properties as required and allows no others. Measured 2026-09-25 with Qwen2.5 0.5B: under the
/// exporter's lenient schema a constrained decoder let the model flatten a nested object into its
/// parent (<c>{"City":"Paris","Country":"France"}</c> for a <c>Location</c> object), which parses into
/// an empty <c>Location</c>; the strict schema makes that answer impossible.
/// </remarks>
internal static class TypedAnswerSchema
{
    private static readonly JsonSchemaExporterOptions ExporterOptions = new() { TreatNullObliviousAsNonNullable = true };

    /// <summary>Builds the strict schema of a type.</summary>
    /// <param name="type">The answer type.</param>
    /// <returns>The schema as compact JSON.</returns>
    public static string For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(JsonSerializerOptions.Default, type, ExporterOptions);
        MakeStrict(schema);
        return schema.ToJsonString();
    }

    /// <summary>Marks every object in a schema as having all its properties required and no others.</summary>
    /// <param name="node">A schema node.</param>
    internal static void MakeStrict(JsonNode? node)
    {
        if (node is not JsonObject schema)
        {
            return;
        }

        if (schema["properties"] is JsonObject properties)
        {
            schema["required"] ??= new JsonArray(properties.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray());
            schema["additionalProperties"] ??= false;
            SubSchemas(properties).ForEach(MakeStrict);
        }

        MakeStrict(schema["items"]);
        foreach (var keyword in (string[])["anyOf", "oneOf", "allOf", "prefixItems"])
        {
            if (schema[keyword] is JsonArray alternatives)
            {
                alternatives.ToList().ForEach(MakeStrict);
            }
        }

        foreach (var keyword in (string[])["$defs", "definitions"])
        {
            if (schema[keyword] is JsonObject definitions)
            {
                SubSchemas(definitions).ForEach(MakeStrict);
            }
        }
    }

    private static List<JsonNode?> SubSchemas(JsonObject map) => map.Select(p => p.Value).ToList();
}
