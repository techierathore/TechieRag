// REQ-UI-050 — interface scaling (Cmd/Ctrl +, -, 0).
//
// WHY THE APP LOOKED TOO SMALL. Two things compounded. The document has no explicit root
// font-size, so it inherited the WebView's 16px; and a Mac Catalyst app built against the iPad
// idiom renders its whole interface at roughly 77%. Almost every size in this UI is expressed in
// rem (TrBlazeUI and the mockups both do), so 0.875rem body text — 14px in the mockup — was
// landing near 11px on screen. The mockups were never wrong; they were being shrunk.
//
// Scaling the ROOT FONT SIZE rather than using CSS zoom or a WebView page-zoom is deliberate:
// rem-based sizes all follow it, layout reflows properly at each step, and it does not blur text
// the way a transform scale does. Sizes written in px — chiefly borders and a few icons — stay
// put, which is what you want: hairlines should not thicken as text grows.

const StorageKey = "td-ui-scale";
const DefaultScale = 1.15;
const MinScale = 0.8;
const MaxScale = 2.0;
const Step = 0.1;

function clamp(value) {
    if (!Number.isFinite(value)) {
        return DefaultScale;
    }
    return Math.min(MaxScale, Math.max(MinScale, Math.round(value * 100) / 100));
}

function read() {
    try {
        const stored = window.localStorage.getItem(StorageKey);
        return stored === null ? DefaultScale : clamp(parseFloat(stored));
    } catch {
        // Private/blocked storage must not cost the user a readable interface.
        return DefaultScale;
    }
}

function apply(scale) {
    const value = clamp(scale);
    document.documentElement.style.setProperty("--td-ui-scale", String(value));
    try {
        window.localStorage.setItem(StorageKey, String(value));
    } catch {
        // Applying it matters; remembering it is a convenience.
    }
    return value;
}

export function current() {
    return read();
}

export function set(scale) {
    return apply(scale);
}

export function zoomIn() {
    return apply(read() + Step);
}

export function zoomOut() {
    return apply(read() - Step);
}

export function reset() {
    return apply(DefaultScale);
}

// Applied before the first paint by the shim in index.html; this re-applies from the same source
// once the module loads, so the two can never disagree.
export function initialize() {
    apply(read());

    window.addEventListener("keydown", (event) => {
        // Cmd on macOS, Ctrl elsewhere. Both are accepted on both platforms because the app also
        // runs on Windows and a user who has learned one should not be told they used it wrong.
        if (!event.metaKey && !event.ctrlKey) {
            return;
        }

        // event.key for "+" is "=" on an unshifted US layout and "+" when shifted; other layouts
        // differ again, so event.code is checked too rather than trusting one of them.
        const key = event.key;
        const code = event.code;

        if (key === "+" || key === "=" || code === "Equal" || code === "NumpadAdd") {
            event.preventDefault();
            zoomIn();
        } else if (key === "-" || key === "_" || code === "Minus" || code === "NumpadSubtract") {
            event.preventDefault();
            zoomOut();
        } else if (key === "0" || code === "Digit0" || code === "Numpad0") {
            event.preventDefault();
            reset();
        }
    });
}
