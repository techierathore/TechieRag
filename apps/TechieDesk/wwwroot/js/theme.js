// Theme + accent application (REQ-UI-038 / BRD-90).
//
// Loaded as a JS module via IJSRuntime import, so it needs no <script> tag beyond the tiny
// first-paint shim in index.html.
//
// Why JS at all: the two things this does cannot be done from C# in a Blazor Hybrid head. Setting an
// attribute on <html> is outside the component's render tree — Blazor owns #app and below, not the
// document element — and `prefers-color-scheme` is only observable from the WebView.
//
// The DATABASE is the store of record for the choice (see AppearanceStore); localStorage here is a
// paint cache and nothing else. It is rewritten from the database on every apply, and clearing it
// changes nothing except the colour of the first frame after a cold start.

const CACHE_KEY = 'td-theme-cache';

// The mockups switch on html[data-theme="dark"]; TrBlazeUI's prebuilt CSS compiles its `dark:`
// variants to :is(.dark *). Both are set so both conventions resolve. See the comment block in
// styles/theme.css for the full reasoning.
function paint(dark) {
    const root = document.documentElement;
    root.setAttribute('data-theme', dark ? 'dark' : 'light');
    root.classList.toggle('dark', dark);
}

function prefersDark() {
    return typeof window.matchMedia === 'function'
        && window.matchMedia('(prefers-color-scheme: dark)').matches;
}

// Resolves 'system' against the OS. 'light'/'dark' are returned as given.
function resolve(mode) {
    if (mode === 'dark') return true;
    if (mode === 'light') return false;
    return prefersDark();
}

// The accent is written as INLINE custom properties on <html>, and BOTH palettes are written at
// once. It would be simpler to resolve the light/dark question here and set `--primary` directly —
// but then a window left on "Match system" would repaint its surfaces at dusk while keeping the
// accent variant it was given at load, so a dark window would carry the light indigo. Handing the
// stylesheet both values and letting the .dark / [data-theme] block choose keeps the accent and the
// palette resolving from the same rule, which is the only way they cannot drift apart.
function paintAccent(lightPrimary, lightForeground, darkPrimary, darkForeground) {
    const root = document.documentElement;
    const set = (name, value) => value
        ? root.style.setProperty(name, value)
        : root.style.removeProperty(name);

    set('--td-accent-light', lightPrimary);
    set('--td-accent-light-fg', lightForeground);
    set('--td-accent-dark', darkPrimary);
    set('--td-accent-dark-fg', darkForeground);
}

let systemQuery = null;
let systemListener = null;

// While the mode is 'system' the palette has to keep following the OS for as long as the window is
// open — macOS switches at dusk without restarting anything. The listener is torn down whenever the
// mode is not 'system' so an explicit Light choice is never overwritten by the OS.
function watchSystem(active) {
    if (typeof window.matchMedia !== 'function') return;

    if (!systemQuery) {
        systemQuery = window.matchMedia('(prefers-color-scheme: dark)');
    }

    if (active && !systemListener) {
        systemListener = (event) => paint(event.matches);
        systemQuery.addEventListener('change', systemListener);
    } else if (!active && systemListener) {
        systemQuery.removeEventListener('change', systemListener);
        systemListener = null;
    }
}

// Mirrors the applied choice for the next cold start's FIRST FRAME only. The accent travels with it
// because a branded install would otherwise flash the product indigo before the database is read —
// the same defect as the light flash, one paint later.
function cache(entry) {
    try {
        window.localStorage.setItem(CACHE_KEY, JSON.stringify(entry));
    } catch (e) {
        // Private/blocked storage costs nothing here — the next cold start just paints light first.
    }
}

/**
 * Applies a theme mode and accent to the document.
 * @param {string} mode 'light', 'dark' or 'system'.
 * @param {string} lightPrimary The accent's light-palette --primary, as an OKLCH string.
 * @param {string} lightForeground The accent's light-palette --primary-foreground.
 * @param {string} darkPrimary The accent's dark-palette --primary.
 * @param {string} darkForeground The accent's dark-palette --primary-foreground.
 */
export function apply(mode, lightPrimary, lightForeground, darkPrimary, darkForeground) {
    const normalized = mode === 'dark' || mode === 'light' ? mode : 'system';
    paint(resolve(normalized));
    paintAccent(lightPrimary, lightForeground, darkPrimary, darkForeground);
    watchSystem(normalized === 'system');
    cache({ mode: normalized, lightPrimary, lightForeground, darkPrimary, darkForeground });
}

/**
 * Reports whether the operating system currently prefers a dark palette, so the C# side can show
 * the resolved state on a "Match system" choice.
 * @returns {boolean} True when the OS prefers dark.
 */
export function systemPrefersDark() {
    return prefersDark();
}
