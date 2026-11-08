namespace TechieRag.Agentic.HostData;

/// <summary>One field of a host entity, as the agent sees it (REQ-RAG-153 / BRD-211).</summary>
/// <param name="Name">The field name the model writes, for example <c>title</c>.</param>
/// <param name="Type">The kind of value.</param>
/// <param name="Description">What the field means, in invariant English for the model.</param>
/// <param name="Required">True when a create must name this field.</param>
public sealed record HostEntityField(string Name, HostFieldType Type, string Description, bool Required = false)
{
    /// <summary>Gets the allowed values of a <see cref="HostFieldType.Choice"/> field; empty for other types.</summary>
    public IReadOnlyList<string> Choices { get; init; } = [];
}
