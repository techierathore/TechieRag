namespace TechieRag.Telemetry;

/// <summary>
/// Configuration for the opt-in TechieRag OpenTelemetry exporters (REQ-RAG-036 / BRD-117).
/// </summary>
/// <remarks>
/// <para><b>Everything that could send a byte is off in the shipped defaults.</b>
/// <see cref="EnableTracing"/> and <see cref="EnableMetrics"/> are both <see langword="false"/>, so
/// constructing this type, registering it, or building a pipeline from it activates no exporter and
/// opens no socket. Referencing the <c>TechieRag.Telemetry</c> package therefore does not, by itself,
/// start any egress — the host has to ask, in code, twice: once to enable a signal and once (if it
/// wants a non-loopback collector) to allow a remote endpoint.</para>
/// <para><b>Local-only means local-only.</b> <see cref="Endpoint"/> defaults to the loopback OTLP
/// port. A non-loopback endpoint is <i>refused</i> — <see cref="ValidateEndpoint"/> throws — unless
/// <see cref="AllowRemoteEndpoint"/> is explicitly set. A typo, a copied sample config or a stray
/// environment value cannot quietly turn a local collector into a remote one.</para>
/// </remarks>
public sealed class TechieRagTelemetryOptions
{
    /// <summary>The shipped default OTLP endpoint: the loopback HTTP/protobuf collector port.</summary>
    public const string DefaultEndpoint = "http://localhost:4318";

    /// <summary>
    /// Gets or sets whether spans are exported. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>While false no tracer provider is built and no span leaves the process.</remarks>
    public bool EnableTracing { get; set; }

    /// <summary>
    /// Gets or sets whether metrics are exported. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>While false no meter provider is built and no measurement leaves the process.</remarks>
    public bool EnableMetrics { get; set; }

    /// <summary>Gets or sets where an enabled pipeline writes. Defaults to <see cref="TechieRagTelemetrySink.Otlp"/>.</summary>
    /// <remarks>
    /// The sink is inert until <see cref="EnableTracing"/> or <see cref="EnableMetrics"/> is set, so
    /// this default costs nothing. Choose <see cref="TechieRagTelemetrySink.Console"/> on a host that
    /// must never open a socket.
    /// </remarks>
    public TechieRagTelemetrySink Sink { get; set; } = TechieRagTelemetrySink.Otlp;

    /// <summary>Gets or sets the OTLP collector endpoint. Defaults to <see cref="DefaultEndpoint"/>.</summary>
    /// <remarks>Ignored when <see cref="Sink"/> is <see cref="TechieRagTelemetrySink.Console"/>.</remarks>
    public Uri Endpoint { get; set; } = new(DefaultEndpoint);

    /// <summary>
    /// Gets or sets whether a non-loopback <see cref="Endpoint"/> is permitted. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Leaving this false makes "telemetry never leaves this machine" a property the code enforces
    /// rather than a property the operator has to remember. Setting it true is a deliberate,
    /// greppable, reviewable act in the host's own source.
    /// </remarks>
    public bool AllowRemoteEndpoint { get; set; }

    /// <summary>Gets or sets how often metrics are flushed to the sink. Defaults to 15 seconds.</summary>
    public TimeSpan MetricExportInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets the <c>service.name</c> resource attribute. Defaults to <c>TechieRag</c>.</summary>
    public string ServiceName { get; set; } = "TechieRag";

    /// <summary>Gets whether any signal is enabled, i.e. whether a pipeline would export anything.</summary>
    public bool IsExportEnabled => EnableTracing || EnableMetrics;

    /// <summary>Throws when the configuration would send OTLP traffic off this machine unasked.</summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Endpoint"/> is not a loopback address and <see cref="AllowRemoteEndpoint"/> is false.
    /// </exception>
    /// <remarks>
    /// Called by <see cref="TechieRagTelemetryPipeline.Create(TechieRagTelemetryOptions)"/> before any
    /// exporter is constructed, and only when an OTLP signal is actually enabled — a remote endpoint
    /// sitting unused in a config object sends nothing and is not an error.
    /// </remarks>
    public void ValidateEndpoint()
    {
        if (Endpoint.IsLoopback || AllowRemoteEndpoint) return;

        throw new InvalidOperationException(
            $"TechieRag telemetry refuses the non-loopback OTLP endpoint '{Endpoint}' (REQ-RAG-036). " +
            "Exporters are local-only by default so that enabling observability cannot silently ship " +
            $"prompts, model names or corpus size off the machine. Set {nameof(AllowRemoteEndpoint)} " +
            "to true to accept a remote collector deliberately.");
    }
}
