using System.Globalization;
using System.Text.Json;
using TechieRag.Models;

namespace TechieRag.VectorStores;

/// <summary>
/// The one rule for "a chunk's metadata matches a filter", shared by every store (REQ-RAG-169, TrStudio TR-RAG-003).
/// </summary>
/// <remarks>
/// <para>A filter is a set of key and text pairs; a chunk matches when, for every pair, its metadata holds the key
/// and the value read as text equals the filter's text, compared ordinally. "Read as text" is the value itself for
/// a text value, and its JSON form for a number or a true/false (<c>42</c>, <c>true</c>), which is what the SQL
/// stores compare as well. Store the values you filter on as text, a Guid's string form for example; decimal
/// numbers are not compared reliably across stores.</para>
/// </remarks>
internal static class MetadataMatch
{
    /// <summary>Refuses a filter no store can apply the same way.</summary>
    /// <param name="filter">The filter.</param>
    /// <exception cref="ArgumentException">A key is empty or holds a double quote or a backslash, or a value is null.</exception>
    public static void Validate(IReadOnlyDictionary<string, string> filter)
    {
        foreach (var (key, value) in filter)
        {
            if (string.IsNullOrEmpty(key) || key.Contains('"') || key.Contains('\\'))
            {
                throw new ArgumentException($"A metadata filter key must be non-empty and hold no double quote or backslash: '{key}'.", nameof(filter));
            }

            if (value is null)
            {
                throw new ArgumentException($"The metadata filter value for '{key}' is null; filter on text.", nameof(filter));
            }
        }
    }

    /// <summary>Gets whether a chunk's metadata matches every pair of the filter.</summary>
    /// <param name="chunk">The chunk.</param>
    /// <param name="filter">The filter.</param>
    /// <returns><see langword="true"/> when every key is present with an equal text value.</returns>
    public static bool Matches(TextChunk chunk, IReadOnlyDictionary<string, string> filter)
    {
        foreach (var (key, expected) in filter)
        {
            if (!chunk.Metadata.TryGetValue(key, out var value) || !string.Equals(TextOf(value), expected, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads a metadata value as text, the way the SQL stores do.</summary>
    /// <param name="value">The value, as stored in memory or read back from JSON.</param>
    /// <returns>The text, or null for a missing value.</returns>
    public static string? TextOf(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        JsonElement { ValueKind: JsonValueKind.Null } => null,
        JsonElement element => element.GetRawText(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
