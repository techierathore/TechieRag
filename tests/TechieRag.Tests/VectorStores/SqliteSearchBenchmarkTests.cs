using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace TechieRag.Tests.VectorStores;

/// <summary>
/// Gate for the SQLite search benchmark (REQ-RAG-056): it seeds 50,000 chunks, so it runs only when
/// <see cref="VariableName"/> is set to <c>1</c>.
/// </summary>
public sealed class SearchBenchmarkFactAttribute : FactAttribute
{
    /// <summary>The environment variable that switches the benchmark on.</summary>
    public const string VariableName = "TechieRagSearchBenchmark";

    /// <summary>Initializes a new instance of the <see cref="SearchBenchmarkFactAttribute"/> class.</summary>
    public SearchBenchmarkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(VariableName) != "1")
        {
            Skip = $"The SQLite search benchmark seeds 50,000 chunks; set {VariableName}=1 to run it.";
        }
    }
}

/// <summary>
/// Times <c>SqliteVecStore.SearchAsync</c> against the old path at 1,000, 10,000 and 50,000 chunks
/// and asserts identical ranking (REQ-RAG-056 / BRD-93).
/// </summary>
/// <remarks>
/// Writes <c>tests/.artifacts/benchmarks/sqlite-search.md</c> and <c>.json</c>; the UsageGuide's
/// table is copied from there. Each figure is the median of five searches after one warm-up, over
/// a file-backed database, top-10, at 1024 dimensions (bge-m3) and 384 (MiniLM).
/// </remarks>
public class SqliteSearchBenchmarkTests
{
    private const int Runs = 5;
    private const int TopK = 10;
    private readonly ITestOutputHelper output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">Where the table is echoed.</param>
    public SqliteSearchBenchmarkTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// Seeds 1k, 10k and 50k chunks at two widths, times both paths, and requires the same top-10.
    /// </summary>
    [SearchBenchmarkFact(DisplayName = "REQ-RAG-056 SearchTimingsAt1k10k50k")]
    public async Task SearchTimingsAt1k10k50k()
    {
        var rows = new List<string>();
        var json = new List<string>();

        foreach (var dimensions in new[] { 1024, 384 })
        {
            foreach (var count in new[] { 1_000, 10_000, 50_000 })
            {
                using var corpus = await SqliteSearchCorpus.CreateAsync(count, dimensions);
                var query = SqliteSearchCorpus.RandomVector(new Random(count + dimensions), dimensions);

                // Warm-up (page cache, JIT) for both paths, and the ranking comparison.
                var legacyTop = await LegacySqliteSimilaritySearch.SearchAsync(corpus.ConnectionString, query, TopK);
                var newTop = await corpus.Store.SearchAsync(query, TopK);
                var identical = legacyTop.Select(r => r.Id).SequenceEqual(newTop.Select(r => r.Chunk.Id));
                Assert.True(identical, $"Ranking differs at {count} chunks, {dimensions} dimensions.");

                var legacyMs = await MedianAsync(() => LegacySqliteSimilaritySearch.SearchAsync(corpus.ConnectionString, query, TopK));
                var newMs = await MedianAsync(() => corpus.Store.SearchAsync(query, TopK));

                rows.Add(string.Create(CultureInfo.InvariantCulture,
                    $"| {count:N0} | {dimensions} | {legacyMs:F1} | {newMs:F1} | {legacyMs / newMs:F1}x | identical |"));
                json.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{{\"chunks\":{count},\"dimensions\":{dimensions},\"oldMs\":{legacyMs:F2},\"newMs\":{newMs:F2},\"identicalTop{TopK}\":{identical.ToString().ToLowerInvariant()}}}"));
            }
        }

        var header = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"SQLite managed search benchmark, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
            .AppendLine(CultureInfo.InvariantCulture, $"Host: {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical CPUs, .NET {Environment.Version}, SIMD width {System.Numerics.Vector<float>.Count} floats")
            .AppendLine(CultureInfo.InvariantCulture, $"Median of {Runs} top-{TopK} searches after one warm-up; file-backed database.")
            .AppendLine()
            .AppendLine("| Chunks | Dimensions | Old path (ms) | New path (ms) | Speed-up | Top-10 ranking |")
            .AppendLine("|---:|---:|---:|---:|---:|---|");
        foreach (var row in rows)
        {
            header.AppendLine(row);
        }

        var folder = ArtifactsFolder();
        await File.WriteAllTextAsync(Path.Combine(folder, "sqlite-search.md"), header.ToString());
        await File.WriteAllTextAsync(Path.Combine(folder, "sqlite-search.json"), "[" + string.Join(",", json) + "]");
        output.WriteLine(header.ToString());
    }

    private static async Task<double> MedianAsync<T>(Func<Task<T>> search)
    {
        var timings = new double[Runs];
        for (var i = 0; i < Runs; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            await search();
            timings[i] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(timings);
        return timings[Runs / 2];
    }

    private static string ArtifactsFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? Path.GetTempPath();
        var folder = Path.Combine(root, "tests", ".artifacts", "benchmarks");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
