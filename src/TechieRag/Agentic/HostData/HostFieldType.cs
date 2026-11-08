namespace TechieRag.Agentic.HostData;

/// <summary>The kind of value a host entity field holds (REQ-RAG-153 / BRD-211).</summary>
/// <remarks>The skill checks a value the model sends against this before any host handler runs.</remarks>
public enum HostFieldType
{
    /// <summary>Free text: a JSON string.</summary>
    Text,

    /// <summary>A number: a JSON number.</summary>
    Number,

    /// <summary>True or false: a JSON boolean.</summary>
    Boolean,

    /// <summary>A calendar date: a JSON string <c>yyyy-MM-dd</c>.</summary>
    Date,

    /// <summary>A date and time: a JSON string in ISO 8601, for example <c>2026-11-08T15:00:00</c>.</summary>
    DateTime,

    /// <summary>One of <see cref="HostEntityField.Choices"/>: a JSON string.</summary>
    Choice
}
