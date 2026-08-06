using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using TechieRag.Diagnostics;
using Xunit;

namespace TechieRag.Tests.Diagnostics;

/// <summary>
/// Tests for the opt-in OpenTelemetry instrumentation surface (REQ-RAG-036 / BRD-117).
/// </summary>
/// <remarks>
/// <para><b>Why every assertion is filtered by model tag.</b> <see cref="TechieRagTelemetry.Enabled"/>
/// is process-wide static state, and xUnit runs test classes in parallel. Each test therefore records
/// against a model name unique to itself and counts only the measurements carrying that tag, so a
/// concurrent test that happens to search or complete cannot make these pass or fail.</para>
/// <para>This class is the only place that mutates <see cref="TechieRagTelemetry.Enabled"/>, and xUnit
/// runs the tests within one class sequentially, so the flag is never contended.</para>
/// </remarks>
[Collection(TelemetryCollection.Name)]
public sealed class TechieRagTelemetryTests : IDisposable
{
    /// <summary>Restores the shipped default so a later test never inherits an enabled pipeline.</summary>
    public void Dispose() => TechieRagTelemetry.Enabled = false;

    /// <summary>
    /// The default is silence. A consumer that never asks for telemetry never emits any, which is
    /// what lets a privacy-promising host (REQ-NFR-008) take this library unchanged.
    /// </summary>
    [Fact]
    public void TelemetryIsOffByDefault()
    {
        var enabled = (bool)typeof(TechieRagTelemetry)
            .GetProperty(nameof(TechieRagTelemetry.Enabled), BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        Assert.False(enabled);
    }

    /// <summary>Nothing is measured while the switch is off, even with a listener attached.</summary>
    [Fact]
    public void NoMeasurementsAreRecordedWhenDisabled()
    {
        TechieRagTelemetry.Enabled = false;
        const string model = "disabled-probe-model";

        using var collector = new MeasurementCollector(model);
        TechieRagTelemetry.RecordLlmCompletion("p", model, 10, 5, TimeSpan.FromMilliseconds(3), false);

        Assert.Empty(collector.Counters);
        Assert.Empty(collector.Histograms);
    }

    /// <summary>Once enabled, a completion produces its count, its token split and its duration.</summary>
    [Fact]
    public void CompletionsAreMeasuredWhenEnabled()
    {
        TechieRagTelemetry.Enabled = true;
        const string model = "enabled-probe-model";

        using var collector = new MeasurementCollector(model);
        TechieRagTelemetry.RecordLlmCompletion("p", model, 10, 5, TimeSpan.FromMilliseconds(3), false);

        Assert.Equal(1, collector.Sum("techierag.llm.completions"));
        Assert.Equal(10, collector.Sum("techierag.llm.tokens", direction: "input"));
        Assert.Equal(5, collector.Sum("techierag.llm.tokens", direction: "output"));
        Assert.Contains(collector.Histograms, h => h.Instrument == "techierag.llm.duration");
    }

    /// <summary>Cache accounting is a distinct direction, not folded into the input total.</summary>
    [Fact]
    public void CacheTokensAreRecordedAsTheirOwnDirections()
    {
        TechieRagTelemetry.Enabled = true;
        const string model = "cache-probe-model";

        using var collector = new MeasurementCollector(model);
        TechieRagTelemetry.RecordLlmCompletion(
            "p", model, 10, 5, TimeSpan.FromMilliseconds(3), false, cacheReadTokens: 700, cacheWriteTokens: 300);

        Assert.Equal(700, collector.Sum("techierag.llm.tokens", direction: "cache.read"));
        Assert.Equal(300, collector.Sum("techierag.llm.tokens", direction: "cache.write"));
        Assert.Equal(10, collector.Sum("techierag.llm.tokens", direction: "input"));
    }

    /// <summary>
    /// A provider that does no caching reports nothing rather than a stream of zeroes, so a dashboard
    /// can tell "no cache" apart from "cache always misses".
    /// </summary>
    [Fact]
    public void ZeroTokenDirectionsAreNotRecorded()
    {
        TechieRagTelemetry.Enabled = true;
        const string model = "zero-probe-model";

        using var collector = new MeasurementCollector(model);
        TechieRagTelemetry.RecordLlmCompletion("p", model, 10, 5, TimeSpan.FromMilliseconds(3), false);

        Assert.DoesNotContain(
            collector.Counters,
            c => c.Instrument == "techierag.llm.tokens" && c.Direction is "cache.read" or "cache.write");
    }

    /// <summary>No span is started while the switch is off, whatever listeners exist.</summary>
    [Fact]
    public void StartActivityReturnsNullWhenDisabled()
    {
        TechieRagTelemetry.Enabled = false;

        using var listener = ListenToEverything();

        Assert.Null(TechieRagTelemetry.StartActivity("TechieRag.Probe"));
    }

    /// <summary>With the switch on and a listener attached, a span is produced on the named source.</summary>
    [Fact]
    public void StartActivityProducesASpanWhenEnabled()
    {
        TechieRagTelemetry.Enabled = true;

        using var listener = ListenToEverything();
        using var activity = TechieRagTelemetry.StartActivity("TechieRag.Probe");

        Assert.NotNull(activity);
        Assert.Equal("TechieRag.Probe", activity!.DisplayName);
        Assert.Equal(TechieRagTelemetry.ActivitySourceName, activity.Source.Name);
    }

    /// <summary>
    /// The core library links no telemetry exporter. This is the library-side mirror of the structural
    /// guard the TechieDesk app carries (REQ-NFR-008 / BRD-99): because TechieRag emits only through
    /// BCL primitives, no exporter can reach the app transitively through this package.
    /// </summary>
    /// <remarks>
    /// <b>Still true, and still asserting exactly what it always did, after REQ-RAG-036 shipped
    /// exporters on 2026-07-31.</b> Those exporters live in the separate opt-in <c>TechieRag.Telemetry</c>
    /// package, which is precisely why this test needed no weakening: the assembly under inspection is
    /// the core one, and it gained no OpenTelemetry reference. Had the exporters been folded into the
    /// core instead, this assertion would have had to be softened into something about runtime state —
    /// and a consumer would have lost the ability to prove the absence structurally. If this test ever
    /// starts failing, an exporter has been pulled into the core package and the promise is gone.
    /// </remarks>
    [Fact]
    public void TheLibraryLinksNoTelemetryExporter()
    {
        string[] markers = ["OpenTelemetry", "ApplicationInsights", "Sentry", "Datadog", "NewRelic"];

        var offenders = typeof(TechieRagTelemetry).Assembly
            .GetReferencedAssemblies()
            .Select(assemblyName => assemblyName.Name ?? string.Empty)
            .Where(name => markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "TechieRag must reference no telemetry exporter (REQ-RAG-036): " + string.Join(", ", offenders));
    }

    private static ActivityListener ListenToEverything()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TechieRagTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    /// <summary>Collects measurements from the TechieRag meter that carry a given model tag.</summary>
    private sealed class MeasurementCollector : IDisposable
    {
        private readonly MeterListener listener;
        private readonly string modelTag;
        private readonly List<CounterMeasurement> counters = [];
        private readonly List<HistogramMeasurement> histograms = [];

        public IReadOnlyList<CounterMeasurement> Counters => counters;

        public IReadOnlyList<HistogramMeasurement> Histograms => histograms;

        public MeasurementCollector(string modelTag)
        {
            this.modelTag = modelTag;

            listener = new MeterListener
            {
                InstrumentPublished = (instrument, meterListener) =>
                {
                    if (instrument.Meter.Name == TechieRagTelemetry.MeterName)
                    {
                        meterListener.EnableMeasurementEvents(instrument);
                    }
                }
            };

            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                if (!Matches(tags)) return;
                lock (counters)
                {
                    counters.Add(new CounterMeasurement(instrument.Name, value, TagValue(tags, "techierag.token.direction")));
                }
            });

            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                if (!Matches(tags)) return;
                lock (histograms)
                {
                    histograms.Add(new HistogramMeasurement(instrument.Name, value));
                }
            });

            listener.Start();
        }

        public long Sum(string instrument, string? direction = null)
        {
            lock (counters)
            {
                return counters
                    .Where(c => c.Instrument == instrument && (direction is null || c.Direction == direction))
                    .Sum(c => c.Value);
            }
        }

        public void Dispose() => listener.Dispose();

        private bool Matches(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
            TagValue(tags, "techierag.model") == modelTag;

        private static string? TagValue(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
        {
            foreach (var tag in tags)
            {
                if (tag.Key == key) return tag.Value as string;
            }

            return null;
        }
    }

    private sealed record CounterMeasurement(string Instrument, long Value, string? Direction);

    private sealed record HistogramMeasurement(string Instrument, double Value);
}
