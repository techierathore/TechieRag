namespace TechieDesk.Services.Files;

/// <summary>
/// Writes text content to a location the user chooses through the operating system's native
/// save panel (REQ-FN-010, BRD-35).
/// </summary>
/// <remarks>
/// <para><b>Why this exists:</b> TechieDesk is a MAUI Blazor Hybrid desktop app. Inside a
/// BlazorWebView the classic browser download — a Blob URL on an anchor with a <c>download</c>
/// attribute — is handed to WKWebView, which routes it to a <c>WKDownloadDelegate</c> that MAUI's
/// BlazorWebView does not install. The click therefore has no handler and NO FILE IS WRITTEN. Any
/// "your file was downloaded" message on that path is a lie. Saving must go through the platform,
/// never through the WebView.</para>
/// <para><b>Implementations:</b> <c>MacCatalystFileSaveService</c> in the head (a
/// <c>UIDocumentPickerViewController</c> in export mode, which macOS renders as the standard save
/// panel), and <see cref="UnsupportedFileSaveService"/> as the honest fallback for a head that has
/// not shipped one yet — the Windows head has no Platforms/Windows sources at all (REQ-FN-035), so
/// it resolves the fallback and reports a failure rather than pretending to save.</para>
/// </remarks>
public interface IFileSaveService
{
    /// <summary>
    /// Gets a value indicating whether this host can present a native save panel.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Presents the native save panel and writes the supplied text to the chosen location.
    /// </summary>
    /// <param name="suggestedFileName">The file name to pre-fill in the panel, including extension.</param>
    /// <param name="contentType">The MIME type of the content, e.g. <c>text/markdown</c>.</param>
    /// <param name="content">The text to write.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="FileSaveResult"/> that is <see cref="FileSaveStatus.Saved"/> only when a file
    /// exists at the returned path, <see cref="FileSaveStatus.Cancelled"/> when the user dismissed
    /// the panel, and <see cref="FileSaveStatus.Failed"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="suggestedFileName"/> is blank.</exception>
    Task<FileSaveResult> SaveTextAsync(
        string suggestedFileName,
        string contentType,
        string content,
        CancellationToken cancellationToken = default);
}
