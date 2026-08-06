namespace TechieRag.Telemetry;

/// <summary>
/// Where an enabled TechieRag telemetry pipeline writes its spans and measurements
/// (REQ-RAG-036 / BRD-117).
/// </summary>
/// <remarks>
/// Both sinks are local by default: <see cref="Console"/> never opens a socket at all, and
/// <see cref="Otlp"/> refuses a non-loopback endpoint unless the host explicitly opts in
/// (<see cref="TechieRagTelemetryOptions.AllowRemoteEndpoint"/>).
/// </remarks>
public enum TechieRagTelemetrySink
{
    /// <summary>
    /// Export over OTLP to <see cref="TechieRagTelemetryOptions.Endpoint"/>, which defaults to the
    /// loopback collector at <c>http://localhost:4318</c>. This is the default sink, but note that a
    /// sink is only ever used once tracing or metrics have been explicitly enabled.
    /// </summary>
    Otlp = 0,

    /// <summary>
    /// Write to standard output. Opens no socket, so it is the right choice on an air-gapped or
    /// zero-egress host that still wants to see what the library is doing.
    /// </summary>
    Console = 1,
}
