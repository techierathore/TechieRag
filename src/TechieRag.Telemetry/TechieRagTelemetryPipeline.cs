using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TechieRag.Diagnostics;

namespace TechieRag.Telemetry;

/// <summary>
/// A built TechieRag OpenTelemetry pipeline: the tracer and meter providers that collect the
/// library's <c>TechieRag</c> activity source and meter and push them to a local sink
/// (REQ-RAG-036 / BRD-117).
/// </summary>
/// <remarks>
/// <para>Create one at startup, keep it for the life of the process, dispose it on shutdown. When
/// the options enable nothing — which is the shipped default — <see cref="Create"/> still returns a
/// pipeline, but an <b>inert</b> one: no provider is built, no exporter is constructed, no socket is
/// opened and <see cref="TechieRagTelemetry.Enabled"/> is left alone. That is deliberate, so a host
/// can call this unconditionally and let configuration decide.</para>
/// <para>Enabling a signal flips <see cref="TechieRagTelemetry.Enabled"/> on, because there is no
/// point collecting from a source the library is not emitting on. <see cref="Dispose"/> flips it
/// back if this pipeline was the one that set it.</para>
/// <example>
/// <code>
/// using var telemetry = TechieRagTelemetryPipeline.Create(new TechieRagTelemetryOptions
/// {
///     EnableTracing = true,
///     EnableMetrics = true,
/// });
/// </code>
/// </example>
/// </remarks>
public sealed class TechieRagTelemetryPipeline : IDisposable
{
    private readonly TracerProvider? tracerProvider;
    private readonly MeterProvider? meterProvider;
    private readonly bool ownsEnabledFlag;
    private bool disposed;

    private TechieRagTelemetryPipeline(
        TracerProvider? tracerProvider,
        MeterProvider? meterProvider,
        bool ownsEnabledFlag)
    {
        this.tracerProvider = tracerProvider;
        this.meterProvider = meterProvider;
        this.ownsEnabledFlag = ownsEnabledFlag;
    }

    /// <summary>Gets whether any exporter is actually running behind this pipeline.</summary>
    /// <remarks>False for the shipped default configuration.</remarks>
    public bool IsActive => tracerProvider is not null || meterProvider is not null;

    /// <summary>Builds the pipeline described by <paramref name="options"/>.</summary>
    /// <param name="options">The exporter configuration. Its defaults enable nothing.</param>
    /// <returns>
    /// A pipeline the caller owns and must dispose. Inert when
    /// <see cref="TechieRagTelemetryOptions.IsExportEnabled"/> is false.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// An OTLP signal is enabled against a non-loopback endpoint without
    /// <see cref="TechieRagTelemetryOptions.AllowRemoteEndpoint"/>.
    /// </exception>
    public static TechieRagTelemetryPipeline Create(TechieRagTelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsExportEnabled)
        {
            return new TechieRagTelemetryPipeline(null, null, ownsEnabledFlag: false);
        }

        if (options.Sink == TechieRagTelemetrySink.Otlp)
        {
            options.ValidateEndpoint();
        }

        var tracing = options.EnableTracing ? BuildTracerProvider(options) : null;
        var metrics = options.EnableMetrics ? BuildMeterProvider(options) : null;

        var ownsFlag = !TechieRagTelemetry.Enabled;
        TechieRagTelemetry.Enabled = true;

        return new TechieRagTelemetryPipeline(tracing, metrics, ownsFlag);
    }

    /// <summary>Pushes everything buffered to the sink now rather than at the next interval.</summary>
    /// <param name="timeoutMilliseconds">How long to wait for the flush, in milliseconds.</param>
    /// <returns>True when every enabled provider flushed within the timeout.</returns>
    /// <remarks>Returns true for an inert pipeline: nothing buffered flushes trivially.</remarks>
    public bool ForceFlush(int timeoutMilliseconds = 10000)
    {
        var tracesFlushed = tracerProvider?.ForceFlush(timeoutMilliseconds) ?? true;
        var metricsFlushed = meterProvider?.ForceFlush(timeoutMilliseconds) ?? true;
        return tracesFlushed && metricsFlushed;
    }

    /// <summary>Shuts the exporters down and restores the library's emit gate if this pipeline set it.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        tracerProvider?.Dispose();
        meterProvider?.Dispose();

        if (ownsEnabledFlag)
        {
            TechieRagTelemetry.Enabled = false;
        }
    }

    private static TracerProvider BuildTracerProvider(TechieRagTelemetryOptions options)
    {
        var builder = Sdk.CreateTracerProviderBuilder()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .AddSource(TechieRagTelemetry.ActivitySourceName);

        if (options.Sink == TechieRagTelemetrySink.Console)
        {
            builder.AddConsoleExporter();
        }
        else
        {
            builder.AddOtlpExporter(exporter => ConfigureOtlp(exporter, options));
        }

        return builder.Build()!;
    }

    private static MeterProvider BuildMeterProvider(TechieRagTelemetryOptions options)
    {
        var builder = Sdk.CreateMeterProviderBuilder()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .AddMeter(TechieRagTelemetry.MeterName);

        if (options.Sink == TechieRagTelemetrySink.Console)
        {
            builder.AddConsoleExporter();
        }
        else
        {
            builder.AddOtlpExporter((exporter, reader) =>
            {
                ConfigureOtlp(exporter, options);
                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                    (int)options.MetricExportInterval.TotalMilliseconds;
            });
        }

        return builder.Build()!;
    }

    private static void ConfigureOtlp(OtlpExporterOptions exporter, TechieRagTelemetryOptions options)
    {
        exporter.Endpoint = options.Endpoint;

        // HTTP/protobuf rather than gRPC: it is the protocol a plain local collector, a sidecar or a
        // test listener can accept without an HTTP/2 stack, and it is what the loopback default port
        // (4318) speaks.
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
    }
}
