using System.Text.Json;

namespace TechieRag.Agentic.HostData;

/// <summary>One record of a host entity: its id and its field values (REQ-RAG-153 / BRD-211).</summary>
/// <param name="Id">The host's id for the record.</param>
/// <param name="Values">The field values by field name, as JSON so every value round-trips unchanged.</param>
/// <remarks>The <see cref="HostDataChange.Before"/> and <see cref="HostDataChange.After"/> of a change record are
/// records of this type, so <see cref="HostDataSkill.UndoAsync"/> hands the host back exactly what it returned.</remarks>
public sealed record HostEntityRecord(string Id, IReadOnlyDictionary<string, JsonElement> Values)
{
    /// <summary>Builds a record from plain .NET values, serialized with <see cref="JsonSerializer"/>.</summary>
    /// <param name="id">The host's id for the record.</param>
    /// <param name="values">The field values by field name.</param>
    /// <returns>The record.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    public static HostEntityRecord From(string id, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(values);

        var converted = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            converted[pair.Key] = JsonSerializer.SerializeToElement(pair.Value);
        }

        return new HostEntityRecord(id, converted);
    }

    /// <summary>Gets a field's value as text: a string as it is, any other JSON value as its raw JSON.</summary>
    /// <param name="field">The field name.</param>
    /// <returns>The text, or null when the field is absent or JSON null.</returns>
    public string? GetString(string field) =>
        Values.TryGetValue(field, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.GetRawText()
            }
            : null;
}
