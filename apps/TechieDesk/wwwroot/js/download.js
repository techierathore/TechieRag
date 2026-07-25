// Triggers a client-side file download from in-memory text (REQ-FN-010).
// Loaded as a JS module via IJSRuntime import, so it needs no <script> tag in App.razor.
export function saveText(fileName, mimeType, text) {
    const blob = new Blob([text], { type: mimeType || 'text/plain' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName || 'download.txt';
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
}
