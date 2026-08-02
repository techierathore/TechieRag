using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TechieRag.Diagnostics;
using TechieRag.Telemetry;
using TechieRag.Tests.Diagnostics;
using Xunit;

namespace TechieRag.Tests.Telemetry;

/// <summary>
/// Tests for the opt-in OpenTelemetry exporter package (REQ-RAG-036 / BRD-117), and for the
/// zero-egress default it must not break (REQ-NFR-008 / BRD-99).
/// </summary>
/// <remarks>
/// <para><b>Why a real socket and not a flag.</b> The cheap version of the default-off test reads
/// <c>options.EnableTracing == false</c> and calls it proven. That test passes against code that
/// exports anyway, which is exactly the failure this project already caught once on REQ-NFR-013.
/// So every egress claim here is counted at an <see cref="HttpListener"/> bound to 127.0.0.1: the
/// pipeline's OTLP endpoint IS that listener, so if the shipped default ever activated an exporter,
/// bytes would land in <see cref="LoopbackCollector.BytesReceived"/> and these tests would go red.</para>
/// <para>The exporter assemblies are loaded in this test process — <c>TechieRag.Tests</c> references
/// <c>TechieRag.Telemetry</c> — so "nothing was sent" is never merely "nothing was available to send".</para>
/// </remarks>
[Collection(TelemetryCollection.Name)]
public sealed class TechieRagTelemetryExporterTests : IDisposable
{
    private const string ProbeProvider = "exporter-probe-provider";

    /// <summary>Restores the shipped default so a later test never inherits an enabled pipeline.</summary>
    public void Dispose() => TechieRagTelemetry.Enabled = false;

    /// <summary>The shipped options enable neither signal, so a pipeline built from them is inert.</summary>
    [Fact]
    public void ShippedOptionsEnableNoSignal()
    {
        var options = new TechieRagTelemetryOptions();

        Assert.False(options.EnableTracing);
        Assert.False(options.EnableMetrics);
        Assert.False(options.IsExportEnabled);
    }

    /// <summary>The shipped OTLP endpoint is the loopback collector, not a remote one.</summary>
    [Fact]
    public void ShippedEndpointIsLoopback()
    {
        var options = new TechieRagTelemetryOptions();

        Assert.True(options.Endpoint.IsLoopback);
        Assert.False(options.AllowRemoteEndpoint);
    }

    /// <summary>
    /// The load-bearing one. With the shipped defaults, and with an OTLP collector listening on the
    /// very endpoint the options point at, driving real TechieRag work puts <b>zero bytes</b> on the
    /// wire — including when the library's own emit gate has been turned on by the host for its own
    /// reasons. Two independent switches, and the socket proves it rather than the flag.
    /// </summary>
    [Fact]
    public async Task TheShippedDefaultPutsZeroBytesOnTheWire()
    {
        using var collector = LoopbackCollector.Start();

        var options = new TechieRagTelemetryOptions { Endpoint = new Uri(collector.Url) };
        using var pipeline = TechieRagTelemetryPipeline.Create(options);

        // Switch one: the library's emit gate, at its default.
        RecordProbeWork();

        // Switch two: even with the gate forced on, no exporter exists to carry anything out.
        TechieRagTelemetry.Enabled = true;
        RecordProbeWork();

        pipeline.ForceFlush();
        await Task.Delay(500);

        // The bytes are the proof. These come FIRST deliberately: an assertion on pipeline.IsActive
        // ahead of them would short-circuit a regression before the socket ever got a chance to speak,
        // which is the flag-only test this project has been burned by before.
        Assert.Equal(0, collector.BytesReceived);
        Assert.Equal(0, collector.RequestCount);
        Assert.False(pipeline.IsActive);
    }

    /// <summary>
    /// Explicitly enabling both signals really does export: spans and measurements arrive at a
    /// loopback collector, carrying the TechieRag source name and instrument names. "It compiles" is
    /// not proof, so this reads the bytes that landed.
    /// </summary>
    [Fact]
    public async Task EnabledPipelineDeliversSpansAndMetricsToTheCollector()
    {
        using var collector = LoopbackCollector.Start();

        var options = new TechieRagTelemetryOptions
        {
            EnableTracing = true,
            EnableMetrics = true,
            Endpoint = new Uri(collector.Url),
            MetricExportInterval = TimeSpan.FromMilliseconds(200),
        };

        using (var pipeline = TechieRagTelemetryPipeline.Create(options))
        {
            Assert.True(pipeline.IsActive);
            Assert.True(TechieRagTelemetry.Enabled);

            using (var activity = TechieRagTelemetry.StartActivity("TechieRag.ExporterProbe"))
            {
                Assert.NotNull(activity);
            }

            RecordProbeWork();
            pipeline.ForceFlush();

            await collector.WaitForPayloadAsync(payload =>
                payload.Contains("TechieRag.ExporterProbe", StringComparison.Ordinal));
            await collector.WaitForPayloadAsync(payload =>
                payload.Contains("techierag.llm.completions", StringComparison.Ordinal));
        }

        Assert.True(collector.RequestCount > 0);
        Assert.True(collector.BytesReceived > 0);
        Assert.Contains(collector.Payloads, p => p.Contains(ProbeProvider, StringComparison.Ordinal));
    }

    /// <summary>
    /// Disposing the pipeline that turned the emit gate on turns it back off, so a host that shuts
    /// telemetry down stops emitting rather than merely stopping exporting.
    /// </summary>
    [Fact]
    public void DisposingThePipelineRestoresTheEmitGate()
    {
        using var collector = LoopbackCollector.Start();
        TechieRagTelemetry.Enabled = false;

        var pipeline = TechieRagTelemetryPipeline.Create(new TechieRagTelemetryOptions
        {
            EnableMetrics = true,
            Endpoint = new Uri(collector.Url),
            MetricExportInterval = TimeSpan.FromSeconds(30),
        });

        Assert.True(TechieRagTelemetry.Enabled);

        pipeline.Dispose();

        Assert.False(TechieRagTelemetry.Enabled);
    }

    /// <summary>
    /// A non-loopback OTLP endpoint is refused outright, not warned about. Local-only is enforced by
    /// the code, so a copied sample config cannot turn a local collector into a remote one.
    /// </summary>
    [Theory]
    [InlineData("http://otel.example.test:4318")]
    [InlineData("https://collector.vendor.test")]
    [InlineData("http://192.0.2.10:4318")]
    public void NonLoopbackEndpointIsRefused(string endpoint)
    {
        var options = new TechieRagTelemetryOptions
        {
            EnableTracing = true,
            Endpoint = new Uri(endpoint),
        };

        var error = Assert.Throws<InvalidOperationException>(() => TechieRagTelemetryPipeline.Create(options));

        Assert.Contains("local-only", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(TechieRagTelemetry.Enabled);
    }

    /// <summary>Every spelling of "this machine" is accepted without the opt-in flag.</summary>
    [Theory]
    [InlineData("http://localhost:4318")]
    [InlineData("http://127.0.0.1:4318")]
    [InlineData("http://[::1]:4318")]
    public void LoopbackEndpointsAreAccepted(string endpoint)
    {
        var options = new TechieRagTelemetryOptions { Endpoint = new Uri(endpoint) };

        options.ValidateEndpoint();
    }

    /// <summary>
    /// Remote is opt-in rather than forbidden: a host that genuinely runs a central collector can say
    /// so, in its own source, where a reviewer can grep for it.
    /// </summary>
    [Fact]
    public void RemoteEndpointIsAcceptedOnlyWhenExplicitlyAllowed()
    {
        var options = new TechieRagTelemetryOptions
        {
            Endpoint = new Uri("https://collector.vendor.test"),
            AllowRemoteEndpoint = true,
        };

        options.ValidateEndpoint();
    }

    /// <summary>
    /// The console sink is fully enabled yet opens no socket, which is what an air-gapped host needs.
    /// Counted at the collector the OTLP endpoint still points at.
    /// </summary>
    [Fact]
    public async Task ConsoleSinkSendsNothingOverTheNetwork()
    {
        using var collector = LoopbackCollector.Start();

        var options = new TechieRagTelemetryOptions
        {
            EnableTracing = true,
            EnableMetrics = true,
            Sink = TechieRagTelemetrySink.Console,
            Endpoint = new Uri(collector.Url),
            MetricExportInterval = TimeSpan.FromMilliseconds(200),
        };

        using (var pipeline = TechieRagTelemetryPipeline.Create(options))
        {
            Assert.True(pipeline.IsActive);
            RecordProbeWork();
            pipeline.ForceFlush();
            await Task.Delay(700);
        }

        Assert.Equal(0, collector.RequestCount);
        Assert.Equal(0, collector.BytesReceived);
    }

    /// <summary>
    /// The DI entry point called with no configuration registers an inert pipeline and sends nothing,
    /// so wiring the package into a container is not itself an opt-in.
    /// </summary>
    [Fact]
    public async Task DiRegistrationWithoutConfigurationSendsNothing()
    {
        using var collector = LoopbackCollector.Start();

        var services = new ServiceCollection();
        services.AddTechieRagTelemetry(options => options.Endpoint = new Uri(collector.Url));

        await using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<TechieRagTelemetryPipeline>();

        TechieRagTelemetry.Enabled = true;
        RecordProbeWork();
        pipeline.ForceFlush();
        await Task.Delay(500);

        Assert.Equal(0, collector.BytesReceived);
        Assert.Equal(0, collector.RequestCount);
        Assert.False(pipeline.IsActive);
    }

    private static void RecordProbeWork()
    {
        TechieRagTelemetry.RecordLlmCompletion(
            ProbeProvider, "exporter-probe-model", 11, 7, TimeSpan.FromMilliseconds(4), false);
        TechieRagTelemetry.RecordIngestion(3, "text");
        TechieRagTelemetry.RecordSearch(TimeSpan.FromMilliseconds(9), 4, reranked: false);
    }

    /// <summary>
    /// A real OTLP/HTTP endpoint on 127.0.0.1 that counts the bytes actually delivered to it.
    /// </summary>
    /// <remarks>
    /// The transport seam. Nothing in the process can fake a request arriving here — the operating
    /// system had to carry it — which is why the egress assertions read this and not a property.
    /// </remarks>
    private sealed class LoopbackCollector : IDisposable
    {
        private readonly HttpListener listener;
        private readonly List<string> payloads = [];
        private int requestCount;
        private long bytesReceived;

        private LoopbackCollector(HttpListener listener, string url)
        {
            this.listener = listener;
            Url = url;
        }

        public string Url { get; }

        public int RequestCount => Volatile.Read(ref requestCount);

        public long BytesReceived => Interlocked.Read(ref bytesReceived);

        public IReadOnlyList<string> Payloads
        {
            get
            {
                lock (payloads)
                {
                    return payloads.ToArray();
                }
            }
        }

        public static LoopbackCollector Start()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            var collector = new LoopbackCollector(listener, $"http://127.0.0.1:{port}");
            _ = collector.AcceptLoopAsync();
            return collector;
        }

        /// <summary>Waits until a delivered payload satisfies <paramref name="predicate"/>.</summary>
        public async Task WaitForPayloadAsync(Func<string, bool> predicate, int timeoutMilliseconds = 15000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);

            while (DateTime.UtcNow < deadline)
            {
                if (Payloads.Any(predicate)) return;
                await Task.Delay(50).ConfigureAwait(false);
            }

            Assert.Fail(
                $"No OTLP payload matching the predicate reached {Url} within {timeoutMilliseconds}ms. " +
                $"Requests seen: {RequestCount}, bytes: {BytesReceived}.");
        }

        public void Dispose() => listener.Close();

        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Disposal races the accept loop; a closed listener is the expected end state.
                    return;
                }

                await RecordAsync(context).ConfigureAwait(false);
            }
        }

        private async Task RecordAsync(HttpListenerContext context)
        {
            Interlocked.Increment(ref requestCount);

            using var body = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(body).ConfigureAwait(false);
            var bytes = body.ToArray();
            Interlocked.Add(ref bytesReceived, bytes.Length);

            lock (payloads)
            {
                // OTLP protobuf writes its strings as raw UTF-8, so source, instrument and tag names
                // are readable in the wire bytes without a protobuf decoder in the test project.
                payloads.Add(Encoding.UTF8.GetString(bytes));
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/x-protobuf";
            context.Response.ContentLength64 = 0;
            context.Response.Close();
        }
    }
}
