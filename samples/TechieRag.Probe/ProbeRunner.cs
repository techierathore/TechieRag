using System.Diagnostics;
using TechieRag.Embedded;
using TechieRag.Models;
using TechieRag.VectorStores;

namespace TechieRag.Probe;

/// <summary>
/// The probe's one measured action: embed three texts, store them, search, report the top hit and
/// the timings (REQ-FN-056 / BRD-94).
/// </summary>
/// <remarks>
/// <para>The embedder is <see cref="EmbeddedEmbeddingProvider.CreateDefault"/>, the same selection
/// <c>UseEmbedded()</c> makes: bge-m3 on Windows and Mac Catalyst, all-MiniLM-L6-v2 on Android and
/// iOS (REQ-RAG-054). No model root is set, so the files land in the per-user application data
/// folder (REQ-RAG-053). The components are used directly rather than through
/// <c>IngestTextAsync</c> so embedding and storing are timed separately.</para>
/// </remarks>
public sealed class ProbeRunner
{
    /// <summary>The three texts that are embedded and stored.</summary>
    public static readonly IReadOnlyList<string> Texts =
    [
        "Paris is the capital city of France.",
        "Bicycles should have their chains oiled regularly.",
        "The Pacific is the largest ocean on Earth."
    ];

    /// <summary>The search query; the Paris sentence is the expected top result.</summary>
    public const string Query = "What is the capital of France?";

    private EmbeddedEmbeddingProvider? provider;

    /// <summary>Runs the probe once.</summary>
    /// <param name="databaseFolder">A writable folder for the SQLite file.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The measurements.</returns>
    public async Task<ProbeResult> RunAsync(string databaseFolder, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        provider ??= EmbeddedEmbeddingProvider.CreateDefault();
        await provider.InitializeAsync(cancellationToken);
        var loadMs = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        var vectors = await provider.EmbedBatchAsync(Texts, cancellationToken);
        var embedMs = stopwatch.Elapsed.TotalMilliseconds;

        var databasePath = Path.Combine(databaseFolder, "probe.db");
        var store = new SqliteVecStore($"Data Source={databasePath};Pooling=False", provider.Dimensions);
        await store.ClearAsync(cancellationToken);

        stopwatch.Restart();
        var chunks = Texts.Select((text, i) => new TextChunk
        {
            Id = $"probe-{i}",
            DocumentId = "probe",
            Text = text,
            Vector = vectors[i],
            ChunkIndex = i,
            Metadata = new Dictionary<string, object> { ["DocumentName"] = "Probe", ["SourcePath"] = "probe" }
        });
        await store.UpsertBatchAsync(chunks, cancellationToken);
        var storeMs = stopwatch.Elapsed.TotalMilliseconds;

        stopwatch.Restart();
        var queryVector = await provider.EmbedAsync(Query, cancellationToken);
        var hits = await store.SearchAsync(queryVector, topK: 1, cancellationToken: cancellationToken);
        var searchMs = stopwatch.Elapsed.TotalMilliseconds;

        var top = hits.Count > 0 ? hits[0] : throw new InvalidOperationException("The search returned nothing.");
        return new ProbeResult(
            DeviceInfo.Current.Platform + " " + DeviceInfo.Current.VersionString + " (" + DeviceInfo.Current.Model + ")",
            provider.ModelName,
            provider.Model?.GetModelDirectory() ?? ModelRoot.Current,
            top.Chunk.Text,
            top.Score,
            loadMs,
            embedMs,
            storeMs,
            searchMs);
    }
}
