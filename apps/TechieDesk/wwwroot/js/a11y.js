// REQ-NFR-005 (BRD-96) — TR-008 workarounds for accessibility defects that live INSIDE TrBlazeUI
// 1.0.7 components, where no app-side parameter can reach the markup. Two of them:
//   1. nameless decorative icons (below), and
//   2. Pagination's malformed list + unnamed rows-per-page combobox (see repairPagination).
//
// TrBlazeUI's LucideIcon emits role="img" unconditionally and exposes no way to name it, so every
// icon the LIBRARY renders inside its own components — the SidebarTrigger chevron, the Pagination
// arrows, the FileUpload dropzone glyph — arrives as an image with no accessible name. axe reports
// that as `svg-img-alt` (WCAG 1.1.1), and it was 25+ nodes on every shell route from the sidebar
// toggle alone. Icons the APP renders are marked aria-hidden at the call site; these cannot be,
// because no app-side parameter reaches inside a library component's markup.
//
// Marking them aria-hidden loses nothing: an element that already has NO accessible name conveys
// nothing to a screen reader, so the only change is that it stops being announced as an unlabelled
// image. Anything the library or the app HAS named — aria-label, aria-labelledby, or a <title>
// child — is deliberately left alone.
//
// Remove this file when TrBlazeUI ships an icon that is decorative by default (TR-008).

/** Icons that claim to be images but carry no name of their own. */
const nameless = 'svg[role="img"]:not([aria-hidden]):not([aria-label]):not([aria-labelledby])';

/** Coalesces bursts of Blazor DOM patches into one pass. */
let scheduled = false;

/**
 * Runs one full workaround pass over the current document.
 * @returns {void}
 */
function applyWorkarounds() {
    scheduled = false;
    repairPagination();
    for (const icon of document.querySelectorAll(nameless)) {
        // A <title> child IS an accessible name, so such an icon is meaningful and must stay.
        if (icon.querySelector(':scope > title')) {
            continue;
        }
        icon.setAttribute('aria-hidden', 'true');
        // Keeps the icon out of the tab order in engines that make SVG focusable by default.
        icon.setAttribute('focusable', 'false');
    }
}

/**
 * Repairs TrBlazeUI's Pagination markup in place (TR-008).
 *
 * The library renders `<nav aria-label="pagination">` containing a `<ul>` whose direct children are
 * `<div>`s, with the page `<li>`s nested inside those divs rather than in the list. That is two axe
 * failures — `list` and `listitem`, both WCAG 1.3.1 — and no app-side parameter reaches the markup:
 * Pagination is rendered by DataTable itself. The rows-per-page `<select>` inside it is likewise
 * unnamed (`button-name`, WCAG 4.1.2), even though the words "Rows per page" sit right beside it.
 *
 * Neither element is a list in any meaningful sense once the nesting is wrong, so the broken list
 * semantics are dropped rather than faked, and the combobox borrows the visible text that already
 * labels it. Both are what the markup would say if it were written correctly.
 * @returns {void}
 */
function repairPagination() {
    for (const nav of document.querySelectorAll('nav[aria-label="pagination"]')) {
        for (const combo of nav.querySelectorAll(
            'button[role="combobox"]:not([aria-label]):not([aria-labelledby])')) {
            const caption = combo.closest('div')?.parentElement?.querySelector('p, label');
            const text = caption?.textContent?.trim();
            if (text) {
                combo.setAttribute('aria-label', text);
            }
        }

        for (const list of nav.querySelectorAll('ul:not([role])')) {
            if (list.querySelector(':scope > :not(li)')) {
                list.setAttribute('role', 'presentation');
            }
        }

        for (const item of nav.querySelectorAll('li:not([role])')) {
            const parent = item.parentElement;
            if (parent && parent.tagName !== 'UL' && parent.tagName !== 'OL') {
                item.setAttribute('role', 'presentation');
            }
        }
    }
}

/**
 * Queues a pass on the next frame.
 * @returns {void}
 */
function schedulePass() {
    if (scheduled) {
        return;
    }

    scheduled = true;
    requestAnimationFrame(applyWorkarounds);
}

/**
 * Starts watching the document for newly rendered library markup.
 * @returns {void}
 */
export function initialize() {
    applyWorkarounds();
    // Blazor patches the DOM on every render, and route changes replace the whole page body, so a
    // one-shot pass would only ever fix the first screen.
    new MutationObserver(schedulePass).observe(document.documentElement, {
        childList: true,
        subtree: true,
    });
}
