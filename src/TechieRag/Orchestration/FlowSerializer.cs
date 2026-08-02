using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechieRag.Orchestration;

/// <summary>
/// Raised when a stored flow cannot be read back (REQ-RAG-042).
/// </summary>
public sealed class FlowSerializationException : Exception
{
    /// <summary>Creates the exception with an explanation.</summary>
    /// <param name="message">What was wrong with the document.</param>
    public FlowSerializationException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with an explanation and the underlying fault.</summary>
    /// <param name="message">What was wrong with the document.</param>
    /// <param name="innerException">The parse failure underneath.</param>
    public FlowSerializationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// The serialization contract between the library's flow model and the host's storage
/// (REQ-RAG-042).
/// </summary>
/// <remarks>
/// <para><b>Division of labour.</b> The APP owns persistence — which table, which column, which
/// workspace, when to write. The LIBRARY owns the document format, so two hosts reading the same
/// exported flow read the same graph. The whole contract is one JSON string; a host needs a text
/// column and nothing else.</para>
/// <para><b>Enums are written as names.</b> <c>"Kind": "Condition"</c>, not <c>"Kind": 2</c>. An
/// ordinal is unreadable in a database, and worse, silently re-points at a different member the day
/// someone inserts an enum value in the middle. Names also make an unknown value detectable, which
/// numbers do not.</para>
/// <para><b>Version is checked before content.</b> <see cref="FromJson"/> refuses a
/// <see cref="FlowDefinition.SchemaVersion"/> it does not understand instead of deserializing what
/// it recognises and dropping the rest — a flow that silently loses a node is a flow that quietly
/// does something else.</para>
/// <para><b>Round-trip is a tested guarantee.</b> Everything a builder UI sets, including the
/// uninterpreted <see cref="FlowNode.Metadata"/> it uses for canvas coordinates, survives
/// <see cref="ToJson"/> followed by <see cref="FromJson"/> unchanged.</para>
/// </remarks>
public static class FlowSerializer
{
    /// <summary>The schema version this library writes and is the highest it can read.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Writes a flow as the JSON document a host stores.
    /// </summary>
    /// <param name="flow">The flow to write.</param>
    /// <returns>An indented JSON document carrying the whole graph.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="flow"/> is null.</exception>
    /// <remarks>
    /// Stamps <see cref="CurrentSchemaVersion"/> onto the flow as it writes, so a document's version
    /// always reflects the library that produced it rather than whatever the object happened to
    /// carry.
    /// </remarks>
    public static string ToJson(FlowDefinition flow)
    {
        ArgumentNullException.ThrowIfNull(flow);

        flow.SchemaVersion = CurrentSchemaVersion;
        return JsonSerializer.Serialize(flow, WriteOptions);
    }

    /// <summary>
    /// Reads a stored flow back.
    /// </summary>
    /// <param name="json">The document written by <see cref="ToJson"/>.</param>
    /// <returns>The flow.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json"/> is blank.</exception>
    /// <exception cref="FlowSerializationException">
    /// Thrown when the document is malformed, empty, or carries a schema version this library does
    /// not understand.
    /// </exception>
    /// <remarks>
    /// Reading is deliberately strict. A flow is executable configuration; accepting something
    /// almost-right and running it is worse than refusing and telling the host which document is
    /// broken.
    /// </remarks>
    public static FlowDefinition FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        FlowDefinition? flow;
        try
        {
            flow = JsonSerializer.Deserialize<FlowDefinition>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new FlowSerializationException($"The stored flow is not valid JSON: {ex.Message}", ex);
        }

        if (flow is null)
        {
            throw new FlowSerializationException("The stored flow deserialized to nothing.");
        }

        if (flow.SchemaVersion > CurrentSchemaVersion)
        {
            throw new FlowSerializationException(
                $"The stored flow is schema version {flow.SchemaVersion}; this library reads up to {CurrentSchemaVersion}. "
                + "Refusing to load it rather than dropping whatever it does not recognise.");
        }

        if (flow.SchemaVersion < 1)
        {
            throw new FlowSerializationException(
                $"The stored flow declares schema version {flow.SchemaVersion}, which is not a version this format ever had.");
        }

        return flow;
    }

    /// <summary>
    /// Reads a stored flow back without throwing.
    /// </summary>
    /// <param name="json">The document to read.</param>
    /// <param name="flow">The flow when reading succeeded; null otherwise.</param>
    /// <param name="error">Why reading failed; null when it succeeded.</param>
    /// <returns>True when the flow was read.</returns>
    /// <remarks>For a list screen that must render a row per stored flow and mark the broken ones.</remarks>
    public static bool TryFromJson(string? json, out FlowDefinition? flow, out string? error)
    {
        flow = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "The stored flow is empty.";
            return false;
        }

        try
        {
            flow = FromJson(json);
            return true;
        }
        catch (FlowSerializationException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
