// Paste-a-screenshot capture for the Support screen (REQ-UI-047 / BRD-141).
// Loaded as a JS module via IJSRuntime import, so it needs no <script> tag.
//
// Why JS at all: TrBlazeUI's <FileUpload> already covers the two affordances the browser gives
// Blazor for free — clicking to choose from disk, and dragging onto the drop zone. It cannot cover
// the third. A pasted screenshot arrives only as a ClipboardEvent, whose DataTransfer never reaches
// the file input's FileList, so there is no InputFileChangeEventArgs for Blazor to raise. Reading
// the clipboard item here and handing the bytes to .NET is the only route.

// Turns a Blob into the base64 payload .NET decodes, without the "data:...;base64," prefix.
function readAsBase64(blob) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onerror = () => reject(reader.error);
        reader.onload = () => {
            const result = reader.result || '';
            const comma = result.indexOf(',');
            resolve(comma >= 0 ? result.slice(comma + 1) : result);
        };
        reader.readAsDataURL(blob);
    });
}

// Names a pasted screenshot. The clipboard supplies no file name — Chrome reports "image.png" for
// every paste — so a timestamp keeps three screenshots on one comment distinguishable instead of
// collapsing into image, image (2), image (3).
function nameFor(blob) {
    const extension = blob.type === 'image/jpeg' ? 'jpg' : 'png';
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    return `pasted-screenshot-${stamp}.${extension}`;
}

// Attaches the paste handler to a container. Safe to call on every render: a container is only
// wired once. Returns false when the element is not in the DOM yet (a dialog that has not opened),
// which is the caller's signal to try again after the next render.
export function attachPasteTarget(elementId, dotNetRef, targetKey) {
    const element = document.getElementById(elementId);
    if (!element) {
        return false;
    }

    if (element.dataset.tdPasteBound === '1') {
        return true;
    }

    element.dataset.tdPasteBound = '1';
    element.addEventListener('paste', async (event) => {
        const items = event.clipboardData ? event.clipboardData.items : null;
        if (!items) {
            return;
        }

        for (const item of items) {
            if (item.kind !== 'file' || !item.type.startsWith('image/')) {
                continue;
            }

            const blob = item.getAsFile();
            if (!blob) {
                continue;
            }

            // Only now: pasting text into the comment box must keep its normal behaviour, so the
            // default is prevented for image payloads alone.
            event.preventDefault();
            try {
                const base64 = await readAsBase64(blob);
                await dotNetRef.invokeMethodAsync('OnPastedAttachment', targetKey, nameFor(blob), blob.type, base64);
            } catch (error) {
                console.error('TechieDesk: could not read the pasted image', error);
            }
        }
    });

    return true;
}
