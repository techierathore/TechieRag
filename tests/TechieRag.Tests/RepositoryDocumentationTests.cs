using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace TechieRag.Tests;

/// <summary>
/// Acceptance checks that are statements about the repository's own files rather than about runtime
/// behaviour: YouTube ingestion is gone from the library and its documents (REQ-RAG-107 / BRD-164), and
/// the UsageGuide carries the owner's numbered probe runbook per device (REQ-FN-069 / BRD-165).
/// </summary>
/// <remarks>
/// The files are read from the working tree, found by walking up from the test output to
/// <c>TechieRag.slnx</c>. The rendered <c>docs/*.html</c> files are regenerated separately and are out
/// of scope; the Markdown they are rendered from is checked.
/// </remarks>
public sealed class RepositoryDocumentationTests
{
    private static readonly string[] SourceExtensions = [".cs", ".csproj", ".props", ".targets", ".json", ".md", ".xml", ".razor", ".yml", ".yaml", ".txt"];

    /// <summary>Markers on a document line that records the removal itself rather than describing the feature.</summary>
    private static readonly string[] RemovalMarkers = ["removed", "BRD-164", "BRD-120", "REQ-RAG-107", "REQ-RAG-076", "TR-RAG-015", "~~"];

    /// <summary>
    /// REQ-RAG-107: no file under <c>src/</c> (build output excluded) mentions YouTube, and the shipped
    /// <c>TechieRag</c> assembly has no YouTube type and no YouTube entry point on
    /// <c>WebIngestionExtensions</c>.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-107 SourceHasNoYouTubeTypeOrEntryPoint")]
    public void SourceHasNoYouTubeTypeOrEntryPoint()
    {
        var sourceRoot = Path.Combine(RepoRoot(), "src");
        var mentions = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => SourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("youtube", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(RepoRoot(), path))
            .ToList();
        Assert.True(mentions.Count == 0, "src/ still mentions YouTube: " + string.Join("; ", mentions));

        var library = typeof(TechieRagBuilder).Assembly;
        Assert.DoesNotContain(library.GetTypes(), type => type.Name.Contains("YouTube", StringComparison.OrdinalIgnoreCase));
        var webIngestion = library.GetTypes().Single(type => type.Name == "WebIngestionExtensions");
        Assert.DoesNotContain(
            webIngestion.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name.Contains("YouTube", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// REQ-RAG-107: no line of <c>docs/*.md</c> still describes YouTube ingestion. Allowed are only the
    /// ledger lines that record the removal (they carry a removal marker such as <c>BRD-164</c>, a struck
    /// row, or "removed", plus the acceptance line directly under such a row), the closed TR-RAG-015
    /// feedback entry that the removal closed, and competitor-matrix rows whose TechieRag cell is ❌.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-107 DocsHaveNoYouTubeIngestionSentence")]
    public void DocsHaveNoYouTubeIngestionSentence()
    {
        var offending = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "docs"), "*.md", SearchOption.TopDirectoryOnly).Order())
        {
            var lines = File.ReadAllLines(path);
            var inClosedEntry = false;
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    inClosedEntry = line.StartsWith("### TR-RAG-015", StringComparison.Ordinal);
                }

                if (!line.Contains("youtube", StringComparison.OrdinalIgnoreCase) || inClosedEntry || RecordsTheRemoval(line))
                {
                    continue;
                }

                var isAcceptanceOfARemovalRow = line.TrimStart().StartsWith("- *Acceptance:*", StringComparison.Ordinal)
                    && index > 0 && RecordsTheRemoval(lines[index - 1]);
                if (isAcceptanceOfARemovalRow || IsCompetitorRowTechieRagLacks(line))
                {
                    continue;
                }

                offending.Add($"{Path.GetFileName(path)}:{index + 1}: {line.Trim()[..Math.Min(160, line.Trim().Length)]}");
            }
        }

        Assert.True(offending.Count == 0, "docs/*.md still describe YouTube ingestion:\n" + string.Join("\n", offending));
    }

    /// <summary>
    /// REQ-FN-069: the UsageGuide's Platform notes hold the owner's runbook with a block per device —
    /// Windows, Mac, Android phone, iPhone — each a numbered list whose steps cover build, deploy, run
    /// and record, plus a numbered shared "Record" procedure.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-069 UsageGuideHasANumberedProbeRunbookPerDevice")]
    public void UsageGuideHasANumberedProbeRunbookPerDevice()
    {
        var guide = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "TechieRag-UsageGuide.md")).Replace("\r\n", "\n");
        var platformNotes = Section(guide, "## Platform notes", "\n## ");
        var runbook = Section(platformNotes, "### Owner's runbook", "\n### ");

        foreach (var device in new[] { "Windows", "Mac", "Android phone", "iPhone" })
        {
            var block = DeviceBlock(runbook, device);
            var steps = Regex.Matches(block, @"^(\d+)\. (.+)$", RegexOptions.Multiline);
            Assert.True(steps.Count >= 3, $"{device}: expected a numbered list of steps, found {steps.Count}.");
            Assert.Equal(Enumerable.Range(1, steps.Count).Select(n => n.ToString()), steps.Select(step => step.Groups[1].Value));

            var stepText = string.Join("\n", steps.Select(step => step.Groups[2].Value));
            foreach (var verb in new[] { "Build", "Deploy", "Run", "Record" })
            {
                Assert.True(
                    Regex.IsMatch(stepText, $@"\b{verb}\b", RegexOptions.IgnoreCase),
                    $"{device}: no numbered step covers '{verb}'.");
            }
        }

        var record = DeviceBlock(runbook, "Record");
        Assert.True(Regex.Matches(record, @"^\d+\. ", RegexOptions.Multiline).Count >= 2, "The shared Record procedure is not a numbered list.");
    }

    private static bool RecordsTheRemoval(string line) =>
        RemovalMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsCompetitorRowTechieRagLacks(string line)
    {
        if (!line.TrimStart().StartsWith('|')) return false;

        var cells = line.Trim().Trim('|').Split('|');
        return cells.Length >= 3 && cells[1].Trim().StartsWith('❌');
    }

    private static string Section(string text, string heading, string nextHeading)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Heading '{heading}' not found.");
        var end = text.IndexOf(nextHeading, start + heading.Length, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    private static string DeviceBlock(string runbook, string device)
    {
        var header = Regex.Match(runbook, @"^\*\*" + Regex.Escape(device) + @"\b[^\n]*\*\*\s*$", RegexOptions.Multiline);
        Assert.True(header.Success, $"No runbook block for '{device}'.");
        var next = Regex.Match(runbook[(header.Index + header.Length)..], @"^\*\*[^\n]+\*\*\s*$", RegexOptions.Multiline);
        return next.Success
            ? runbook.Substring(header.Index + header.Length, next.Index)
            : runbook[(header.Index + header.Length)..];
    }

    private static bool IsBuildOutput(string path)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return path.Split(separators).Any(segment => segment is "bin" or "obj");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechieRag.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root (TechieRag.slnx) not found above the test output.");
    }
}
