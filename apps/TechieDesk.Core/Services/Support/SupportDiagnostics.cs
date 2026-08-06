using TechieDeskDb;

namespace TechieDesk.Services.Support;

/// <summary>
/// Builds the optional diagnostics block a user may attach to a support issue (REQ-UI-032).
/// </summary>
/// <remarks>
/// The Support screen promises exactly this and nothing more: "app version, OS, and the last 200
/// log lines. No document content or chat text is included." That promise is kept here by
/// construction — the only file this type ever reads is the newest rolling log under
/// <see cref="DataDirectory.LogDirectoryName"/>, never the app database, never the RAG store.
/// <para>
/// <b>REQ-UI-055 / BRD-91 — classified machine-facing, invariant English.</b> This block is not read
/// by the person who submits it. It is appended to the issue body, posted to AppManager and read by
/// a TechieDesk support engineer triaging a ticket, alongside 200 lines of log output that are
/// themselves English by construction — a Hindi <c>ऐप संस्करण:</c> in front of an English stack trace
/// would translate the label and leave the evidence, which helps nobody and makes the field
/// ungreppable across tickets. The block is diagnostic wire format; the Support SCREEN around it —
/// the checkbox, its explanation, the submit button — is ordinary UI and is localized in the razor
/// tree where it lives.
/// </para>
/// </remarks>
public static class SupportDiagnostics
{
    /// <summary>Number of trailing log lines included.</summary>
    public const int MaxLogLines = 200;

    /// <summary>Heading that opens the diagnostics block.</summary>
    public const string Heading = "--- Diagnostics ---";

    /// <summary>
    /// Reads the tail of the newest log file in a log directory.
    /// </summary>
    /// <param name="logDirectory">Absolute path of the log directory; it need not exist.</param>
    /// <param name="maxLines">Most lines to return, from the end of the file.</param>
    /// <returns>The trailing lines, oldest first; empty when there is no readable log.</returns>
    /// <remarks>
    /// Streams the file through a bounded queue rather than reading it whole: a rolling daily log on
    /// a busy install is tens of megabytes, and loading all of it to keep 200 lines would stall the
    /// window for the sake of text that is thrown away.
    /// </remarks>
    public static IReadOnlyList<string> ReadRecentLogLines(string logDirectory, int maxLines = MaxLogLines)
    {
        if (maxLines <= 0 || !Directory.Exists(logDirectory))
        {
            return Array.Empty<string>();
        }

        FileInfo? newest = null;
        foreach (var path in Directory.EnumerateFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly))
        {
            var candidate = new FileInfo(path);
            if (newest is null || candidate.LastWriteTimeUtc > newest.LastWriteTimeUtc)
            {
                newest = candidate;
            }
        }

        if (newest is null)
        {
            return Array.Empty<string>();
        }

        var tail = new Queue<string>(maxLines);
        try
        {
            using var stream = new FileStream(
                newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (tail.Count == maxLines)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(line);
            }
        }
        catch (IOException)
        {
            // A log rolling over mid-read must not fail the issue submission; whatever was read
            // already is still useful, and an empty tail is reported honestly by Build.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale.
        }

        return tail.ToArray();
    }

    /// <summary>
    /// Formats the diagnostics block from already-gathered facts.
    /// </summary>
    /// <param name="appVersion">The running application version.</param>
    /// <param name="operatingSystem">A description of the host OS.</param>
    /// <param name="logLines">The trailing log lines, oldest first.</param>
    /// <returns>The block to append to the issue description.</returns>
    public static string Build(string appVersion, string operatingSystem, IReadOnlyList<string> logLines)
    {
        ArgumentNullException.ThrowIfNull(logLines);

        var lines = new List<string>
        {
            Heading,
            $"App version: {appVersion}",
            $"Operating system: {operatingSystem}"
        };

        if (logLines.Count == 0)
        {
            lines.Add("Recent log lines: none available.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add($"Recent log lines ({logLines.Count}):");
        lines.AddRange(logLines);
        return string.Join(Environment.NewLine, lines);
    }
}
