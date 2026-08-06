using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TechieRag.Diagnostics;

/// <summary>
/// The library's OpenTelemetry instrumentation surface (REQ-RAG-036 / BRD-117).
/// </summary>
/// <remarks>
/// <para><b>Why there is no OpenTelemetry package reference here.</b> TechieRag emits through
/// <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/>,
/// which are the BCL primitives the OpenTelemetry .NET SDK collects natively. That is deliberate and
/// it is the whole design:</para>
/// <list type="bullet">
/// <item><description><b>The exporter belongs to the host, not the library.</b> A library that
/// references an exporter forces every consumer to carry it, ship it, and defend it in a security
/// review — including consumers that promise their users the instance phones no home. TechieDesk is
/// exactly such a consumer (REQ-NFR-008 / BRD-99), and it has a structural test that fails the build
/// if any OpenTelemetry assembly is linked into the app. Emitting through the BCL keeps that promise
/// true by construction rather than by discipline.</description></item>
/// <item><description><b>It is inert until someone listens.</b> An <see cref="ActivitySource"/> with
/// no listener and a <see cref="Meter"/> with no reader allocate nothing and send nothing. On top of
/// that, <see cref="Enabled"/> defaults to <see langword="false"/>, so even a host that has already
/// wired an OpenTelemetry pipeline for its own reasons gets nothing from TechieRag until it opts in.
/// Two independent switches, both off by default.</description></item>
/// </list>
/// <para><b>Wiring it up — the batteries-included way.</b> The <c>TechieRag.Telemetry</c> package
/// (REQ-RAG-036) ships ready-made OTLP and console exporters for this source and meter. It is a
/// separate package on purpose: taking it is an explicit act in the host's own project file, so the
/// core package everyone else takes still links no exporter. Its defaults export nothing and its
/// OTLP endpoint is loopback-only, so referencing it starts no egress by itself.</para>
/// <code>
/// // dotnet add package TechieRag.Telemetry
/// using var telemetry = TechieRagTelemetryPipeline.Create(new TechieRagTelemetryOptions
/// {
///     EnableTracing = true,
///     EnableMetrics = true,
/// });
/// </code>
/// <para><b>Wiring it up — by hand.</b> Nothing obliges a host to take that package; the names below
/// are all an existing OpenTelemetry pipeline needs.</para>
/// <code>
/// TechieRagTelemetry.Enabled = true;
/// services.AddOpenTelemetry()
///     .WithTracing(t => t.AddSource(TechieRagTelemetry.ActivitySourceName).AddOtlpExporter())
///     .WithMetrics(m => m.AddMeter(TechieRagTelemetry.MeterName).AddOtlpExporter());
/// </code>
/// <para>Any exporter works — OTLP, Prometheus, console, Jaeger, Azure Monitor — because the choice
/// is made entirely in the host's own dependency graph.</para>
/// <para><b>Emitted instruments:</b></para>
/// <list type="table">
/// <item><term>techierag.llm.completions</term><description>Counter — completions, tagged by provider, model and streaming.</description></item>
/// <item><term>techierag.llm.tokens</term><description>Counter — tokens, tagged by direction (input/output/cache.read/cache.write).</description></item>
/// <item><term>techierag.llm.duration</term><description>Histogram (ms) — wall time of a completion.</description></item>
/// <item><term>techierag.ingestion.documents</term><description>Counter — documents ingested.</description></item>
/// <item><term>techierag.ingestion.chunks</term><description>Counter — chunks written to the vector store.</description></item>
/// <item><term>techierag.search.duration</term><description>Histogram (ms) — wall time of a retrieval.</description></item>
/// <item><term>techierag.search.results</term><description>Histogram — results returned by a retrieval.</description></item>
/// </list>
/// </remarks>
public static class TechieRagTelemetry
{
    /// <summary>The name to pass to <c>AddSource</c> when configuring an OpenTelemetry tracer provider.</summary>
    public const string ActivitySourceName = "TechieRag";

    /// <summary>The name to pass to <c>AddMeter</c> when configuring an OpenTelemetry meter provider.</summary>
    public const string MeterName = "TechieRag";

    /// <summary>The instrumentation version reported to collectors.</summary>
    public const string InstrumentationVersion = "1.0.0";

    private static readonly Counter<long> CompletionCounter;
    private static readonly Counter<long> TokenCounter;
    private static readonly Histogram<double> CompletionDuration;
    private static readonly Counter<long> DocumentCounter;
    private static readonly Counter<long> ChunkCounter;
    private static readonly Histogram<double> SearchDuration;
    private static readonly Histogram<int> SearchResults;

    static TechieRagTelemetry()
    {
        ActivitySource = new ActivitySource(ActivitySourceName, InstrumentationVersion);
        Meter = new Meter(MeterName, InstrumentationVersion);

        CompletionCounter = Meter.CreateCounter<long>(
            "techierag.llm.completions", "{completion}", "LLM completions issued.");
        TokenCounter = Meter.CreateCounter<long>(
            "techierag.llm.tokens", "{token}", "Tokens consumed, by direction.");
        CompletionDuration = Meter.CreateHistogram<double>(
            "techierag.llm.duration", "ms", "Wall time of an LLM completion.");
        DocumentCounter = Meter.CreateCounter<long>(
            "techierag.ingestion.documents", "{document}", "Documents ingested.");
        ChunkCounter = Meter.CreateCounter<long>(
            "techierag.ingestion.chunks", "{chunk}", "Chunks written to the vector store.");
        SearchDuration = Meter.CreateHistogram<double>(
            "techierag.search.duration", "ms", "Wall time of a retrieval.");
        SearchResults = Meter.CreateHistogram<int>(
            "techierag.search.results", "{result}", "Results returned by a retrieval.");
    }

    /// <summary>
    /// Gets or sets whether TechieRag emits telemetry. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The opt-in gate. A host that has an OpenTelemetry pipeline for its own reasons still receives
    /// nothing from this library until it sets this to <see langword="true"/>, so adding observability
    /// to an application never silently starts exporting a user's prompts, model names or corpus size.
    /// </remarks>
    public static bool Enabled { get; set; }

    /// <summary>Gets the activity source spans are emitted on. Add it with <c>AddSource</c>.</summary>
    public static ActivitySource ActivitySource { get; }

    /// <summary>Gets the meter instruments are emitted on. Add it with <c>AddMeter</c>.</summary>
    public static Meter Meter { get; }

    /// <summary>Starts a span, or returns null when telemetry is off or nothing is listening.</summary>
    /// <param name="name">Span name, e.g. <c>TechieRag.Search</c>.</param>
    /// <param name="kind">Span kind; internal unless the operation crosses a process boundary.</param>
    /// <returns>The started activity, or null. Callers must tolerate null — that is the normal case.</returns>
    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal) =>
        Enabled ? ActivitySource.StartActivity(name, kind) : null;

    /// <summary>Records one LLM completion: its count, token split and duration.</summary>
    /// <param name="providerName">Provider display name, e.g. <c>Anthropic</c>.</param>
    /// <param name="modelName">Model name the completion ran against.</param>
    /// <param name="inputTokens">Prompt tokens billed.</param>
    /// <param name="outputTokens">Completion tokens billed.</param>
    /// <param name="duration">Wall time of the call.</param>
    /// <param name="isStreaming">Whether the response was streamed.</param>
    /// <param name="cacheReadTokens">Prompt tokens served from the provider's cache (REQ-RAG-043).</param>
    /// <param name="cacheWriteTokens">Prompt tokens written into the provider's cache (REQ-RAG-043).</param>
    public static void RecordLlmCompletion(
        string providerName,
        string modelName,
        int inputTokens,
        int outputTokens,
        TimeSpan duration,
        bool isStreaming,
        int cacheReadTokens = 0,
        int cacheWriteTokens = 0)
    {
        if (!Enabled) return;

        var provider = new KeyValuePair<string, object?>("techierag.provider", providerName);
        var model = new KeyValuePair<string, object?>("techierag.model", modelName);
        var streaming = new KeyValuePair<string, object?>("techierag.streaming", isStreaming);

        CompletionCounter.Add(1, provider, model, streaming);
        CompletionDuration.Record(duration.TotalMilliseconds, provider, model, streaming);

        RecordTokens(inputTokens, "input", provider, model);
        RecordTokens(outputTokens, "output", provider, model);
        RecordTokens(cacheReadTokens, "cache.read", provider, model);
        RecordTokens(cacheWriteTokens, "cache.write", provider, model);
    }

    /// <summary>Records one document ingestion and the chunks it produced.</summary>
    /// <param name="chunkCount">Chunks written to the vector store for this document.</param>
    /// <param name="sourceType">Where the document came from, e.g. <c>file</c>, <c>text</c>, <c>web</c>.</param>
    public static void RecordIngestion(int chunkCount, string sourceType)
    {
        if (!Enabled) return;

        var source = new KeyValuePair<string, object?>("techierag.source.type", sourceType);
        DocumentCounter.Add(1, source);
        if (chunkCount > 0) ChunkCounter.Add(chunkCount, source);
    }

    /// <summary>Records one retrieval: its duration and how many results it returned.</summary>
    /// <param name="duration">Wall time of the retrieval, embedding and rerank included.</param>
    /// <param name="resultCount">Results handed back to the caller.</param>
    /// <param name="reranked">Whether the rerank stage ran for this call.</param>
    public static void RecordSearch(TimeSpan duration, int resultCount, bool reranked)
    {
        if (!Enabled) return;

        var rerank = new KeyValuePair<string, object?>("techierag.reranked", reranked);
        SearchDuration.Record(duration.TotalMilliseconds, rerank);
        SearchResults.Record(resultCount, rerank);
    }

    private static void RecordTokens(
        int count,
        string direction,
        KeyValuePair<string, object?> provider,
        KeyValuePair<string, object?> model)
    {
        // A zero-token direction is not a data point, it is an absence. Recording it would put
        // meaningless zeroes into every cache histogram for providers that do no caching at all.
        if (count <= 0) return;

        TokenCounter.Add(
            count,
            provider,
            model,
            new KeyValuePair<string, object?>("techierag.token.direction", direction));
    }
}
