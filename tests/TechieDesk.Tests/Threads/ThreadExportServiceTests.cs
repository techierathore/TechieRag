using TechieDesk.Services.Files;
using TechieDesk.Services.Threads;
using TechieDesk.Tests.Support;
using TechieRag.Models;
using Xunit;

namespace TechieDesk.Tests.Threads;

/// <summary>
/// REQ-FN-010 (BRD-35): thread export writes a real file through the platform save service, and
/// reports success ONLY when that file exists. The defect these cover is the browser-blob export
/// that toasted "Exported …" while WKWebView silently dropped the download and wrote nothing.
/// </summary>
public sealed class ThreadExportServiceTests : IDisposable
{
    private readonly string workingDirectory =
        Path.Combine(Path.GetTempPath(), "techiedesk-export-tests", Guid.NewGuid().ToString("N"));

    private readonly ThreadExporter exporter = new();
    private readonly ResourceHarness resources = new("en");

    /// <summary>Creates the per-test scratch directory the fake save services write into.</summary>
    public ThreadExportServiceTests() => Directory.CreateDirectory(workingDirectory);

    /// <summary>Removes the per-test scratch directory.</summary>
    public void Dispose()
    {
        resources.Dispose();

        if (Directory.Exists(workingDirectory))
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Builds a representative thread with a user question and a cited assistant answer.
    /// </summary>
    private static (ConversationThread Thread, List<StoredChatMessage> Messages) BuildSample()
    {
        var thread = new ConversationThread
        {
            ThreadId = "thread-1",
            UserId = "user-1",
            WorkspaceId = "ws-1",
            Title = "Contract liability questions",
            CreatedAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 7, 2, 10, 30, 0, DateTimeKind.Utc)
        };

        var source = new SearchResult
        {
            Score = 0.91f,
            Chunk = new TextChunk
            {
                DocumentId = "doc-7",
                Text = "Liability is capped at the fees paid in the preceding twelve months.",
                Metadata = new Dictionary<string, object> { ["DocumentName"] = "MSA.pdf" }
            }
        };

        var messages = new List<StoredChatMessage>
        {
            new()
            {
                ThreadId = thread.ThreadId,
                Role = "user",
                Content = "What do the contracts say about liability?",
                CreatedAt = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                ThreadId = thread.ThreadId,
                Role = "assistant",
                Content = "Liability is capped at twelve months of fees.",
                Sources = new List<SearchResult> { source },
                CreatedAt = new DateTime(2026, 7, 2, 10, 1, 0, DateTimeKind.Utc)
            }
        };

        return (thread, messages);
    }

    private ThreadExportService BuildService(IFileSaveService saveService) =>
        new(exporter, saveService, resources.Localize);

    /// <summary>
    /// Exporting as Markdown through a save service that writes the file reports Saved, names the
    /// path, and leaves a non-empty .md file on disk.
    /// </summary>
    [Fact]
    public async Task ExportWritesMarkdownFileAndReportsSaved()
    {
        var (thread, messages) = BuildSample();
        var saver = new WritingFileSaveService(workingDirectory);

        var outcome = await BuildService(saver).ExportAsync(thread, messages, ThreadExportFormat.Markdown);

        Assert.Equal(FileSaveStatus.Saved, outcome.Status);
        Assert.True(outcome.IsSuccess);
        Assert.NotNull(outcome.FilePath);
        Assert.True(File.Exists(outcome.FilePath));
        Assert.EndsWith(".md", outcome.FilePath, StringComparison.Ordinal);
        Assert.True(outcome.BytesWritten > 0);
    }

    /// <summary>
    /// Exporting as JSON through a save service that writes the file reports Saved and leaves a
    /// non-empty .json file on disk.
    /// </summary>
    [Fact]
    public async Task ExportWritesJsonFileAndReportsSaved()
    {
        var (thread, messages) = BuildSample();
        var saver = new WritingFileSaveService(workingDirectory);

        var outcome = await BuildService(saver).ExportAsync(thread, messages, ThreadExportFormat.Json);

        Assert.Equal(FileSaveStatus.Saved, outcome.Status);
        Assert.True(File.Exists(outcome.FilePath));
        Assert.EndsWith(".json", outcome.FilePath, StringComparison.Ordinal);
        Assert.True(outcome.BytesWritten > 0);
    }

    /// <summary>
    /// The bytes that reach disk are byte-for-byte what ThreadExporter produced, for both formats,
    /// so the rewrite of the DELIVERY path did not disturb the exported content.
    /// </summary>
    [Fact]
    public async Task ExportedFileContentMatchesExporterOutput()
    {
        var (thread, messages) = BuildSample();
        var saver = new WritingFileSaveService(workingDirectory);
        var service = BuildService(saver);

        var markdown = await service.ExportAsync(thread, messages, ThreadExportFormat.Markdown);
        var json = await service.ExportAsync(thread, messages, ThreadExportFormat.Json);

        Assert.Equal(exporter.ToMarkdown(thread, messages), await File.ReadAllTextAsync(markdown.FilePath!));
        Assert.Equal(exporter.ToJson(thread, messages), await File.ReadAllTextAsync(json.FilePath!));
    }

    /// <summary>
    /// Dismissing the save panel yields Cancelled: no success toast, no error toast, and no file —
    /// the caller's toast is gated on IsSuccess, which is false here.
    /// </summary>
    [Fact]
    public async Task ExportRaisesNoSuccessWhenSavePanelCancelled()
    {
        var (thread, messages) = BuildSample();

        var outcome = await BuildService(new CancellingFileSaveService())
            .ExportAsync(thread, messages, ThreadExportFormat.Markdown);

        Assert.Equal(FileSaveStatus.Cancelled, outcome.Status);
        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.IsError);
        Assert.Equal(string.Empty, outcome.Message);
        Assert.Null(outcome.FilePath);
        Assert.Empty(Directory.GetFiles(workingDirectory));
    }

    /// <summary>
    /// THE DEFECT, pinned: a save service that claims success while writing nothing — exactly what
    /// the WKWebView blob anchor did — is downgraded to Failed, never reported as a success.
    /// </summary>
    [Fact]
    public async Task ExportReportsFailureWhenSaverClaimsSuccessButWritesNoFile()
    {
        var (thread, messages) = BuildSample();
        var phantomPath = Path.Combine(workingDirectory, "never-written.md");

        var outcome = await BuildService(new LyingFileSaveService(phantomPath))
            .ExportAsync(thread, messages, ThreadExportFormat.Markdown);

        Assert.Equal(FileSaveStatus.Failed, outcome.Status);
        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.IsError);
        Assert.False(File.Exists(phantomPath));
    }

    /// <summary>
    /// A save that produces a zero-byte file is a failure, not a success — an empty export is not
    /// a transcript the user can use.
    /// </summary>
    [Fact]
    public async Task ExportReportsFailureWhenWrittenFileIsEmpty()
    {
        var (thread, messages) = BuildSample();
        var emptyPath = Path.Combine(workingDirectory, "empty.md");
        await File.WriteAllTextAsync(emptyPath, string.Empty);

        var outcome = await BuildService(new LyingFileSaveService(emptyPath))
            .ExportAsync(thread, messages, ThreadExportFormat.Markdown);

        Assert.Equal(FileSaveStatus.Failed, outcome.Status);
        Assert.False(outcome.IsSuccess);
    }

    /// <summary>
    /// A save service that throws surfaces as Failed with its reason, not as an unhandled exception
    /// and not as a success.
    /// </summary>
    [Fact]
    public async Task ExportReportsFailureWhenSaverThrows()
    {
        var (thread, messages) = BuildSample();

        var outcome = await BuildService(new ThrowingFileSaveService())
            .ExportAsync(thread, messages, ThreadExportFormat.Json);

        Assert.Equal(FileSaveStatus.Failed, outcome.Status);
        Assert.False(outcome.IsSuccess);
        Assert.Contains("disk is on fire", outcome.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// On a head with no native save panel registered — today's Windows head, which has no
    /// Platforms/Windows sources — the fallback reports Failed rather than silently writing nothing.
    /// </summary>
    [Fact]
    public async Task ExportReportsFailureOnHeadWithoutNativeSavePanel()
    {
        var (thread, messages) = BuildSample();

        var outcome = await BuildService(new UnsupportedFileSaveService())
            .ExportAsync(thread, messages, ThreadExportFormat.Markdown);

        Assert.Equal(FileSaveStatus.Failed, outcome.Status);
        Assert.False(outcome.IsSuccess);
        Assert.True(outcome.IsError);
    }

    /// <summary>
    /// The suggested file name handed to the save panel carries the format's extension, so the
    /// panel pre-fills a usable name.
    /// </summary>
    [Fact]
    public async Task ExportSuggestsFileNameWithFormatExtension()
    {
        var (thread, messages) = BuildSample();
        var saver = new WritingFileSaveService(workingDirectory);

        await BuildService(saver).ExportAsync(thread, messages, ThreadExportFormat.Json);

        Assert.Equal(exporter.BuildFileName(thread, "json"), saver.LastSuggestedFileName);
        Assert.Equal("application/json", saver.LastContentType);
    }

    /// <summary>A save service that behaves like a real save panel: it writes the file.</summary>
    private sealed class WritingFileSaveService(string directory) : IFileSaveService
    {
        public bool IsSupported => true;

        public string? LastSuggestedFileName { get; private set; }

        public string? LastContentType { get; private set; }

        public async Task<FileSaveResult> SaveTextAsync(
            string suggestedFileName,
            string contentType,
            string content,
            CancellationToken cancellationToken = default)
        {
            LastSuggestedFileName = suggestedFileName;
            LastContentType = contentType;

            var path = Path.Combine(directory, suggestedFileName);
            await File.WriteAllTextAsync(path, content, cancellationToken);
            return FileSaveResult.Saved(path, new FileInfo(path).Length);
        }
    }

    /// <summary>A save service standing in for a user who dismissed the panel.</summary>
    private sealed class CancellingFileSaveService : IFileSaveService
    {
        public bool IsSupported => true;

        public Task<FileSaveResult> SaveTextAsync(
            string suggestedFileName,
            string contentType,
            string content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FileSaveResult.Cancelled());
    }

    /// <summary>A save service that reports success for a path it never wrote — the original defect.</summary>
    private sealed class LyingFileSaveService(string claimedPath) : IFileSaveService
    {
        public bool IsSupported => true;

        public Task<FileSaveResult> SaveTextAsync(
            string suggestedFileName,
            string contentType,
            string content,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(FileSaveResult.Saved(claimedPath, 566));
    }

    /// <summary>A save service whose write blows up.</summary>
    private sealed class ThrowingFileSaveService : IFileSaveService
    {
        public bool IsSupported => true;

        public Task<FileSaveResult> SaveTextAsync(
            string suggestedFileName,
            string contentType,
            string content,
            CancellationToken cancellationToken = default) =>
            throw new IOException("the disk is on fire");
    }
}
