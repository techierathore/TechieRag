using TechieRag.Orchestration;

namespace TechieDesk.Services.Flows;

/// <summary>
/// One stored flow, exactly as the <c>Flow</c> table holds it (REQ-UI-040 / BRD-92).
/// </summary>
/// <remarks>
/// <para><b>The blob is the flow; the columns are a projection.</b>
/// <see cref="DefinitionJson"/> is <see cref="FlowSerializer.ToJson"/> output stored verbatim and is
/// the single source of truth. <see cref="Name"/>, <see cref="Description"/> and
/// <see cref="SchemaVersion"/> are mirrored onto columns so the list screen can sort, label and grey
/// out rows without parsing every blob — not so anything can disagree with the document.</para>
/// </remarks>
public sealed class FlowRecord
{
    /// <summary>Gets or sets the flow identifier, matching <see cref="FlowDefinition.Id"/>.</summary>
    public string FlowId { get; set; } = string.Empty;

    /// <summary>Gets or sets the owning workspace identifier.</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the flow's display name, mirrored from the definition.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the flow's description, mirrored from the definition.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the serialized flow, stored verbatim.</summary>
    public string DefinitionJson { get; set; } = string.Empty;

    /// <summary>Gets or sets the schema version the document was written with.</summary>
    public int SchemaVersion { get; set; } = FlowSerializer.CurrentSchemaVersion;

    /// <summary>Gets or sets whether this flow may be run.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets when the flow was first saved (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the flow was last saved (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// One row of the flow list: the stored record, and the flow it parsed to — or why it did not
/// (REQ-UI-040).
/// </summary>
/// <param name="Record">The stored row.</param>
/// <param name="Definition">The parsed flow, or null when the document could not be read.</param>
/// <param name="ReadError">Why the document could not be read; null when it was read.</param>
/// <remarks>
/// <para><b>Why a corrupt row is a value and not an exception.</b> A list screen must render a row
/// per stored flow. If one hand-edited or newer-schema document threw on the way out of the
/// repository, the whole page would show an error and the user's other nine flows would be
/// unreachable — a single bad row taking a screen down is the defect this shape exists to prevent.
/// <see cref="FlowSerializer.TryFromJson"/> is the library's own affordance for exactly this.</para>
/// </remarks>
public sealed record FlowListItem(FlowRecord Record, FlowDefinition? Definition, string? ReadError)
{
    /// <summary>Gets whether the stored document could be read back into a flow.</summary>
    public bool IsReadable => Definition is not null;

    /// <summary>Gets how many nodes the flow has, or zero when it could not be read.</summary>
    public int NodeCount => Definition?.Nodes.Count ?? 0;

    /// <summary>Gets how many edges the flow has, or zero when it could not be read.</summary>
    public int EdgeCount => Definition?.Edges.Count ?? 0;
}
