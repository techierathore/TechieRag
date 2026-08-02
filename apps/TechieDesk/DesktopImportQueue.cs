namespace TechieDesk;

/// <summary>
/// Hands documents chosen in a native OS picker to the Blazor ingestion UI (REQ-UI-041, BRD-133).
/// </summary>
/// <remarks>
/// <para>
/// The OS file and folder pickers are MAUI-head APIs, so the <c>File</c> menu opens them on the
/// native side; the ingestion screen that consumes the result is a Razor component. This is the
/// seam between the two. It is static because both ends live in the single-window desktop head and
/// there is exactly one user making one selection — a DI service would need a registration in
/// <c>MauiProgram.cs</c> for no behavioural gain.
/// </para>
/// <para>
/// Selections are DRAINED, never merely read: <see cref="TakeFiles"/> and <see cref="TakeFolder"/>
/// clear what they return, so navigating back to the ingestion screen later cannot silently
/// re-ingest a folder the user picked ten minutes ago.
/// </para>
/// </remarks>
public static class DesktopImportQueue
{
    /// <summary>Guards the pending selection; the picker completes off the UI thread.</summary>
    private static readonly Lock Gate = new();

    private static readonly List<string> PendingFiles = [];

    private static string? pendingFolder;

    /// <summary>Raised after a native picker adds a selection, so a live screen can drain it.</summary>
    /// <remarks>
    /// Navigation alone is not enough: when the ingestion screen is already open, re-navigating to
    /// its own route does not re-initialise the component, so without this the menu would appear to
    /// do nothing on the second use.
    /// </remarks>
    public static event Action? SelectionAdded;

    /// <summary>Gets a value indicating whether anything is waiting to be ingested.</summary>
    public static bool HasSelection
    {
        get
        {
            lock (Gate)
            {
                return PendingFiles.Count > 0 || pendingFolder is not null;
            }
        }
    }

    /// <summary>Queues files chosen in the native file picker.</summary>
    /// <param name="paths">Absolute paths of the chosen files. An empty sequence is ignored.</param>
    public static void QueueFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var added = false;
        lock (Gate)
        {
            foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                PendingFiles.Add(path);
                added = true;
            }
        }

        if (added)
        {
            SelectionAdded?.Invoke();
        }
    }

    /// <summary>Queues a folder chosen in the native folder picker.</summary>
    /// <param name="path">Absolute path of the chosen folder.</param>
    public static void QueueFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (Gate)
        {
            pendingFolder = path;
        }

        SelectionAdded?.Invoke();
    }

    /// <summary>Takes and clears the queued files.</summary>
    /// <returns>The queued absolute file paths; empty when nothing was queued.</returns>
    public static IReadOnlyList<string> TakeFiles()
    {
        lock (Gate)
        {
            if (PendingFiles.Count == 0)
            {
                return [];
            }

            var taken = PendingFiles.ToArray();
            PendingFiles.Clear();
            return taken;
        }
    }

    /// <summary>Takes and clears the queued folder.</summary>
    /// <returns>The queued absolute folder path, or null when none was queued.</returns>
    public static string? TakeFolder()
    {
        lock (Gate)
        {
            var taken = pendingFolder;
            pendingFolder = null;
            return taken;
        }
    }
}
