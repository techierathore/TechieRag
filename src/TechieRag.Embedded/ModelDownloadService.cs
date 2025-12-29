namespace TechieRag.Embedded;

/// <summary>
/// Tracks model download progress and state for UI display.
/// </summary>
public class ModelDownloadProgress
{
    /// <summary>Current download status.</summary>
    public ModelDownloadStatus Status { get; set; } = ModelDownloadStatus.NotStarted;

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

    /// <summary>Overall progress percentage (0-100).</summary>
    public int OverallProgressPercent => TotalFiles > 0
        ? (int)((CompletedFiles / (double)TotalFiles) * 100)
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
/// Singleton service to track and report BGE-M3 model download progress.
/// </summary>
public class ModelDownloadService
{
    private static ModelDownloadService? _instance;
    private static readonly object _lock = new();

    /// <summary>Gets the singleton instance.</summary>
    public static ModelDownloadService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ModelDownloadService();
                }
            }
            return _instance;
        }
    }

    private ModelDownloadService() { }

    /// <summary>Current download progress.</summary>
    public ModelDownloadProgress Progress { get; } = new();

    /// <summary>Event raised when progress changes.</summary>
    public event EventHandler<ModelDownloadProgress>? ProgressChanged;

    /// <summary>
    /// Updates progress and notifies subscribers.
    /// </summary>
    internal void UpdateProgress(Action<ModelDownloadProgress> update)
    {
        update(Progress);
        ProgressChanged?.Invoke(this, Progress);
    }

    /// <summary>
    /// Checks if the model is already downloaded.
    /// </summary>
    public bool IsModelReady => Progress.Status == ModelDownloadStatus.Completed;

    /// <summary>
    /// Checks if download is in progress.
    /// </summary>
    public bool IsDownloading => Progress.Status == ModelDownloadStatus.Downloading;
}
