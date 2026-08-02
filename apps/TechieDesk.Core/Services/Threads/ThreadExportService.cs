using TechieDesk.Services.Files;
using TechieRag.Models;

namespace TechieDesk.Services.Threads;

/// <summary>
/// Exports a conversation thread to a real file on disk through the platform's native save panel
/// (REQ-FN-010, BRD-35).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Joins the pure <see cref="ThreadExporter"/> serializers to an
/// <see cref="IFileSaveService"/> and — critically — VERIFIES the write before reporting success.
/// The defect this replaces raised "Exported …" unconditionally after a WebView blob-anchor click
/// that MAUI's BlazorWebView never handles, so the user was told the export worked and got no file.
/// Here, <see cref="ThreadExportOutcome.IsSuccess"/> is true only after <c>File.Exists</c> and a
/// non-zero length have been confirmed at the path the save service returned; a save service that
/// claims success without producing a file is downgraded to a failure.</para>
/// <para><b>Threading:</b> deliberately no <c>ConfigureAwait(false)</c>. The save panel is presented
/// on the UI thread and the caller is a Blazor component that raises a toast on the continuation, so
/// the synchronization context is kept.</para>
/// </remarks>
public sealed class ThreadExportService
{
    private readonly ThreadExporter exporter;
    private readonly IFileSaveService fileSaveService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreadExportService"/> class.
    /// </summary>
    /// <param name="exporter">The serializer that renders a thread to Markdown or JSON.</param>
    /// <param name="fileSaveService">The platform save panel that writes the file.</param>
    /// <exception cref="ArgumentNullException">Thrown when any dependency is null.</exception>
    public ThreadExportService(ThreadExporter exporter, IFileSaveService fileSaveService)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(fileSaveService);

        this.exporter = exporter;
        this.fileSaveService = fileSaveService;
    }

    /// <summary>
    /// Serializes a thread and asks the user where to save it, then confirms the file landed.
    /// </summary>
    /// <param name="thread">The thread being exported.</param>
    /// <param name="messages">The thread's messages in chronological order.</param>
    /// <param name="format">The export format.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// An outcome whose <see cref="ThreadExportOutcome.IsSuccess"/> is true only when a non-empty
    /// file exists at <see cref="ThreadExportOutcome.FilePath"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="thread"/> or
    /// <paramref name="messages"/> is null.</exception>
    public async Task<ThreadExportOutcome> ExportAsync(
        ConversationThread thread,
        IReadOnlyList<StoredChatMessage> messages,
        ThreadExportFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(messages);

        var isMarkdown = format == ThreadExportFormat.Markdown;
        var content = isMarkdown ? exporter.ToMarkdown(thread, messages) : exporter.ToJson(thread, messages);
        var fileName = exporter.BuildFileName(thread, isMarkdown ? "md" : "json");
        var contentType = isMarkdown ? "text/markdown" : "application/json";

        FileSaveResult result;
        try
        {
            result = await fileSaveService.SaveTextAsync(fileName, contentType, content, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Cancelled();
        }
        catch (Exception ex)
        {
            return Failure($"Export failed: {ex.Message}");
        }

        if (result.Status == FileSaveStatus.Cancelled)
        {
            return Cancelled();
        }

        if (result.Status == FileSaveStatus.Failed)
        {
            return Failure(result.ErrorMessage ?? "The export could not be saved.");
        }

        return Confirm(thread, result, isMarkdown);
    }

    /// <summary>
    /// Downgrades a claimed save to a failure unless a non-empty file really exists at the path.
    /// </summary>
    private static ThreadExportOutcome Confirm(ConversationThread thread, FileSaveResult result, bool isMarkdown)
    {
        if (string.IsNullOrWhiteSpace(result.FilePath) || !File.Exists(result.FilePath))
        {
            return Failure("The export reported success but no file was written.");
        }

        var length = new FileInfo(result.FilePath).Length;
        if (length == 0)
        {
            return Failure("The export produced an empty file.");
        }

        var label = isMarkdown ? "Markdown" : "JSON";
        return new ThreadExportOutcome
        {
            Status = FileSaveStatus.Saved,
            FilePath = result.FilePath,
            BytesWritten = length,
            Message = $"Exported \"{thread.Title}\" as {label} to {result.FilePath}."
        };
    }

    private static ThreadExportOutcome Cancelled() =>
        new() { Status = FileSaveStatus.Cancelled, Message = string.Empty };

    private static ThreadExportOutcome Failure(string message) =>
        new() { Status = FileSaveStatus.Failed, Message = message };
}
