using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace TechieRag.Embedded;

/// <summary>
/// Tracks model download progress and state for UI display.
/// </summary>
public class ModelDownloadProgress
{
    /// <summary>Current download status.</summary>
    public ModelDownloadStatus Status { get; set; } = ModelDownloadStatus.NotStarted;

    /// <summary>Name of the model being fetched (for example <c>bge-m3</c>).</summary>
    public string? ModelName { get; set; }

    /// <summary>Name of file currently being downloaded.</summary>
    public string? CurrentFile { get; set; }

    /// <summary>Size description of current file (e.g., "2.27 GB").</summary>
    public string? CurrentFileSize { get; set; }

    /// <summary>Total number of files to download.</summary>
    public int TotalFiles { get; set; }

    /// <summary>Number of files already downloaded.</summary>
    public int CompletedFiles { get; set; }

    /// <summary>Bytes downloaded for current file.</summary>
    public long CurrentFileBytesDownloaded { get; set; }

    /// <summary>Total bytes for current file (if known).</summary>
    public long CurrentFileTotalBytes { get; set; }

    /// <summary>
    /// Bytes this download has to transfer in total, known before the first byte (REQ-RAG-055).
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>Bytes transferred so far across every file of this download.</summary>
    public long BytesDownloaded { get; set; }

    /// <summary>Overall progress percentage (0-100), by files completed.</summary>
    public int OverallProgressPercent => TotalFiles > 0
        ? (int)((CompletedFiles / (double)TotalFiles) * 100)
        : 0;

    /// <summary>Overall progress percentage (0-100), by bytes transferred.</summary>
    public int OverallBytesProgressPercent => TotalBytes > 0
        ? (int)Math.Min(100, BytesDownloaded / (double)TotalBytes * 100)
        : 0;

    /// <summary>Current file progress percentage (0-100).</summary>
    public int CurrentFileProgressPercent => CurrentFileTotalBytes > 0
        ? (int)((CurrentFileBytesDownloaded / (double)CurrentFileTotalBytes) * 100)
        : 0;

    /// <summary>Human-readable status message.</summary>
    public string StatusMessage => Status switch
    {
        ModelDownloadStatus.NotStarted => "Model not downloaded",
        ModelDownloadStatus.Checking => "Checking for model...",
        ModelDownloadStatus.Downloading => $"Downloading {CurrentFile} ({CurrentFileSize})... {CurrentFileProgressPercent}%",
        ModelDownloadStatus.Completed => "Model ready",
        ModelDownloadStatus.Failed => "Download failed",
        _ => "Unknown status"
    };

    /// <summary>Error message if download failed.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Model download status values.
/// </summary>
public enum ModelDownloadStatus
{
    /// <summary>Model download has not started.</summary>
    NotStarted,
    /// <summary>Checking if model is already downloaded.</summary>
    Checking,
    /// <summary>Model files are being downloaded.</summary>
    Downloading,
    /// <summary>Model download completed successfully.</summary>
    Completed,
    /// <summary>Model download failed.</summary>
    Failed
}

/// <summary>
/// One file of a model download: where it comes from, what it is called on disk, how big it is.
/// </summary>
/// <param name="FileName">The file name inside the model folder.</param>
/// <param name="Url">The absolute URL it is fetched from.</param>
/// <param name="ExpectedBytes">
/// Its size. It is what the size-known event reports before the first byte, and a file on disk at
/// 95 percent of it or more counts as already downloaded.
/// </param>
public sealed record ModelDownloadFile(string FileName, Uri Url, long ExpectedBytes);

/// <summary>
/// Raised once per download, after the size is known and before the first byte is requested
/// (REQ-RAG-055 / BRD-92).
/// </summary>
/// <remarks>
/// A host that must ask the user first (a phone on a metered network) sets <see cref="Decline"/>;
/// the download then stops before any request is sent and throws
/// <see cref="ModelDownloadDeclinedException"/>. Handlers run synchronously on the downloading thread.
/// </remarks>
public sealed class ModelDownloadSizeEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="modelName">The model being fetched.</param>
    /// <param name="totalBytes">Bytes still to transfer.</param>
    /// <param name="fileCount">Files still to transfer.</param>
    /// <param name="destinationDirectory">The folder the files land in.</param>
    public ModelDownloadSizeEventArgs(string modelName, long totalBytes, int fileCount, string destinationDirectory)
    {
        ModelName = modelName;
        TotalBytes = totalBytes;
        FileCount = fileCount;
        DestinationDirectory = destinationDirectory;
    }

    /// <summary>The model being fetched.</summary>
    public string ModelName { get; }

    /// <summary>Bytes still to transfer (a resumed file counts only its missing part).</summary>
    public long TotalBytes { get; }

    /// <summary>The size as text, for example "2.3 GB".</summary>
    public string DisplaySize => ModelDownloadService.FormatBytes(TotalBytes);

    /// <summary>Files still to transfer.</summary>
    public int FileCount { get; }

    /// <summary>The folder the files land in, under the model root.</summary>
    public string DestinationDirectory { get; }

    /// <summary>Set to <see langword="true"/> to stop the download before its first byte.</summary>
    public bool Decline { get; set; }
}

/// <summary>
/// Thrown when a <see cref="ModelDownloadService.DownloadSizeKnown"/> handler declined a download.
/// </summary>
public sealed class ModelDownloadDeclinedException : OperationCanceledException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="modelName">The model whose download was declined.</param>
    /// <param name="totalBytes">The size that was declined.</param>
    public ModelDownloadDeclinedException(string modelName, long totalBytes)
        : base($"The download of '{modelName}' ({ModelDownloadService.FormatBytes(totalBytes)}) was declined by the host.")
    {
        ModelName = modelName;
        TotalBytes = totalBytes;
    }

    /// <summary>The model whose download was declined.</summary>
    public string ModelName { get; }

    /// <summary>The size that was declined, in bytes.</summary>
    public long TotalBytes { get; }
}

/// <summary>
/// Downloads model files into the model root and reports every download's size before its first
/// byte and its progress after (REQ-RAG-055 / BRD-92).
/// </summary>
/// <remarks>
/// <para><b>One service for every model.</b> The embedding model, the reranker and any later local
/// model fetch through <see cref="DownloadAsync"/>, so a host subscribes to
/// <see cref="DownloadSizeKnown"/> and <see cref="ProgressChanged"/> once and sees them all.</para>
/// <para><b>Resumable.</b> A file is written to <c>&lt;name&gt;.part</c> and renamed when complete;
/// an interrupted download resumes from the partial file with an HTTP range request when the server
/// supports it, and starts again when it does not.</para>
/// <para><b>Never writes to the console.</b> A package must not write to its host's console;
/// everything goes through the events.</para>
/// </remarks>
public class ModelDownloadService
{
    private static readonly Lazy<ModelDownloadService> Shared = new(() => new ModelDownloadService(null));

    /// <summary>A file on disk at this fraction of its expected size counts as downloaded.</summary>
    private const double CompleteFraction = 0.95;

    /// <summary>Minimum gap between two byte-progress events.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Gets the process-wide instance every TechieRag model download reports through.</summary>
    public static ModelDownloadService Instance => Shared.Value;

    /// <summary>
    /// Creates a service; the handler is for tests, <see langword="null"/> uses the default network stack.
    /// </summary>
    /// <param name="handler">The HTTP handler to send requests through.</param>
    internal ModelDownloadService(HttpMessageHandler? handler)
    {
        httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        httpClient.Timeout = TimeSpan.FromHours(2);
    }

    /// <summary>Current download progress.</summary>
    public ModelDownloadProgress Progress { get; } = new();

    /// <summary>Event raised when progress changes.</summary>
    public event EventHandler<ModelDownloadProgress>? ProgressChanged;

    /// <summary>
    /// Raised once per download with its total size, before the first byte is requested.
    /// </summary>
    public event EventHandler<ModelDownloadSizeEventArgs>? DownloadSizeKnown;

    /// <summary>
    /// Checks if the model is already downloaded.
    /// </summary>
    public bool IsModelReady => Progress.Status == ModelDownloadStatus.Completed;

    /// <summary>
    /// Checks if download is in progress.
    /// </summary>
    public bool IsDownloading => Progress.Status == ModelDownloadStatus.Downloading;

    /// <summary>
    /// Updates progress and notifies subscribers.
    /// </summary>
    /// <param name="update">The change to apply.</param>
    internal void UpdateProgress(Action<ModelDownloadProgress> update)
    {
        update(Progress);
        ProgressChanged?.Invoke(this, Progress);
    }

    /// <summary>
    /// Reports whether every file of a model is present in a folder at its expected size.
    /// </summary>
    /// <param name="destinationDirectory">The model folder.</param>
    /// <param name="files">The model's files.</param>
    /// <returns><see langword="true"/> when nothing would be downloaded.</returns>
    public static bool IsComplete(string destinationDirectory, IReadOnlyList<ModelDownloadFile> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(files);
        return files.All(f => IsFileComplete(destinationDirectory, f));
    }

    /// <summary>
    /// Computes how many bytes a download would transfer, without sending anything.
    /// </summary>
    /// <param name="destinationDirectory">The model folder.</param>
    /// <param name="files">The model's files.</param>
    /// <returns>The bytes still missing; 0 when the model is complete.</returns>
    public static long GetPendingBytes(string destinationDirectory, IReadOnlyList<ModelDownloadFile> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(files);
        return files
            .Where(f => !IsFileComplete(destinationDirectory, f))
            .Sum(f => Math.Max(0, f.ExpectedBytes - PartialLength(destinationDirectory, f)));
    }

    /// <summary>
    /// Downloads whatever files of a model are missing, reporting the size first and progress after.
    /// </summary>
    /// <param name="modelName">The model's name, carried in every event.</param>
    /// <param name="destinationDirectory">The folder the files land in; created when missing.</param>
    /// <param name="files">The model's files.</param>
    /// <param name="cancellationToken">Cancels the download; a partial file is kept for resuming.</param>
    /// <returns>A task that completes when every file is on disk.</returns>
    /// <exception cref="ModelDownloadDeclinedException">A <see cref="DownloadSizeKnown"/> handler declined.</exception>
    /// <exception cref="HttpRequestException">The server refused a file.</exception>
    public async Task DownloadAsync(
        string modelName,
        string destinationDirectory,
        IReadOnlyList<ModelDownloadFile> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(files);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pending = files.Where(f => !IsFileComplete(destinationDirectory, f)).ToList();
            if (pending.Count == 0)
            {
                UpdateProgress(p =>
                {
                    p.ModelName = modelName;
                    p.Status = ModelDownloadStatus.Completed;
                    p.TotalFiles = files.Count;
                    p.CompletedFiles = files.Count;
                });
                return;
            }

            var totalBytes = GetPendingBytes(destinationDirectory, files);
            UpdateProgress(p =>
            {
                p.ModelName = modelName;
                p.Status = ModelDownloadStatus.Checking;
                p.TotalFiles = files.Count;
                p.CompletedFiles = files.Count - pending.Count;
                p.TotalBytes = totalBytes;
                p.BytesDownloaded = 0;
                p.ErrorMessage = null;
            });

            var sizeKnown = new ModelDownloadSizeEventArgs(modelName, totalBytes, pending.Count, destinationDirectory);
            DownloadSizeKnown?.Invoke(this, sizeKnown);
            if (sizeKnown.Decline)
            {
                UpdateProgress(p =>
                {
                    p.Status = ModelDownloadStatus.NotStarted;
                    p.ErrorMessage = "Declined by the host.";
                });
                throw new ModelDownloadDeclinedException(modelName, totalBytes);
            }

            Directory.CreateDirectory(destinationDirectory);
            UpdateProgress(p => p.Status = ModelDownloadStatus.Downloading);

            long transferredBefore = 0;
            foreach (var file in pending)
            {
                transferredBefore += await DownloadFileAsync(destinationDirectory, file, transferredBefore, cancellationToken)
                    .ConfigureAwait(false);
                UpdateProgress(p => p.CompletedFiles++);
            }

            UpdateProgress(p => p.Status = ModelDownloadStatus.Completed);
        }
        catch (Exception exception) when (exception is not ModelDownloadDeclinedException)
        {
            UpdateProgress(p =>
            {
                p.Status = ModelDownloadStatus.Failed;
                p.ErrorMessage = exception.Message;
            });
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Formats a byte count the way sizes are shown to a user: "2.3 GB", "91 MB", "5 KB".
    /// </summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The size in decimal units.</returns>
    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_000_000_000 => (bytes / 1_000_000_000d).ToString("0.0", CultureInfo.InvariantCulture) + " GB",
        >= 1_000_000 => (bytes / 1_000_000d).ToString("0", CultureInfo.InvariantCulture) + " MB",
        >= 1_000 => (bytes / 1_000d).ToString("0", CultureInfo.InvariantCulture) + " KB",
        _ => bytes.ToString(CultureInfo.InvariantCulture) + " B"
    };

    /// <summary>Fetches one file, resuming a partial one, and reports byte progress.</summary>
    /// <param name="directory">The model folder.</param>
    /// <param name="file">The file.</param>
    /// <param name="transferredBefore">Bytes earlier files of this download transferred.</param>
    /// <param name="cancellationToken">Cancels the transfer.</param>
    /// <returns>The bytes this file transferred.</returns>
    private async Task<long> DownloadFileAsync(
        string directory,
        ModelDownloadFile file,
        long transferredBefore,
        CancellationToken cancellationToken)
    {
        var finalPath = Path.Combine(directory, file.FileName);
        var partPath = finalPath + ".part";
        var resumeFrom = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
        if (resumeFrom > 0)
        {
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
        }

        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (resumeFrom > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The partial file is already whole (or the server disagrees about its length): the
            // rename below finishes it.
            File.Move(partPath, finalPath, overwrite: true);
            return 0;
        }

        response.EnsureSuccessStatusCode();
        var resumed = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
        {
            resumeFrom = 0;
        }

        var fileTotal = response.Content.Headers.ContentLength is { } length ? resumeFrom + length : file.ExpectedBytes;
        UpdateProgress(p =>
        {
            p.CurrentFile = file.FileName;
            p.CurrentFileSize = FormatBytes(fileTotal);
            p.CurrentFileTotalBytes = fileTotal;
            p.CurrentFileBytesDownloaded = resumeFrom;
        });

        long transferred = 0;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = new FileStream(partPath, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            var lastReport = DateTime.UtcNow;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                transferred += read;

                if (DateTime.UtcNow - lastReport >= ProgressInterval)
                {
                    lastReport = DateTime.UtcNow;
                    ReportBytes(resumeFrom + transferred, transferredBefore + transferred);
                }
            }
        }

        ReportBytes(resumeFrom + transferred, transferredBefore + transferred);
        File.Move(partPath, finalPath, overwrite: true);
        return transferred;
    }

    /// <summary>Raises one byte-progress update.</summary>
    /// <param name="fileBytes">Bytes of the current file on disk.</param>
    /// <param name="overallBytes">Bytes this download has transferred.</param>
    private void ReportBytes(long fileBytes, long overallBytes) => UpdateProgress(p =>
    {
        p.CurrentFileBytesDownloaded = fileBytes;
        p.BytesDownloaded = overallBytes;
    });

    /// <summary>Whether one file is on disk at its expected size.</summary>
    /// <param name="directory">The model folder.</param>
    /// <param name="file">The file.</param>
    /// <returns><see langword="true"/> when it need not be fetched.</returns>
    private static bool IsFileComplete(string directory, ModelDownloadFile file)
    {
        var path = Path.Combine(directory, file.FileName);
        return File.Exists(path) && new FileInfo(path).Length >= file.ExpectedBytes * CompleteFraction;
    }

    /// <summary>The length of a partial file left by an interrupted download.</summary>
    /// <param name="directory">The model folder.</param>
    /// <param name="file">The file.</param>
    /// <returns>Its length, 0 when there is none.</returns>
    private static long PartialLength(string directory, ModelDownloadFile file)
    {
        var partPath = Path.Combine(directory, file.FileName + ".part");
        return File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
    }
}
