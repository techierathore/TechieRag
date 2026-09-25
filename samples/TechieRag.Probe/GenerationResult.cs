using System.Globalization;

namespace TechieRag.Probe;

/// <summary>
/// What one press of the probe's local-model button measured (REQ-FN-059).
/// </summary>
/// <param name="ModelName">The local model the platform default picked.</param>
/// <param name="Sentence">The generated sentence.</param>
/// <param name="LoadMs">Terms, download (first run only) and load.</param>
/// <param name="TimeToFirstTokenMs">From the request to the first text.</param>
/// <param name="TokensPerSecond">Tokens after the first, per second.</param>
/// <param name="OutputTokens">Tokens generated.</param>
/// <param name="PeakMemoryBytes">The process's peak memory.</param>
public sealed record GenerationResult(
    string ModelName,
    string Sentence,
    double LoadMs,
    double TimeToFirstTokenMs,
    double TokensPerSecond,
    int OutputTokens,
    long PeakMemoryBytes)
{
    /// <summary>
    /// Bytes per megabyte: decimal, the same unit <c>ModelDownloadService.FormatBytes</c> uses for the
    /// download line on the same screen (before 2026-09-25 this was binary, 1,048,576).
    /// </summary>
    private const double BytesPerMegabyte = 1_000_000d;

    /// <summary>Peak memory in decimal megabytes, as shown on screen and in the result line.</summary>
    public double PeakMemoryMb => PeakMemoryBytes / BytesPerMegabyte;

    /// <summary>The timings as shown on screen.</summary>
    public string Timings => string.Create(CultureInfo.InvariantCulture,
        $"load {LoadMs:F0} ms · first token {TimeToFirstTokenMs:F0} ms · {TokensPerSecond:F1} tokens/s · peak {PeakMemoryMb:F0} MB");

    /// <summary>One line for logs and automation.</summary>
    /// <returns>The line, without the prefix.</returns>
    public string ToLine() => string.Create(CultureInfo.InvariantCulture,
        $"OK generate model={ModelName} sentence=\"{Sentence}\" loadMs={LoadMs:F0} ttftMs={TimeToFirstTokenMs:F0} tokensPerSecond={TokensPerSecond:F1} outputTokens={OutputTokens} peakMb={PeakMemoryMb:F0}");
}
