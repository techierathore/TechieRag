# TrBlazeUI Feedback — surfaced during TechieRag

## Summary (filled by /flow-master on consolidation)
- 0 blockers · 0 major · 2 open minor (TR-003 SidebarInset min-width; TR-004 DataTable scroll wrapper — both worked around app-side; TR-001 resolved) · 3 nice-to-have (TR-002 css 404; TR-005 no Stepper/Wizard component; TR-006 no Chat/message-thread component)
- **Open for the TrBlazeUI team: TR-003, TR-004** (both minor, app has workarounds; TR-004 is the higher-value fix — DataTable should self-contain its own scroll wrapper, and the shipped Tailwind build should not purge layout utilities used only in consumer `.razor`). Component requests: TR-005, TR-006.
- Last consolidated: 2026-07-17 (TechieDesk mockups pass added TR-005/TR-006 component-gap requests; earlier same day: handoff after REQ-UI-014 TechieDesk rename — no new issues; the pre-existing TR-002 `{Assembly}.styles.css` 404 now presents as `TechieDesk.styles.css`, same behavior. Prior: 2026-07-02 — TR-004 corrected: the `overflow-x-auto` class is purged/inert, real fix is inline style, see TR-004)

## Issues

<!-- Append entries as gaps are found. IDs are append-only, never renumbered.
     TrBlazeUI → TR-NNN -->

### TR-001 — ✅ RESOLVED (app config) — TrBlazeUI overlays (Select/Toast/Dialog) silently fail when the app root isn't interactive
- **Severity:** minor → **RESOLVED 2026-07-01 (app-side, not a library defect)**
- **Repro:** all TrBlazeUI overlays (`Select` dropdown content, `ToastProvider`, `Dialog`) render into a `PortalHost` hosted in `MainLayout.razor`. The sample's `App.razor` rendered `<Routes />` + `<HeadOutlet />` with **no render mode** (static SSR) while each page set `@rendermode InteractiveServer` individually — so the layout + `PortalHost` sat in a static tree while pages were interactive.
- **Symptoms observed:** (1) `ToastService.Success/Error` produced no DOM node app-wide (Save/Reset silent); (2) the **Source `<Select>` dropdown on /llm-settings would not open** — console spammed `Portal select-portal-select-N-content render timeout` + `Floating element is not ready or not in DOM` from `TrBlazeUI.Primitives/js/primitives/positioning.js`, followed by `JSDisconnectedException` in `SelectContent.DisposeAsync`; (3) the REQ-UI-013 vector-detail modal did not open.
- **Root cause:** render-mode boundary — a `PortalHost` in a statically-rendered layout can't host portal content emitted by interactive pages, so positioning runs before the floating element is in the interactive DOM.
- **Fix applied (app-side):** switched the sample to **global interactivity** — `App.razor` now sets `@rendermode="InteractiveServer"` on both `<HeadOutlet>` and `<Routes>`, and the 9 now-conflicting per-page `@rendermode InteractiveServer` declarations were removed. Live-verified 2026-07-01: the Source dropdown opens and all 7 options select correctly, Save shows the "LLM configuration saved and applied" toast, and `portalErrors = 0`.
- **Suggested fix for the library team (doc-only):** document that `ToastProvider`/`PortalHost`/`Select`/`Dialog` require the hosting layout to be in an interactive render tree (global interactivity or an interactive boundary around the PortalHost) — a static layout + per-page interactivity silently breaks all overlays. Optionally emit a clearer diagnostic than "Floating element is not ready" when the PortalHost is non-interactive.

### TR-002 — Dangling `{Assembly}.styles.css` link 404s when a project has no scoped component CSS
- **Severity:** nice-to-have
- **Repro:** the TrBlazeUI sample scaffold's `App.razor` links `<link rel="stylesheet" href="TechieRagWeb.styles.css" />`, but the project has no `.razor.css` companion files, so the scoped-CSS bundle is never generated and `MapStaticAssets()` returns 404 for it on every page load.
- **Expected:** no 404 for an unused scoped bundle.
- **Actual:** `GET /TechieRagWeb.styles.css → 404` on every page (cosmetic — Tailwind theme.css/trblazeui.css/base.css provide all styling; layout is unaffected).
- **Encountered in:** all pages — surfaced 2026-07-01.
- **Workaround:** none needed (benign).
- **Suggested fix:** omit the scoped-bundle `<link>` from the scaffold when no `.razor.css` files exist, or make it conditional.

### TR-003 — `SidebarInset` doesn't contain over-wide page content: whole shell (main + header) stretches past the viewport on mobile
- **Severity:** minor
- **Repro:** at a 390×844 viewport, load a page whose content min-width exceeds the viewport (e.g. TechieRag sample `/tool-demo`: a `flex items-center justify-between` card-header row plus a `Textarea + Button` `flex gap-2` row give ~455px min-content; `/ingestion` reaches 560px). The `SidebarInset`-rendered `<main class="relative flex h-full flex-1 flex-col …">` grows to the content's min-width (455/560px) instead of staying at viewport width — `document.scrollWidth > window.innerWidth`, and the `<header>` (with the `SidebarTrigger`) stretches with it, so the whole app shell pans horizontally and right-side controls ("Add Custom Tool", "Run Agent Loop") sit off-canvas.
- **Expected:** the inset/main is capped at the viewport (`min-w-0` on the flex item + overflow containment), so over-wide content scrolls *inside* the content region while the header/shell stays fixed — matching shadcn sidebar behavior where the inset can't push the layout wider than the screen.
- **Actual:** `main` has `flex-1` but no `min-w-0`/`overflow-x` containment; measured `main` width 455px (tool-demo) and 560px (ingestion) at a 390px viewport — verifier §4b VISUAL-FAIL evidence: `test-results/screens/req-ui-007-freshload-mobile.png`, probe 2026-07-02.
- **Encountered in:** `/tool-demo` (REQ-UI-007), `/ingestion` (REQ-UI-004) — surfaced 2026-07-02 (verifier `*verify REQ-UI-007`).
- **Workaround (app-side):** make the offending page rows wrap at narrow widths (`flex-wrap` / `flex-col sm:flex-row` on ToolDemo.razor:20 and :84) so the page's min-content stays under 390px. The app owns its responsive layout — this entry is about the shell amplifying the problem to the header.
- **Suggested fix:** add `min-w-0` (and consider `overflow-x-hidden` on the header row) to the `SidebarInset` main element so page-content overflow can never widen the shell/header beyond the viewport.
- **Workaround applied (app-side, 2026-07-02):** `main { min-width: 0; }` in `wwwroot/styles/base.css` — verified: all pages measure `scrollWidth == viewport` at 390px.

### TR-004 — `DataTable` ships no contained scroll wrapper; its pagination's `sr-only` spans escape scroll containers and widen the page
- **Severity:** minor
- **Repro:** render a `DataTable` whose table (or pagination bar: "Rows per page … Page N of M … first/prev/next/last") is wider than a narrow viewport. Two defects compound:
  1. The component renders `div.w-full > div.rounded-md > table` with **no `overflow-auto` wrapper** (shadcn's Table renders `<div class="relative w-full overflow-auto">` around the table), so the table's min-content width propagates to the page.
  2. Even when the consumer adds their own `overflow-x-auto` wrapper, the pagination buttons' **`sr-only` spans are `position:absolute`**, so their containing block is the nearest *positioned* ancestor (the `SidebarInset` `<main>`, which is `relative`) — they escape every unpositioned scroll container and extend the document's scroll area (measured: spans at x=414/450/486 at a 390px viewport → `document.scrollWidth=487`).
- **Expected:** a `DataTable` never widens the page; wide content scrolls within the component.
- **Actual:** `/ingestion` panned to 487-560px at a 390px viewport until worked around. Verifier probe evidence 2026-07-02 (REQ-UI-004/REQ-UI-007).
- **Workaround applied (app-side, 2026-07-02):** wrap every `<DataTable>` in `<div class="relative overflow-x-auto">` — `relative` makes the wrapper the containing block for the sr-only spans so the scroll container clips them.
- **Suggested fix:** render the shadcn-style `relative w-full overflow-auto` wrapper inside `DataTable` itself (covering table + pagination), and/or use the standard sr-only recipe with `clip`/`clip-path` inside a positioned parent so hidden labels can't extend the page's scrollable area.
- **⚠ CORRECTION 2026-07-02 (flow-master `*build-phase`, REQ-UI-011):** the `overflow-x-auto` wrapper class above is **INERT in this app** — the `.overflow-x-auto` Tailwind utility is **purged from the shipped `_content/TrBlazeUI.Components/trblazeui.css`** (it appears only in the sample's `.razor` markup, which TrBlazeUI's own Tailwind build does not scan), so the wrapper computes `overflow-x: visible` and never clips. Proven on `/qdrant-admin` with a **running** Qdrant container: the 6-column containers `DataTable` (long image name `docker.io/qdrant/qdrant:v1.15.5`) reaches ~488px min-content, escapes its 374px wrapper, and drives `document.scrollWidth=496` at a 390px viewport (the Stop/connect buttons sit off-canvas). Earlier pages "passed" only because their tables were narrow enough to fit 374px without needing the (non-functional) scroll. **Additional trap:** an app-owned `base.css` rule reviving `.overflow-x-auto` did NOT reach the browser either — `MapStaticAssets` served a **0-byte `base.css` to any client sending `Accept-Encoding: br, gzip`** (stale/empty precompressed companion after editing the source `wwwroot/styles/base.css`), so CSS-file fixes are unreliable mid-session.
- **Real fix applied (app-side, 2026-07-02):** put the containment **inline on the wrapper divs** in `QdrantAdmin.razor` — `style="overflow-x:auto;max-width:100%"` (rendered into the Blazor HTML, immune to Tailwind purge and the static-asset compression pipeline). Verified live: `document.scrollWidth` 496→**390** at 390px, wrapper computed `overflow-x: auto`, the wide table now scrolls **inside** its local container; desktop 1280 no regression (`tests/verify/req-ui-011-mobile-fix.spec.ts`, both cases pass).
- **Two suggested fixes for the library team:** (1) ship the `relative w-full overflow-auto` wrapper **inside** `DataTable` (self-contained — consumers shouldn't need any wrapper class); (2) ensure TrBlazeUI's Tailwind build safelists/scans consumer markup, or documents that layout utilities like `overflow-x-auto` used only in consumer `.razor` are purged from the shipped bundle and must be supplied by the app.

### TR-005 — No Stepper/Wizard component (nice-to-have, found 2026-07-17 during TechieDesk mockups)

- **Context:** TechieDesk's first-run onboarding wizard (`/setup`, `docs/mockups/setup-wizard.html`) needs a multi-step flow with step states (done/current/pending), a progress indication, and back/continue navigation.
- **Gap:** the catalog has no Stepper/Wizard control. The mockup composes `Progress` + a custom step-label row + `Card` — replicable, but every consumer app will hand-roll the same thing.
- **Suggested addition:** a `Stepper` component (horizontal step indicators with done/current/pending states, optional content slots per step, Back/Next wiring) in the shadcn idiom.
- **Impact:** none on the build (composition works); design-system consistency only.

### TR-006 — No Chat/message-thread component (nice-to-have, found 2026-07-17 during TechieDesk mockups)

- **Context:** TechieDesk's core screen is a workspace chat (`docs/mockups/workspace-chat.html`): scrollable message list (user/assistant alignment), markdown body, citation chips, expandable citation panels, a tool-execution trace block, and a composer (textarea + mode select + mic/send).
- **Gap:** no chat-oriented components exist. The mockup composes `ScrollArea` + styled blocks + `Badge` + `Textarea` + `Button`. Workable, but chat UIs are now a mainstream Blazor need (every LLM app).
- **Suggested addition:** a `ChatThread`/`ChatMessage`/`ChatComposer` family (message alignment variants, streaming-cursor state, attachment/citation chip slot, composer with send-on-Enter + Shift+Enter newline).
- **Impact:** none on the build (composition works); would materially reduce boilerplate for TechieDesk and any TechieRag consumer.
