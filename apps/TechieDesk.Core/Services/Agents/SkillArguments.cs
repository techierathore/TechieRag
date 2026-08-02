using System.Globalization;
using System.Text.Json;

namespace TechieDesk.Services.Agents;

/// <summary>
/// Reads the tool-call argument payloads a model produces, for every skill (REQ-RAG-022).
/// </summary>
/// <remarks>
/// <para><b>A malformed payload is a bad tool call, not a crash.</b> Models emit truncated JSON,
/// numbers where strings belong and objects where arrays belong. Every reader here answers with
/// "absent" instead of throwing, so the skill can report what was missing into the execution trace
/// and the agent loop can retry — where an exception would end the turn.</para>
/// <para><b>One tested place.</b> Parsing lives here rather than in each skill, so the five new
/// skills cannot each invent their own slightly different tolerance for bad input.</para>
/// </remarks>
public static class SkillArguments
{
    /// <summary>
    /// Parses a payload into its root object.
    /// </summary>
    /// <param name="json">The raw JSON arguments the model produced.</param>
    /// <param name="document">The parsed document, which the caller must dispose, or null.</param>
    /// <returns>True when the payload parsed to a JSON object.</returns>
    public static bool TryParseObject(string? json, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                return false;
            }

            document = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a string property.
    /// </summary>
    /// <param name="json">The raw JSON arguments.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value, or an empty string when absent, null, non-string or unparseable.</returns>
    public static string ReadString(string? json, string property)
    {
        if (!TryParseObject(json, out var document))
        {
            return string.Empty;
        }

        using (document)
        {
            return document!.RootElement.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
    }

    /// <summary>
    /// Reads a whole-number property, clamped into the range the skill will accept.
    /// </summary>
    /// <param name="json">The raw JSON arguments.</param>
    /// <param name="property">The property to read.</param>
    /// <param name="fallback">The value used when the property is absent or unusable.</param>
    /// <param name="minimum">The smallest value the skill accepts.</param>
    /// <param name="maximum">The largest value the skill accepts.</param>
    /// <returns>The clamped value.</returns>
    /// <remarks>
    /// Clamping rather than rejecting is deliberate: a model asking for ten thousand rows has made
    /// a judgement error, not a malicious request, and the skill's own cap is the real protection.
    /// </remarks>
    public static int ReadInt(string? json, string property, int fallback, int minimum, int maximum)
    {
        if (!TryParseObject(json, out var document))
        {
            return Math.Clamp(fallback, minimum, maximum);
        }

        using (document)
        {
            var found = document!.RootElement.TryGetProperty(property, out var value);
            var raw = found switch
            {
                true when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) => number,
                true when value.ValueKind == JsonValueKind.String
                    && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var text) => text,
                _ => fallback
            };

            return Math.Clamp(raw, minimum, maximum);
        }
    }

    /// <summary>
    /// Reads an array of strings.
    /// </summary>
    /// <param name="json">The raw JSON arguments.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>
    /// The string entries in order. Non-string entries are rendered with their raw text so a model
    /// that labelled a chart with numbers still gets labels rather than an empty axis.
    /// </returns>
    public static IReadOnlyList<string> ReadStrings(string? json, string property)
    {
        if (!TryParseObject(json, out var document))
        {
            return [];
        }

        using (document)
        {
            if (!document!.RootElement.TryGetProperty(property, out var value)
                || value.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return value.EnumerateArray()
                .Select(entry => entry.ValueKind == JsonValueKind.String
                    ? entry.GetString() ?? string.Empty
                    : entry.ToString())
                .ToList();
        }
    }

    /// <summary>
    /// Reads an array of numbers, discarding anything that is not a finite number.
    /// </summary>
    /// <param name="json">The raw JSON arguments.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The finite numeric entries in order, or an empty list.</returns>
    /// <remarks>
    /// Infinities and NaN are dropped rather than plotted: an axis scaled to infinity renders a
    /// chart that is wrong in a way the reader cannot see.
    /// </remarks>
    public static IReadOnlyList<double> ReadNumbers(string? json, string property)
    {
        if (!TryParseObject(json, out var document))
        {
            return [];
        }

        using (document)
        {
            if (!document!.RootElement.TryGetProperty(property, out var value)
                || value.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return value.EnumerateArray()
                .Select(ToFiniteNumber)
                .Where(number => number.HasValue)
                .Select(number => number!.Value)
                .ToList();
        }
    }

    /// <summary>
    /// Reads a flat object of named values, used for SQL query parameters.
    /// </summary>
    /// <param name="json">The raw JSON arguments.</param>
    /// <param name="property">The property holding the object.</param>
    /// <returns>
    /// The name-to-value map. Values keep their JSON type (string, number, boolean or null); nested
    /// objects and arrays are skipped because no supported parameter type accepts them.
    /// </returns>
    public static IReadOnlyDictionary<string, object?> ReadValueMap(string? json, string property)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!TryParseObject(json, out var document))
        {
            return values;
        }

        using (document)
        {
            if (!document!.RootElement.TryGetProperty(property, out var map)
                || map.ValueKind != JsonValueKind.Object)
            {
                return values;
            }

            foreach (var entry in map.EnumerateObject())
            {
                if (TryReadScalar(entry.Value, out var scalar))
                {
                    values[entry.Name] = scalar;
                }
            }

            return values;
        }
    }

    /// <summary>Converts a JSON element to a finite double, or null when it is not one.</summary>
    /// <param name="element">The element to convert.</param>
    /// <returns>The finite value, or null.</returns>
    private static double? ToFiniteNumber(JsonElement element)
    {
        var parsed = element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) => text,
            _ => (double?)null
        };

        return parsed.HasValue && double.IsFinite(parsed.Value) ? parsed.Value : null;
    }

    /// <summary>Converts a JSON element to a scalar a database parameter can carry.</summary>
    /// <param name="element">The element to convert.</param>
    /// <param name="scalar">The converted value.</param>
    /// <returns>True when the element was a scalar.</returns>
    private static bool TryReadScalar(JsonElement element, out object? scalar)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                scalar = element.GetString();
                return true;
            case JsonValueKind.Number:
                scalar = element.TryGetInt64(out var whole) ? whole : element.GetDouble();
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                scalar = element.GetBoolean();
                return true;
            case JsonValueKind.Null:
                scalar = null;
                return true;
            default:
                scalar = null;
                return false;
        }
    }
}
