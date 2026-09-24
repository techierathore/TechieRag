using System.Globalization;

namespace TechieRag.Probe;

/// <summary>
/// What one press of the probe's button measured.
/// </summary>
/// <param name="Platform">The platform the probe ran on.</param>
/// <param name="ModelName">The embedding model <c>UseEmbedded()</c> picked here.</param>
/// <param name="ModelDirectory">Where the model files are, under the model root.</param>
/// <param name="TopResult">The text of the best search hit.</param>
/// <param name="TopScore">Its cosine similarity.</param>
/// <param name="LoadMs">Model download (first run only) and load.</param>
/// <param name="EmbedMs">Embedding the three texts.</param>
/// <param name="StoreMs">Storing them in SQLite.</param>
/// <param name="SearchMs">Embedding the query and searching.</param>
public sealed record ProbeResult(
    string Platform,
    string ModelName,
    string ModelDirectory,
    string TopResult,
    float TopScore,
    double LoadMs,
    double EmbedMs,
    double StoreMs,
    double SearchMs)
{
    /// <summary>The timings as shown on screen.</summary>
    public string Timings => string.Create(CultureInfo.InvariantCulture,
        $"load {LoadMs:F0} ms · embed {EmbedMs:F0} ms · store {StoreMs:F0} ms · search {SearchMs:F0} ms");

    /// <summary>One line for logs and automation.</summary>
    /// <returns>The line, without the prefix.</returns>
    public string ToLine() => string.Create(CultureInfo.InvariantCulture,
        $"OK platform={Platform} model={ModelName} top=\"{TopResult}\" score={TopScore:F4} loadMs={LoadMs:F0} embedMs={EmbedMs:F0} storeMs={StoreMs:F0} searchMs={SearchMs:F0} modelDir=\"{ModelDirectory}\"");
}
