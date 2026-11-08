namespace TechieRag.Agentic.HostData;

/// <summary>What the model asked a host entity's list handler for (REQ-RAG-153 / BRD-211).</summary>
/// <param name="Text">Free text to match, or null for no text filter; the host decides how to match it.</param>
/// <param name="Limit">The most records to return.</param>
public sealed record HostEntityQuery(string? Text, int Limit);
