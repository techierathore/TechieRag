#if MACCATALYST || IOS
using UIKit;
using UniformTypeIdentifiers;
#endif

namespace TechieDesk;

/// <summary>
/// Opens the operating system's folder chooser (REQ-UI-041, BRD-133).
/// </summary>
/// <remarks>
/// <para>
/// MAUI ships <see cref="FilePicker"/> but has no folder picker in <c>Microsoft.Maui.Essentials</c>
/// — verified against 10.0.20, whose Mac Catalyst assembly exposes <c>FilePickerImplementation</c>
/// and nothing folder-shaped. BRD-133 asks for folder ingestion, so this wraps the platform control
/// directly: on Mac Catalyst a <c>UIDocumentPickerViewController</c> restricted to
/// <c>UTTypes.Folder</c>, which the OS renders as the standard Finder "choose folder" panel.
/// </para>
/// <para>
/// Returns null for a cancelled pick and throws nothing for an unsupported host — a picker that is
/// unavailable must surface as "no folder chosen" in the caller, never as an unhandled exception
/// from a menu click. The unsupported case is reported through <see cref="IsSupported"/> so callers
/// can say why instead of appearing to ignore the request.
/// </para>
/// </remarks>
public static class DesktopFolderPicker
{
    /// <summary>Gets a value indicating whether this host can show a folder chooser.</summary>
    public static bool IsSupported =>
#if MACCATALYST || IOS
        true;
#else
        false;
#endif

    /// <summary>
    /// Shows the OS folder chooser and waits for the user to choose or cancel.
    /// </summary>
    /// <returns>The absolute path of the chosen folder, or null when cancelled or unsupported.</returns>
    public static Task<string?> PickAsync()
    {
#if MACCATALYST || IOS
        var completion = new TaskCompletionSource<string?>();

        var controller = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController();
        if (controller is null)
        {
            return Task.FromResult<string?>(null);
        }

        // asCopy: false — the folder is opened in place. Copying a documents folder into the app
        // container to read it would duplicate every file the user asked to ingest.
        var picker = new UIDocumentPickerViewController([UTTypes.Folder], asCopy: false)
        {
            AllowsMultipleSelection = false
        };

        picker.DidPickDocumentAtUrls += (_, arguments) =>
        {
            var url = arguments.Urls.FirstOrDefault();
            if (url is null)
            {
                completion.TrySetResult(null);
                return;
            }

            // Sandboxed builds hand back a security-scoped URL; access must be claimed before the
            // path is read. It is deliberately never stopped: the ingestion that follows this call
            // runs asynchronously, and releasing the scope here would revoke access mid-read.
            url.StartAccessingSecurityScopedResource();
            completion.TrySetResult(url.Path);
        };

        picker.WasCancelled += (_, _) => completion.TrySetResult(null);

        controller.PresentViewController(picker, animated: true, completionHandler: null);
        return completion.Task;
#else
        return Task.FromResult<string?>(null);
#endif
    }
}
