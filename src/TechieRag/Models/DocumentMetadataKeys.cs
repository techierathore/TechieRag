using System.Text.Json;

namespace TechieRag.Models;

/// <summary>
/// The well-known <see cref="Document.Metadata"/> keys, and the rule deciding which of a chunk's
/// metadata entries describe the whole document rather than that one chunk.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Ingestion records everything it knows on every <see cref="TextChunk"/>,
/// because a chunk is what a vector store persists. A document row is created from the first chunk
/// of a document, so without a shared rule about which entries are document-scoped a store has to
/// either drop them all (which is what <c>SqliteVecStore</c> did — it wrote a literal <c>{}</c>) or
/// copy them all, which would publish genuinely chunk-local values such as a PDF page number or an
/// audio segment's start offset as facts about the whole document.</para>
/// <para><b>Code Flow:</b> <c>TechieRagClient</c> writes these keys onto every chunk during
/// ingestion; each <c>IVectorStore</c> calls <see cref="ExtractDocumentScoped"/> when it creates the
/// document row, so <c>ListDocumentsAsync</c> returns them in <see cref="Document.Metadata"/>.</para>
/// <para><b>Design:</b> An allowlist rather than a denylist. A new chunk-local key added by a
/// processor must not silently start appearing at document level, whereas a new document-level key
/// is added here deliberately, in one place, by whoever introduces it.</para>
/// </remarks>
public static class DocumentMetadataKeys
{
    /// <summary>
    /// Byte size of the source artefact the document was ingested from.
    /// </summary>
    /// <remarks>
    /// A <see cref="long"/>. For a file this is the file's length on disk; for text, web and
    /// transcript ingestion it is the UTF-8 byte count of the text that was actually stored, which
    /// is the artefact in those routes. Absent for documents ingested before this key existed —
    /// consumers must treat a missing value as "unknown" rather than as zero.
    /// </remarks>
    public const string FileSize = "FileSize";

    /// <summary>Absolute URL the document was retrieved from, when it came from the web.</summary>
    public const string SourceUrl = "SourceUrl";

    /// <summary>Short identifier of the ingestion route, for example <c>web</c> or <c>youtube</c>.</summary>
    public const string SourceType = "SourceType";

    /// <summary>Display name of the connector or site the document came from.</summary>
    public const string SourceName = "SourceName";

    /// <summary>MIME type of the source artefact, when the route knows it.</summary>
    public const string ContentType = "ContentType";

    /// <summary>Identifier of the item in the source system, for connector ingestion.</summary>
    public const string ItemId = "ItemId";

    /// <summary>Round-trip UTC timestamp recorded by the ingestion route.</summary>
    public const string IngestedAtUtc = "IngestedAtUtc";

    /// <summary>
    /// Identifies what produced this document's vectors — provider, model and encoding revision
    /// (REQ-RAG-052).
    /// </summary>
    /// <remarks>
    /// <para>Written by <c>TechieRagClient</c> on every chunk at ingestion, from
    /// <see cref="Abstractions.IEmbeddingProvider.EmbeddingSignature"/>, and compared by
    /// <see cref="EmbeddingStaleness.Analyze"/>. Vectors carrying different signatures live in
    /// different spaces and must not be searched together.</para>
    /// <para><b>Absent for anything ingested before 2026-08-04</b>, which is the whole point of it —
    /// a missing stamp is reported as <see cref="EmbeddingStalenessReason.Unstamped"/> and therefore
    /// stale, never as "probably fine".</para>
    /// </remarks>
    public const string EmbeddingSignature = "EmbeddingSignature";

    /// <summary>
    /// Gets every metadata key that describes a document as a whole.
    /// </summary>
    /// <remarks>
    /// Ordered only for readability; membership is what matters. Anything absent from this list
    /// stays on the chunk it was written to.
    /// </remarks>
    public static IReadOnlyList<string> DocumentScoped { get; } =
    [
        FileSize,
        SourceUrl,
        SourceType,
        SourceName,
        ContentType,
        ItemId,
        IngestedAtUtc,
        EmbeddingSignature
    ];

    /// <summary>
    /// Selects the document-scoped entries from one chunk's metadata.
    /// </summary>
    /// <param name="chunkMetadata">Metadata of the chunk the document row is being created from, or null.</param>
    /// <returns>
    /// A new dictionary holding the entries whose keys appear in <see cref="DocumentScoped"/>.
    /// Empty when the chunk carries none of them, which is the correct result for a document
    /// ingested before those keys were written.
    /// </returns>
    /// <remarks>
    /// Null values are dropped: a key present with no value tells a consumer nothing and would make
    /// "the key is there" a useless test.
    /// </remarks>
    public static Dictionary<string, object> ExtractDocumentScoped(
        IReadOnlyDictionary<string, object>? chunkMetadata)
    {
        var extracted = new Dictionary<string, object>(StringComparer.Ordinal);
        if (chunkMetadata is null)
        {
            return extracted;
        }

        foreach (var key in DocumentScoped)
        {
            if (chunkMetadata.TryGetValue(key, out var value) && value is not null)
            {
                extracted[key] = value;
            }
        }

        return extracted;
    }

    /// <summary>
    /// Rebuilds a document's metadata dictionary from the JSON a vector store persisted.
    /// </summary>
    /// <param name="json">The stored JSON object, or null/blank when the store holds none.</param>
    /// <returns>A dictionary whose values are ordinary CLR primitives, never <see cref="JsonElement"/>.</returns>
    /// <remarks>
    /// <para><b>Why this is not just <c>JsonSerializer.Deserialize</c>.</b> Deserializing into
    /// <c>Dictionary&lt;string, object&gt;</c> yields <see cref="JsonElement"/> values, and
    /// <see cref="JsonElement"/> does not implement <see cref="IConvertible"/>. A caller doing the
    /// obvious thing — <c>Convert.ToInt64(document.Metadata["FileSize"])</c> — therefore throws, and
    /// a caller that guards the conversion silently falls back to "unknown" for a value that was
    /// stored correctly. Unwrapping here makes the round trip return what was put in.</para>
    /// <para>Objects and arrays are returned as their raw JSON text, because there is no primitive
    /// to unwrap them to and discarding them would lose data a caller deliberately stored.</para>
    /// </remarks>
    public static Dictionary<string, object> FromJson(string? json)
    {
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return metadata;
        }

        Dictionary<string, JsonElement>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch (JsonException)
        {
            return metadata;
        }

        if (raw is null)
        {
            return metadata;
        }

        foreach (var pair in raw)
        {
            var value = UnwrapJsonValue(pair.Value);
            if (value is not null)
            {
                metadata[pair.Key] = value;
            }
        }

        return metadata;
    }

    /// <summary>
    /// Reads a document's recorded byte size.
    /// </summary>
    /// <param name="document">A document, typically from <c>ListDocumentsAsync</c>.</param>
    /// <param name="bytes">Receives the byte size, or zero when none was recorded.</param>
    /// <returns>True when the document carries a usable, positive byte size; otherwise false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
    /// <remarks>
    /// A document that predates <see cref="FileSize"/>, or whose stored value survived a JSON round
    /// trip as something unconvertible, returns false rather than zero — the caller then shows
    /// "unknown" instead of claiming an empty file.
    /// </remarks>
    public static bool TryGetFileSizeBytes(Document document, out long bytes)
    {
        ArgumentNullException.ThrowIfNull(document);

        bytes = 0;
        if (!document.Metadata.TryGetValue(FileSize, out var value) || value is null)
        {
            return false;
        }

        try
        {
            bytes = Convert.ToInt64(value);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            bytes = 0;
            return false;
        }

        return bytes > 0;
    }

    /// <summary>Converts one parsed JSON value into the CLR primitive a caller can use.</summary>
    /// <param name="element">The parsed value.</param>
    /// <returns>The unwrapped value, or null when the entry carried no value.</returns>
    /// <remarks>
    /// A whole number is boxed as a <see cref="long"/>, not a <see cref="double"/>. The obvious
    /// ternary makes both branches <see cref="double"/> — the common type of <c>long</c> and
    /// <c>double</c> — so a byte size stored as <c>7080</c> came back as <c>7080d</c> and any
    /// consumer doing a type test on it saw the wrong type.
    /// </remarks>
    private static object? UnwrapJsonValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                // Not a ternary: `cond ? whole : element.GetDouble()` has the natural type double,
                // so the long would be widened before it was boxed.
                if (element.TryGetInt64(out var whole))
                {
                    return whole;
                }

                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                return element.GetRawText();
        }
    }
}
