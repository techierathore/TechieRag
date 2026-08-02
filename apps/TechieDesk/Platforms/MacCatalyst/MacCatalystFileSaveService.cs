using Foundation;
using TechieDesk.Services.Files;
using UIKit;

namespace TechieDesk;

/// <summary>
/// Saves text to a user-chosen location using the macOS save panel (REQ-FN-010, BRD-35).
/// </summary>
/// <remarks>
/// <para><b>Why not a WebView download:</b> the export used to build a Blob URL and click an anchor
/// carrying a <c>download</c> attribute. In a BlazorWebView that request reaches WKWebView, which
/// hands downloads to a <c>WKDownloadDelegate</c> that MAUI does not install — so the click was
/// silently dropped and no file was ever written, while the UI reported "Exported". The fix has to
/// be a platform save path, and this is it.</para>
/// <para><b>Why UIDocumentPickerViewController and not NSSavePanel:</b> Mac Catalyst is UIKit; AppKit
/// (and therefore <c>NSSavePanel</c>) is not reachable from a Catalyst app without a separate AppKit
/// plug-in bundle. <c>UIDocumentPickerViewController</c> in export mode
/// (<c>initForExportingURLs:asCopy:</c>) is the supported API, and macOS renders it AS the standard
/// Finder save panel. Same pattern the app already uses for folder selection in
/// <see cref="DesktopFolderPicker"/>.</para>
/// <para><b>Verification:</b> the result reports <see cref="FileSaveStatus.Saved"/> only after the
/// destination has been stat-ed, so the caller can never toast an export that did not land.</para>
/// </remarks>
public sealed class MacCatalystFileSaveService : IFileSaveService
{
    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public async Task<FileSaveResult> SaveTextAsync(
        string suggestedFileName,
        string contentType,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        // The picker exports an existing file, so the payload is staged in a per-call temp directory
        // under the suggested file name — that name is what the panel pre-fills for the user.
        var stagingDirectory = Path.Combine(Path.GetTempPath(), "techiedesk-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var stagedPath = Path.Combine(stagingDirectory, suggestedFileName);

        try
        {
            await File.WriteAllTextAsync(stagedPath, content, cancellationToken);

            var saved = await MainThread.InvokeOnMainThreadAsync(() => PresentPanelAsync(stagedPath));
            if (saved is null)
            {
                return FileSaveResult.Cancelled();
            }

            if (saved.Length == 0)
            {
                return FileSaveResult.Failed("The save panel closed but no file was written.");
            }

            return FileSaveResult.Saved(saved.Path, saved.Length);
        }
        catch (OperationCanceledException)
        {
            return FileSaveResult.Cancelled();
        }
        catch (Exception ex)
        {
            return FileSaveResult.Failed($"The file could not be saved: {ex.Message}");
        }
        finally
        {
            TryDeleteStaging(stagingDirectory);
        }
    }

    /// <summary>
    /// Presents the export panel for a staged file and waits for the chosen destination.
    /// </summary>
    /// <param name="stagedPath">The absolute path of the staged temp file to export.</param>
    /// <returns>The destination and its verified size, or null when the user cancelled.</returns>
    private static Task<SavedFile?> PresentPanelAsync(string stagedPath)
    {
        var completion = new TaskCompletionSource<SavedFile?>();

        var controller = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController();
        if (controller is null)
        {
            completion.TrySetResult(null);
            return completion.Task;
        }

        // asCopy: true — the staged file stays put and the OS copies it to the destination, so the
        // temp directory can be cleaned up unconditionally in the finally above. asCopy:false would
        // MOVE the staged file and turn that cleanup into a race.
        var picker = new UIDocumentPickerViewController([NSUrl.FromFilename(stagedPath)], asCopy: true);

        picker.DidPickDocumentAtUrls += (_, arguments) =>
        {
            var url = arguments.Urls.FirstOrDefault();
            var path = url?.Path;
            if (string.IsNullOrEmpty(path))
            {
                completion.TrySetResult(null);
                return;
            }

            // A destination outside the app container arrives as a security-scoped URL: access must
            // be claimed before the path can be stat-ed, and released once it has been. The stat
            // happens INSIDE the scope, which is why this method — not the caller — measures it.
            var scoped = url!.StartAccessingSecurityScopedResource();
            try
            {
                var length = File.Exists(path) ? new FileInfo(path).Length : 0L;
                completion.TrySetResult(new SavedFile(path, length));
            }
            catch (IOException)
            {
                completion.TrySetResult(new SavedFile(path, 0L));
            }
            finally
            {
                if (scoped)
                {
                    url.StopAccessingSecurityScopedResource();
                }
            }
        };

        picker.WasCancelled += (_, _) => completion.TrySetResult(null);

        controller.PresentViewController(picker, animated: true, completionHandler: null);
        return completion.Task;
    }

    private static void TryDeleteStaging(string stagingDirectory)
    {
        try
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is harmless; failing the export over it would not be.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A destination path together with the size observed at that path.</summary>
    private sealed record SavedFile(string Path, long Length);
}
