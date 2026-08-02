// Chat composer keyboard + auto-grow behaviour (BRD-137 / REQ-UI-044).
// Loaded as a JS module via IJSRuntime import, so it needs no <script> tag in App.razor.
//
// Why JS: Blazor's `preventDefault` on @onkeydown is a compile-time flag, so it cannot be applied
// to Return-without-Shift only. Handling the key here lets Return send WITHOUT the browser also
// inserting the newline, while Shift+Return keeps the browser's native newline insertion.

// Reads the CSS max-height so the growth cap stays owned by the stylesheet, falling back to the
// requested row count when max-height is not a pixel value.
function growthLimit(textarea, maxRows) {
    const style = window.getComputedStyle(textarea);
    const cssMax = parseFloat(style.maxHeight);
    if (!Number.isNaN(cssMax) && cssMax > 0) {
        return cssMax;
    }

    const lineHeight = parseFloat(style.lineHeight) || 20;
    const padding = (parseFloat(style.paddingTop) || 0) + (parseFloat(style.paddingBottom) || 0);
    return (lineHeight * (maxRows || 12)) + padding;
}

// TR-025 workaround. TrBlazeUI's Textarea always renders the `value` attribute and hardcodes its
// binding to `oninput`, so on Blazor Server every keystroke round-trips and the server's echo is
// patched back onto the element. Under fast input (paste-then-type, or a fast typist) an echo can
// land AFTER later keystrokes and truncate them — driving the composer with Playwright reproduced
// "first line" arriving as "ft line". A stale echo is always a strict PREFIX of what the element
// already holds, so while the user is typing we drop exactly those writes and let the next echo,
// which carries the full text, apply. Our own programmatic writes bypass the guard.
function guardAgainstStaleEchoes(textarea) {
    const descriptor = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value');
    if (!descriptor || !descriptor.get || !descriptor.set) {
        return;
    }

    textarea.tdSetValue = (next) => descriptor.set.call(textarea, next);

    Object.defineProperty(textarea, 'value', {
        configurable: true,
        get() {
            return descriptor.get.call(this);
        },
        set(next) {
            const live = descriptor.get.call(this);

            // The turn was just sent and the box was cleared. Any non-empty echo still in flight
            // belongs to the question that was already dispatched and must not repopulate the box.
            if (this.tdIgnoreEchoUntilInput && typeof next === 'string' && next.length > 0) {
                return;
            }

            const stale = document.activeElement === this
                && typeof next === 'string'
                && next.length < live.length
                && live.startsWith(next);
            if (stale) {
                return;
            }
            descriptor.set.call(this, next);
        }
    });
}

function setValue(textarea, next) {
    if (typeof textarea.tdSetValue === 'function') {
        textarea.tdSetValue(next);
    } else {
        textarea.value = next;
    }
}

function grow(textarea, maxRows) {
    const limit = growthLimit(textarea, maxRows);
    textarea.style.height = 'auto';
    const wanted = textarea.scrollHeight;
    textarea.style.height = Math.min(wanted, limit) + 'px';
    textarea.style.overflowY = wanted > limit ? 'auto' : 'hidden';
}

// Attaches the send/newline keyboard contract and the auto-grow behaviour to the composer.
// Safe to call on every render: a textarea is only wired once.
export function attach(elementId, dotNetRef, maxRows) {
    const textarea = document.getElementById(elementId);
    if (!textarea) {
        return false;
    }

    if (textarea.dataset.tdComposerBound !== '1') {
        textarea.dataset.tdComposerBound = '1';
        guardAgainstStaleEchoes(textarea);

        textarea.addEventListener('input', () => {
            textarea.tdIgnoreEchoUntilInput = false;
            grow(textarea, maxRows);
        });

        textarea.addEventListener('keydown', (event) => {
            if (event.key !== 'Enter' || event.shiftKey || event.altKey || event.ctrlKey || event.metaKey) {
                return;
            }

            // IME composition: Return commits the candidate, it does not send the message.
            if (event.isComposing || event.keyCode === 229) {
                return;
            }

            event.preventDefault();
            const text = textarea.value;
            if (!text || text.trim().length === 0) {
                return;
            }

            dotNetRef.invokeMethodAsync('SubmitFromComposer', text);
        });
    }

    grow(textarea, maxRows);
    return true;
}

// Clears the composer after a turn is sent. Blazor cannot do this on its own here: its view of the
// bound value was already emptied when the turn was dispatched, so the DOM node would keep the text.
export function reset(elementId, maxRows) {
    const textarea = document.getElementById(elementId);
    if (!textarea) {
        return;
    }

    textarea.tdIgnoreEchoUntilInput = true;
    setValue(textarea, '');
    grow(textarea, maxRows);
}

// Inserts text at the caret (saved prompts) and returns the resulting value so the caller can
// keep its bound field in step.
export function insertAtCaret(elementId, text, maxRows) {
    const textarea = document.getElementById(elementId);
    if (!textarea) {
        return text;
    }

    textarea.tdIgnoreEchoUntilInput = false;
    const start = textarea.selectionStart ?? textarea.value.length;
    const end = textarea.selectionEnd ?? textarea.value.length;
    setValue(textarea, textarea.value.slice(0, start) + text + textarea.value.slice(end));
    const caret = start + text.length;
    textarea.setSelectionRange(caret, caret);
    textarea.focus();
    grow(textarea, maxRows);
    return textarea.value;
}

// Replaces the composer's text outright and returns the value that stuck.
//
// REQ-UI-035: dictation needs this, and insertAtCaret cannot serve. A dictated transcript is
// REVISED as the user keeps talking — the recognizer rewrites earlier words once later context
// lands — so the composer is rewritten on every update rather than appended to. A revision that
// shortens the text looks exactly like the stale server echo the guard above exists to swallow, so
// the write goes through tdSetValue to bypass it: this write is ours, not an echo. The caret is
// parked at the end so the user can carry on typing where the dictation left off.
export function replaceValue(elementId, text, maxRows) {
    const textarea = document.getElementById(elementId);
    if (!textarea) {
        return text;
    }

    textarea.tdIgnoreEchoUntilInput = false;
    setValue(textarea, text ?? '');
    const caret = textarea.value.length;
    textarea.setSelectionRange(caret, caret);
    grow(textarea, maxRows);
    return textarea.value;
}

// Reads the live composer text. The Send button uses this so a click never races the bound value.
export function readValue(elementId) {
    const textarea = document.getElementById(elementId);
    return textarea ? textarea.value : '';
}

// Parks the transcript at its newest message (REQ-UI-044).
//
// Opening a thread on the OLDEST message is wrong on its own — a chat is read from the bottom — but
// it also decides where the scrolled-out history is LAID OUT. A list parked at the top puts its
// overflow below the box, i.e. straight through the composer beneath it; parked at the bottom, the
// overflow is above the box, where the chat column has nothing but its own header row. That is what
// turned an ordinary scrollback into the 980 px read-aloud/Send intersection the 2026-07-31 sweep
// measured at 1024x720.
//
// scrollHeight is read AFTER layout (this runs from OnAfterRenderAsync), so the new message is
// already measured. Assigning past the maximum is clamped by the browser, so no maths is needed.
export function scrollToEnd(elementId) {
    const list = document.getElementById(elementId);
    if (!list) {
        return;
    }

    list.scrollTop = list.scrollHeight;
}
