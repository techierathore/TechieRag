# TechieDesk — UI Design Spec (Mockups)

> **What this is.** The approved visual design for TechieDesk's NEW product screens (Phases 1–4), produced before the build. Each screen has a **rendered mockup** (`docs/mockups/{screen}.html`, styled to look like TrBlazeUI) and a **component map** that ties every region to a real **TrBlazeUI control**, so the build (`/trblazeui`) reproduces it 1:1 and the verifier's visual-truth gate diffs the live screen against it. **20 live screens are mocked** (+ 2 retired kept as record): 17 product screens + 3 console screens (Qdrant Admin, LLM Settings, Token Usage) added as *reskin previews* so the prototype navigation is complete — the console screens' current-behavior baseline remains the DevGuide screenshots (`docs/screenshots/TechieRag/`); their mockups show the Phase-1 product shell they migrate into. The P5 flow builder is deferred (mocked when Phase 5 is scheduled). This is a HUMAN document → rendered to HTML. **APPROVED by the owner on 2026-07-26** — these mockups are now the frozen visual contract the build reproduces and the verifier diffs against. Changing a screen after this point goes through `*mockups TechieDesk --update` and is re-approved, not edited ad hoc.

> **Updated 2026-07-26 — desktop-only (`*mockups TechieDesk --update`).** TechieDesk is now a **MAUI Blazor Hybrid desktop app** (BRD-128), so every mockup gained **native window chrome** — a macOS title bar and a real menu bar above the app shell — because the product no longer renders in a browser tab. **Added:** Data & storage (BRD-130/133) and a configurable **Docker daemon** panel on Qdrant Admin (BRD-134). **Retired:** Admin — Users (BRD-72) and Admin — Chat logs (BRD-74); both mockups are kept, banner-marked and greyed, and are unreachable from the nav. **Reframed:** Login and Register are now optional licence activation, never an access gate (BRD-129); the first-run wizard configures providers, not an administrator. The sidebar's "Admin" group is now "Operator", "Instance Settings" is "App Settings", and the footer no longer shows a role — there are no roles (BRD-23/24/25 retired).

> **Updated 2026-07-26 (second pass) — owner review of the mockups.** Fifteen issues were raised and all are fixed. Four were global: the **menu bar is now live** (seven real dropdowns that navigate and switch theme), the **sidebar follows the theme** instead of being permanently dark, **content fills the window** (the 1180px cap stranded the right half of a desktop window), and the **workspace switcher opens** a real workspace list. Screens gained the interactions that were only described before — thread context menus, a document **View** with preview / chunks / details and an honest "the source file has moved" path, working **tab switching** on Workspace settings and App settings, a real **New issue** dialog plus full issue detail with the comment thread, **event detail** behind every log row, and working **New collection** / **Pull image** on Qdrant Admin. Three raised gaps were **requirement** gaps, not mockup gaps, and were added to the BRD: **BRD-135** email/IMAP connector, **BRD-136** provider-conditional settings with save-time validation. Two screens the BRD already required but that had never been drawn are now mocked: **Agents** (BRD-83…86) and **Automations** (BRD-92/93). One tab was **removed** rather than drawn — Workspace settings › Members, which has no meaning on a single-user desktop.

> **Updated 2026-07-26 (third pass) — second owner review.** Seven more issues, all fixed and **verified by driving the mockups in a real browser** (18/18 interaction checks pass, zero page errors) rather than by inspection. One was a genuine bug that inspection had missed: a stray `</div>` had landed *inside* `workspace-settings.html`'s `<script>`, so **all JavaScript on that page was dead** — which is why its tabs still did not switch after the previous round claimed they did. Every mockup's inline script is now syntax-checked as part of the build.
>
> Five of the seven were **requirement** gaps, now in the BRD: **BRD-137** multi-line chat composer with per-turn mode/model/scope · **BRD-138** named user-defined agents · **BRD-139** background scheduler service · **BRD-140** natural-language authoring of schedules and flows · **BRD-141** support attachments and change-priority.

## Table of Contents

1. [How to use](#how-to-use)
2. [Design system (TrBlazeUI)](#design-system-trblazeui)
3. [Screens](#screens)
   - [Login](#screen-login-login)
   - [Register](#screen-register-register)
   - [Password recovery](#screen-password-recovery-forgot-password-reset-password)
   - [Profile](#screen-profile-profile)
   - [Workspace chat](#screen-workspace-chat-workspace-slug)
   - [Workspace settings](#screen-workspace-settings-workspace-slug-settings)
   - [Document library](#screen-document-library-workspace-slug-documents)
   - [First-run wizard](#screen-first-run-wizard-setup)
   - [Connectors](#screen-connectors-workspace-slug-connectors)
   - [Agents](#screen-agents-workspace-slug-agents)
   - [Automations](#screen-automations-automations)
   - [Pricing](#screen-pricing-pricing)
   - [Billing](#screen-billing-billing)
   - [Data & storage](#screen-data-storage-settings-data)
   - [Operator — Event log](#screen-operator-event-log-admin-events)
   - [Operator — App settings](#screen-operator-app-settings-admin-settings)
   - [Support](#screen-support-support)
   - [Console reskins — Qdrant Admin / LLM Settings / Token Usage](#screen-console-reskins-qdrant-admin-llm-settings-token-usage-qdrant-admin-llm-settings-token-usage)
   - [~~Admin — Users~~ (retired)](#screen-admin-users-admin-users-retired-2026-07-26)
   - [~~Admin — Chat logs~~ (retired)](#screen-admin-chat-logs-admin-chats-retired-2026-07-26)

## How to use

- Every screen links to its rendered mockup in `docs/mockups/`. Open the `.html` files in a browser to see the intended layout with realistic placeholder data.
- The **Component map** is the build contract: `region → TrBlazeUI control`. Only controls that exist in the TrBlazeUI catalog (`.trblazeui/TrBlazeUI-AI-Reference.md`) are used. Two library gaps were found and logged (see Design system notes): no Stepper/Wizard control, no Chat-thread control — both composed from existing primitives.
- To change a screen after approval: `*mockups TechieDesk --update`.

## Design system (TrBlazeUI)

- **Source:** TrBlazeUI component catalog (`.trblazeui/TrBlazeUI-AI-Reference.md`, shadcn-style, OKLCH tokens, Tailwind v4).
- **Host (amended 2026-07-26):** a **MAUI `BlazorWebView`**, not a browser (BRD-128). Every mockup therefore renders inside **native window chrome** — a 34px title bar with traffic lights and a 28px menu bar (`TechieDesk · File · Edit · View · Workspace · Window · Help`) — drawn *above* the app shell. That chrome is **MAUI's, not TrBlazeUI's**: it has no component map, it is the OS window the WebView sits in, and the build must not attempt to reproduce it in Razor. Everything below it is TrBlazeUI as before.
- **Window sizing (BRD-97, amended):** minimum **1024 × 720**; the mockups are drawn at ~1280 wide. The old 390px mobile gate is retired with the browser head — a desktop window cannot reach it.
- **Layout shell:** `SidebarProvider` + `Sidebar` (workspace switcher + grouped icon nav via `SidebarMenuButton` + `LucideIcon`: **Workspace / Account / Operator / Console** groups) + `SidebarInset` main column with a `Breadcrumb` topbar (license badge + avatar right). The licence/onboarding screens (`/login`, `/register`, `/forgot-password`, `/setup`) use a centered-card layout on a soft gradient backdrop, no sidebar — reached from inside the app, never as a gate in front of it.
- **Theme:** **indigo accent** (`--primary ≈ #4F46E5` light / `#818CF8` dark, applied via TrBlazeUI's OKLCH theme variables / tweakcn shadcn theme). **Permanently dark slate sidebar** (deep `#161B2E→#10131F` gradient with accent-tinted active item) against a soft `#F6F7F9` light canvas — white cards + subtle shadows. **Full dark mode** (`html[data-theme="dark"]` in the mockups ⇔ TrBlazeUI's `.dark` in the build): every mockup carries a moon-toggle (topbar on shell screens, floating on auth screens) with the choice persisted; deep `#0D1017` canvas, `#151A26` cards, re-tinted badges/alerts/inputs/tables. User chat bubbles = accent; agent execution trace + code snippets = dark slate mono panels in both themes.
- **Workspace switcher (2026-07-26):** the sidebar switcher opens a real list (workspaces with chunk counts, *New workspace…*, *Workspace settings…*). **New workspace** opens a creation dialog — name, description, start-from (empty / copy settings / copy settings + documents), and an optional system prompt — reachable both there and from **File › New Workspace ⌘N**.
- **Operable prototype (2026-07-26):** the mockups are not just linked, they *work* for the interactions being reviewed — menu-bar dropdowns, the workspace switcher, row overflow (&hellip;) menus, tab switching, and modal dialogs all respond, and `Esc` closes whatever is open. This is a shared ~90-line CSS/JS kit in every file. It exists so the design can be *reviewed* rather than imagined; it is **not** a build artefact and no Razor code should be ported from it.
- **Window chrome is not a component:** the title bar and menu bar are MAUI's. The menu contents (File / Edit / View / Workspace / Window / Help and their items) *are* a design decision and part of REQ-UI-041; their rendering is not.
- **Clickable prototype:** the mockups are interlinked — every sidebar item (including the three Console screens), the topbar avatar/license badge, and cross-screen CTAs (e.g. login → chat, pricing → billing) navigate to the sibling mockup file, so the whole app can be walked through in a browser in either theme.
- **Controls inventory used:** `Sidebar`/`SidebarProvider`/`SidebarInset`/`SidebarMenuButton`, `Breadcrumb`, `Card`, `Tabs`, `Button` (default/outline/ghost/destructive/sm), `Input`, `Textarea`, `Label`, `Field`, `Select`, `Combobox`, `MultiSelect`, `RadioGroup`, `Checkbox`, `Switch`, `Slider`, `NumericInput`, `InputGroup`, `FileUpload`, `DataTable`, `Badge`, `Avatar`, `Progress`, `Alert`, `AlertDialog`, `Dialog`, `Sheet`, `Toast`, `Empty`, `Skeleton`, `Spinner`, `DropdownMenu`, `DatePicker`, `Pagination`, `ColorPicker`, `ScrollArea`, `Separator`, `Kbd`, `LucideIcon`.
- **Library gaps (logged to `docs/TechieRag-TrBlazeUI-Feedback.md`):**
  - **TR-005 — no Stepper/Wizard component.** The first-run wizard composes `Progress` + a custom step-label row + `Card`. A dedicated Stepper would be cleaner.
  - **TR-006 — no Chat/message-thread component.** Workspace chat composes `ScrollArea` + styled message blocks + `Badge` citation chips + a custom composer (`Textarea` + `Button` + `Select`). A first-class Chat component would serve every RAG consumer.
- **Standing workaround:** every `DataTable` gets an inline `style="overflow-x:auto"` wrapper (TR-004 — the library's `.overflow-x-auto` class is purged from shipped CSS).

## Screens

### Screen: Login (`/login`)

**Mockup:** [docs/mockups/login.html](./mockups/login.html) · **Role(s):** the sole user · **BRD:** BRD-13, BRD-26, BRD-129, BRD-132 · **REQ:** REQ-UI-007, REQ-UI-013, REQ-FN-036 · **Reframed 2026-07-26**

**Layout (one line):** centered card on muted background; wordmark above; an "optional" `Alert` above the form; error-state specs shown in a second card.

> **Amended 2026-07-26 — this screen is no longer a gate.** Signing in activates a licence; it never stands between the user and their own documents (BRD-129). The mockup leads with an info `Alert` saying so, and the form offers **two** primary actions: *Sign in & activate licence* and *Continue without an account*. There is no forced redirect to `/login` at launch and no returnUrl gate to defend.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Wordmark | static brand block | TD mark + name | — |
| Optional-sign-in notice | `Alert` info | "signing in is optional" | always shown |
| Sign-in form | `Card` + `Field`/`Input` ×2 + `Button` | email, password → `POST /AuthSvc/login` | invalid creds `Alert` (danger) |
| Skip action | `Button` outline (full width) | → straight into the workspace, no account | primary path on first launch |
| Forgot link | link beside password `Label` | → `/forgot-password` | — |
| Error states | `Alert` danger/warning | INVALID_CREDENTIALS, ACCOUNT_LOCKED (423), ACCOUNT_DISABLED (403), NO_APP_ACCESS (BRD-26) | distinct copy per code |

**Notes / interactions:** password RSA-encrypted client-side before send (REQ-FN-001). On success the tokens go to the **OS credential store** (BRD-132 / REQ-FN-039), not a cookie — the footer hint says so. Loading: button shows `Spinner`.

**Empty / loading / error:** never blank — every AppManager error code maps to a friendly `Alert`. An AppManager outage does **not** block the app; it falls back to cached licence grace (BRD-51).

### Screen: Register (`/register`)

**Mockup:** [docs/mockups/register.html](./mockups/register.html) · **Role(s):** public · **BRD:** BRD-12 · **REQ:** REQ-UI-006

**Layout (one line):** centered card; 2-col name row; stacked fields; full-width CTA.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Form | `Card` + `Field`s: `Input` ×5 | first/last name, email, mobile (optional), password + confirm → `POST /AuthSvc/register` | per-field validation errors |
| Password hint | `Field` description | AppManager complexity rules | turns destructive on violation |
| CTA | `Button` (full width) | Create account → auto-login | `Spinner` while registering |

**Empty / loading / error:** duplicate-email and INVALID_PASSWORD map to field-level errors, not a generic banner.

### Screen: Password recovery (`/forgot-password`, `/reset-password`)

**Mockup:** [docs/mockups/password-recovery.html](./mockups/password-recovery.html) · **Role(s):** public · **BRD:** BRD-17 · **REQ:** REQ-UI-009

**Layout (one line):** two stacked cards (request + reset), each badged with its route.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Forgot form | `Card` + `Input` + `Button` | email → `/AuthSvc/forgot-password` | always-success `Alert` (anti-enumeration) |
| Reset form | `Card` + `Input` ×2 + `Button` | token (from email link) + new password | INVALID_RESET_TOKEN / APP_ID_MISMATCH `Alert` danger |

### Screen: Profile (`/profile`)

**Mockup:** [docs/mockups/profile.html](./mockups/profile.html) · **Role(s):** all authenticated · **BRD:** BRD-18, BRD-19, BRD-22, BRD-49 · **REQ:** REQ-UI-010, REQ-UI-011, REQ-UI-012

**Layout (one line):** app shell; header + Save; 2-col card grid (personal + password left; license + GDPR right).

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Personal info | `Card` + `Avatar` + `Input`s | name/mobile/avatar URL → `PUT /UserSvc/profile`; email read-only ("Managed by AppManager") | save `Toast` |
| Change password | `Card` + `Input` ×3 + `Button` outline | encrypted current+new → `/UserSvc/change-password` | INVALID_CURRENT_PASSWORD field error |
| License | `Card` + `Badge` success | plan, expiry, days remaining, devices | expired → warning `Badge` + upgrade link |
| GDPR | `Card` + `Button` outline + `Button` destructive + confirm `Input` | data-export & delete requests (email confirm) | EMAIL_MISMATCH error; request-submitted `Alert` success |

### Screen: Workspace chat (`/workspace/{slug}`)

**Mockup:** [docs/mockups/workspace-chat.html](./mockups/workspace-chat.html) · **Role(s):** assigned users+ · **BRD:** BRD-30, BRD-34, BRD-38, BRD-39, BRD-83, BRD-85, BRD-87, BRD-88 · **REQ:** REQ-UI-016, REQ-UI-017, REQ-UI-018, REQ-UI-034, REQ-UI-035, REQ-UI-036

> **Amended 2026-07-26 (2nd owner review) — the composer was the weakest thing in the set.** It was a **single-line** input with one dropdown option (`Auto-RAG`), which meant BRD-48's chat-vs-query modes were specified but unreachable at the point of use. It is now a proper composer (BRD-137 / REQ-UI-044): a **multi-line** input (Return sends, Shift+Return newline, grows to ~12 lines) under a control bar carrying the **answering mode** (Auto-RAG · Query · Chat · Direct-LLM · Agent), a **per-turn model override**, and a **retrieval scope** (whole workspace · pinned only · chosen documents), plus attach and saved-prompts. Thread rows also gained real **⋯ context menus** (open, rename, duplicate, export MD/JSON, copy link, pin, move, delete-with-confirm).

**Layout (one line):** app shell; 260px threads panel + chat column (messages, composer, hint line).

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Threads panel | `Card` + `Button` outline sm + list items + `DropdownMenu` (⋯) | threads newest-first; active highlighted; rename/delete | `Empty`: "No threads yet" |
| Messages | `ScrollArea` + styled blocks (TR-006 composition) | user right/muted; assistant left with markdown | streaming cursor ▍ + "Streaming…" hint; loading `Skeleton` |
| Citations | `Badge` chips + expandable panel | doc name + score; expanded: snippet + relevance | absent in Direct-LLM mode |
| Agent trace | bordered mono panel (from F-TOOLS pattern) | tool steps with timing | collapses when done |
| Composer | `Select` (mode) + `Textarea` + `Button` ghost (mic `LucideIcon`) + `Button` (send) | message; `@agent` trigger; Shift+Enter newline (`Kbd` in hint) | disabled while streaming; mic pulses while dictating |

**Notes / interactions:** mode select = Direct-LLM / Auto-RAG; retrieval always workspace-scoped; TTS read-aloud action on assistant messages (speaker icon, browser synthesis).

**Empty / loading / error:** new thread shows workspace welcome message; provider failure surfaces the library's retry/fallback status as a warning `Alert` in-thread.

### Screen: Workspace settings (`/workspace/{slug}/settings`)

**Mockup:** [docs/mockups/workspace-settings.html](./mockups/workspace-settings.html) · **Role(s):** Manager/Admin · **BRD:** BRD-28, BRD-29, BRD-47, BRD-48 · **REQ:** REQ-UI-015, REQ-FN-008 (+ REQ-RAG-014/015 behavior)

> **Amended 2026-07-26 (owner review).** The tab strip was decorative — all four panels were stacked, so only *General* appeared to exist. **Tabs now switch.** The **Members** tab is *removed*, not drawn: a desktop install has one user, so there is no membership to manage (BRD-23/24/25 retired). Remaining tabs: General / Retrieval / Danger.

**Layout (one line):** app shell; header + Save; Tabs (General / Retrieval / Members / Danger) — mockup shows all four stacked with tab badges.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| General | `Card` + `Input` ×2 + `Textarea` + `Select` + `RadioGroup` | name, slug, system prompt, LLM override, chat/query mode | slug conflict error |
| Retrieval | `Card` + `Slider` + `NumericInput` + `Switch` | threshold 0–1, top-K, rerank toggle | rerank disabled+hint until library reranker (REQ-RAG-025) ships |
| Members | `Card` + `DataTable` + `Combobox` + `Button` sm | member/role rows; add member | `Empty`: "Only you so far" |
| Danger | `Card` (danger border) + `Button` destructive + `AlertDialog` | delete workspace w/ consequences copy | dialog confirm/cancel |

### Screen: Document library (`/workspace/{slug}/documents`)

**Mockup:** [docs/mockups/document-library.html](./mockups/document-library.html) · **Role(s):** Manager/Admin manage, User view · **BRD:** BRD-40…46 · **REQ:** REQ-UI-019, REQ-UI-020, REQ-UI-021

> **Amended 2026-07-26 (owner review) — a document had no way to be *viewed*.** Every row now carries a **View** action opening a preview dialog with three tabs: **Preview** (paged render + *Open in default app* / *Reveal in Finder*), **Extracted chunks** (what the retriever actually stored — the thing citations point at), and **Details** (source path, the copy TechieDesk keeps, content hash, embedding model).
>
> A second path matters as much: **the user may have deleted or moved the original after ingesting it.** That View shows an amber `Alert` naming the last known location and states plainly that retrieval is unaffected — the extracted text and embeddings live in TechieDesk's own store, so the document still answers and still cites — offering *Locate file…*, *View extracted text*, and *Copy last known path*. Missing source ≠ missing document.

**Layout (one line):** app shell; header + Upload; dropzone card; upload queue; library DataTable; empty-state spec.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Dropzone | `FileUpload` (drag-drop + browse, multi) | accepted types incl. XLSX/PPTX | reject per-file with reason |
| Upload queue | `Card` + `Progress` rows | per-file embed progress | queued / embedding % / done |
| Library table | `DataTable` in `overflow-x:auto` wrapper (TR-004) | pin, name, type, size, chunks, uploaded, workspaces, status `Badge`, actions | statuses: embedded/embedding/failed(+reason) |
| Pin | toggle icon (`LucideIcon` pin) | always-in-context flag | pinned tooltip |
| Delete | `Button` ghost sm + `AlertDialog` | removes vectors everywhere (dedupe note) | confirmation with usage count |
| Empty state | `Empty` | "No documents yet…" | — |

### Screen: First-run wizard (`/setup`)

**Mockup:** [docs/mockups/setup-wizard.html](./mockups/setup-wizard.html) · **Role(s):** operator (first run only) · **BRD:** BRD-52, BRD-53, BRD-54 · **REQ:** REQ-UI-022, REQ-UI-023, REQ-FN-016

**Layout (one line):** centered 560px card; Progress + 4 step labels (TR-005 composition); step body; Back/Continue.

> **Amended 2026-07-26.** The wizard configures **providers, not administrators**. The old step 4 ("Admin account") is gone with BRD-23/24/25, and step 3 is now explicitly optional — it activates a licence and can be skipped without limiting local use (BRD-129).

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Step indicator | `Progress` + custom label row (TR-005 gap) | Defaults ✓ → AI Provider ● → Licence *(optional)* → Workspace | step states done/current/pending |
| Defaults notice | `Alert` success | BGE-M3 + SqliteVec applied, zero services | — |
| Ollama detect | `Alert` success + `Select` | detected endpoint + model list | not-found → info alert + manual URL `Input` |
| Provider choice | `RadioGroup` (bordered rows) | Ollama / LM Studio / OpenAI-compatible / skip | — |
| Nav | `Button` outline + `Button` | back / continue | continue disabled until valid |

**Notes:** step 3 (Licence) captures the AppManager base URL + API key/secret, or is skipped entirely — account-free operation is the norm, not a fallback; step 4 creates the default workspace. On first launch the app also resolves and creates its per-user data directory and runs DbUp in-process before this screen paints (BRD-130 / REQ-FN-037).

### Screen: Connectors (`/workspace/{slug}/connectors`)

**Mockup:** [docs/mockups/connectors.html](./mockups/connectors.html) · **Role(s):** the sole user · **BRD:** BRD-60…65, **BRD-135** · **REQ:** REQ-RAG-016…020, **REQ-RAG-049**, REQ-FN-020 · **Phase 2**

**Layout (one line):** app shell; source-card grid; config dialog per source; jobs table.

> **Amended 2026-07-26 (2nd owner review) — every source now configures.** Only Email had a working Configure button; the other five were dead links beside a single inline "Configure: Website crawler" panel. Each source now opens **its own dialog with its own fields** — URL (single page, strip-chrome, follow-redirect), Crawler (root, depth, max links, include/exclude, robots.txt), YouTube (video/playlist, transcript language, playlist limit, timestamp retention, plus an honest "no transcript → skipped with a reason, we do not transcribe audio"), GitHub/GitLab (host, repo, branch, token, include/exclude globs, re-sync), Confluence (base URL, space key, credentials, label filter, updated-since, child pages, attachments). The shared inline panel is gone; it implied a uniformity these sources do not have.

> **Amended 2026-07-26 (owner review) — Email added.** The connector set had no email source. For the contracts/approvals/decisions this product targets, the mailbox is frequently where the source of truth lives, so its absence was a **requirements** gap, now closed by **BRD-135**. Its config opens as a three-tab dialog (Account / What to ingest / Schedule) rather than the inline panel, because an IMAP source needs materially more setup than a URL.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Source cards | `Card` ×6 + `Button` outline sm | URL / crawler / YouTube / GitHub-GitLab / Confluence / **Email** | license-gated `CONNECTORS` → upgrade prompt |
| Config panel | `Card` + `Field`s (`Input`, `NumericInput`) + `Button` | per-source params (root URL, depth, max links, globs) | validation |
| **Email config** | `Dialog` + `Tabs` ×3 + `Select` + `Input` + `Switch` ×4 + `Alert` | provider/server/port/address/app-password; folders, from-date, sender + subject filters, attachments, quoted-reply stripping; sync interval + incremental | connection test; TLS-required `Alert`; privacy `Alert` |
| Jobs | `DataTable` + `Progress` + `Badge` | source, target, progress, status, items, started | running/completed/failed(+reason) |

**Notes:** IMAP credentials go to the OS credential store (REQ-FN-039), never a settings file, and a plaintext connection is refused rather than warned about. The scope filters and the two off-by-default switches (*include messages you sent*, *ingest spam*) are acceptance criteria — a mailbox is the highest-sensitivity source in the product.

### Screen: Agents (`/workspace/{slug}/agents`)

**Mockup:** [docs/mockups/agents.html](./mockups/agents.html) · **Role(s):** the sole user · **BRD:** BRD-83, BRD-84, BRD-85, BRD-86 · **REQ:** REQ-RAG-021, REQ-RAG-022, REQ-RAG-023, REQ-UI-034 · **Phase 4** · **NEW 2026-07-26**

**Layout (one line):** app shell; Tabs (Skills / MCP servers / Run history); skill toggle list, MCP table, run table + trace dialog.

> **Added 2026-07-26 (owner review)**, then **substantially reworked on the second review**: the first version showed Skills, MCP servers and Run history but **no agents** — nothing to create, name or configure. That was a requirements gap, not just a drawing gap: BRD-83/84 described *one anonymous agent* with workspace-level toggles. **BRD-138 / REQ-UI-045** adds a real agent registry, and the page now leads with an **Agents** tab.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| **Agent list** | `DataTable` + `Badge` + `DropdownMenu` | named agents: handle, model, skills, knowledge scope, last used (BRD-138) | built-in `@agent` marked undeletable; row menu = run / duplicate / export / delete |
| **Agent editor** | `Dialog` + `Tabs` ×4 (Identity / Skills / Knowledge / Guardrails) + `Input` + `Textarea` + `Select` + `Switch` | handle, description, plain-language instructions, model, skill subset, knowledge scope, guardrails | skills the workspace catalogue forbids render **greyed and marked *Blocked***, not hidden |
| Skill catalogue | `Card` + `Switch` ×6 + `Badge` | per-workspace skill enable (BRD-84): RAG search, web search, web scrape, SQL query, chart generation, file operations | `Badge` marks *Local* / *Leaves the machine* / *Needs review*; license-gated `AGENTS` |
| Behaviour | `Card` + `NumericInput` ×2 + `Switch` ×3 | max tool calls per turn, run time limit, show trace, confirm-before-egress, allow self-directed follow-ups | — |
| MCP servers | `DataTable` + `Badge` + `DropdownMenu` + `Dialog` | registered servers (BRD-86), transport, tool count, handshake state | `Connected` / `Handshake failed` + View error |
| Add MCP server | `Dialog` + `Select` + `Input` ×3 + `Textarea` + `Alert` | stdio command or http URL, env/headers | trust `Alert` — an MCP server runs with the user's permissions |
| Run history | `DataTable` + `Badge` | prompt, tools used, duration, outcome | `Completed` / `Stopped — time limit` |
| Execution trace | `Dialog` + step `Card`s | per tool call: arguments, timing, result (BRD-85) | includes the *0 chunks above threshold* step — the agent moving on rather than inventing |

**Notes:** every skill is a library `ITool`; the app contributes the registry, toggles, trace rendering, and the confirm-before-egress prompt only. Skills that reach outside the workspace default to **off**.

⚠ **The permission model is the part to get right.** Two levels: the workspace **skill catalogue** is the outer boundary (what is permitted here at all), and each agent selects from within it. An agent must not be able to enable what the catalogue forbids, and revoking a catalogue skill must take effect for every agent immediately — so the effective set is an intersection computed at run time, not a copy taken when the agent was saved. The editor greys forbidden skills rather than hiding them, so the reason stays legible.

### Screen: Automations (`/automations`)

**Mockup:** [docs/mockups/automations.html](./mockups/automations.html) · **Role(s):** the sole user · **BRD:** BRD-92, BRD-93 · **REQ:** REQ-UI-040, REQ-FN-028 · **Phase 5** · **NEW 2026-07-26**

**Layout (one line):** app shell; Tabs (Schedules / Flows / Run history); schedule table, flow cards, run table.

> **Added 2026-07-26 (owner review)**, then **corrected on the second review — the first version was wrong on the substance.** It asserted that a desktop app "cannot be a background daemon" and made the user live with it. That is false: a per-user OS helper (macOS **launchd** agent, Windows **per-user service**) can host the *same in-process scheduler* and run schedules with the window closed. **BRD-139 / REQ-FN-042** makes that a configurable feature with run conditions (mains power, named networks, wake-for-run) rather than a limitation to apologise for. *(For the record: nothing in this codebase uses Hangfire or any other job framework, and BRD-139 explicitly rules one out — a job server or job database would hand back exactly the operational weight the desktop pivot removed.)*
>
> **Authoring is now natural language (BRD-140 / REQ-UI-046).** The first version made the user compose a cron expression and hand-assemble flow steps, and showed raw cron (`*/30 * * * *`) in the grid. A product that ships an LLM should not do that. You now *describe* what you want; the configured **local** model drafts a reviewable, individually-editable result; you confirm. **Cron is never required** and appears only behind an *Advanced* disclosure — every list, grid and notification shows plain language ("Every weekday at 07:00"). Flow steps are editable individually and refinable conversationally ("skip anything under £10k").

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Background-service banner | `Alert` info + link | whether schedules run with the window closed, and a link to configure it | reflects the helper's installed state |
| **Background service** | `Dialog` + `Switch` ×5 + `Badge` | install/remove the helper; mechanism per platform named explicitly; mains-power-only, named-networks-only, wake-for-run, menu-bar indicator | states what is installed and where; uninstall must genuinely remove it |
| Schedules | `DataTable` + `Switch` + `Badge` + `DropdownMenu` | job, action, **plain-language schedule**, last run, next run (BRD-93) | `OK` / paused. ⚠ **no cron in this column** |
| New schedule | `Dialog` + `Textarea` + interpreted-result `Card` + `<details>` advanced + `Switch` ×2 | describe it in plain English → interpreted → reviewable steps, each with *Change* | confidence `Badge`; next-3-runs preview; cron only inside *Advanced* |
| Flows | `Card` grid + `Badge` | saved multi-step agent flows (BRD-92) | license/phase-gated `Badge` |
| New flow | `Dialog` + `Textarea` + drafted-step `Card`s | describe the outcome → drafted steps → review | *Start from a blank flow instead* escape hatch; *Refine with a follow-up instruction* |
| Step editor | `Dialog` + `Input` (plain language) + `Select` + `Input`s | edit a step by describing the change, **or** set action/workspace/top-K/threshold/query directly | both routes always available |
| Run history | `DataTable` + `Badge` | started, job, items, duration, outcome (BRD-65 shape) | `Succeeded` / `Partial` / `Skipped` → Details |

**Notes:** the helper is a **hosting choice, not a second implementation** — it loads the same `SchedulerService` and the same data directory. With it off, behaviour falls back to run-while-open plus catch-up at next launch.

⚠ **Design the confirm step for the model being wrong.** Natural-language interpretation will sometimes misread an instruction; the confirm panel is the only thing standing between that and a wrong automation running unattended on a schedule. It must therefore show the **full** understood result — trigger, every step, and the delivery action — never a one-line summary.

⚠ **Installing the helper is a user-visible OS action** (macOS prompts; Windows may require elevation depending on mechanism). The toggle names what it installs and where, and uninstall must actually remove it.

### Screen: Pricing (`/pricing`)

**Mockup:** [docs/mockups/pricing.html](./mockups/pricing.html) · **Role(s):** all authenticated · **BRD:** BRD-76, BRD-79 · **REQ:** REQ-UI-029, REQ-FN-026 · **Phase 3**

**Layout (one line):** app shell; currency select; 3 tier cards (current plan highlighted); promo card.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Currency | `Select` | `GET /LicenseSvc/types?aCurrency=` | — |
| Tier cards | `Card` ×3 (+ ring on current) + `Badge` | features per §5 matrix; CTA per tier | current-plan state |
| Promo | `InputGroup` + `Button` + `Alert` | `POST /PaymentSvc/promo-codes/validate` | valid success / each error code distinct |

### Screen: Billing (`/billing`)

**Mockup:** [docs/mockups/billing.html](./mockups/billing.html) · **Role(s):** all authenticated · **BRD:** BRD-77, BRD-78 · **REQ:** REQ-UI-030, REQ-UI-031 · **Phase 3**

**Layout (one line):** app shell; 2-col (subscription + license); full-width transactions + invoices tables.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Subscription | `Card` + `Badge` + `Button` outline/ghost | plan, amount, next billing; cancel period-end/immediate (`AlertDialog`) | ALREADY_CANCELLED handled |
| License | `Card` + mono key + `Button` ghost sm | key, status, expiry, devices n/m, deactivate device | — |
| Transactions | `DataTable` | number, type, amount, status `Badge`, date | `Pagination`; `Empty` |
| Invoices | `DataTable` + `Button` outline sm | download PDF (`GET /PaymentSvc/invoices/{id}/download`) | PDF_GENERATION_FAILED toast |

### Screen: Data & storage (`/settings/data`)

**Mockup:** [docs/mockups/data-storage.html](./mockups/data-storage.html) · **Role(s):** the sole user · **BRD:** BRD-130, BRD-133 · **REQ:** REQ-UI-041, REQ-FN-037 · **Phase 1** · **NEW 2026-07-26**

**Layout (one line):** app shell; header with Reveal-in-Finder; four stacked cards — Location, Disk usage, Migration, Backup & reset.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Path panel | `Card` + monospace `InputGroup` + `Button` ghost | the resolved per-user data directory; copy-path | `Badge` green when writable, red + `Alert` when not |
| Reveal / change | `Button` outline ×2 | OS reveal (Finder/Explorer) via MAUI; relocate-and-restart | change opens `AlertDialog` (moves data, restarts) |
| Disk usage | `Card` + `Progress` + `DataTable` | per-artefact size and last-written: model, vector store, app DB, RAG store, uploads | `Skeleton` while measuring; `Empty` on a fresh install |
| Per-row actions | `Button` ghost sm | compact a database, re-download the model, reveal uploads | `Spinner` inline; result `Toast` |
| Migration notice | `Card` + `Alert` green | one-time relocation of a legacy app-relative `data/` folder, files named | hidden when nothing was migrated |
| Backup & reset | `Card` + `Button` outline ×2 + `Button` destructive + `Alert` amber | back up, restore, delete all local data | destructive path requires `AlertDialog` confirmation |

**Notes / interactions:** the path is **shown, never typed** — it comes from `DataDirectory` (BRD-130). "Reveal in Finder" and the file pickers are **MAUI platform calls**, not web APIs; the Razor side only raises the intent. Deleting local data never touches the licence.

**Empty / loading / error:** measuring disk usage shows `Skeleton` rows; an unwritable directory is a red `Alert` naming the path and the OS error, never a silent fallback.

### Screen: Operator — Event log (`/admin/events`)

**Mockup:** [docs/mockups/admin-events.html](./mockups/admin-events.html) · **Role(s):** the sole user · **BRD:** BRD-73 · **REQ:** REQ-UI-026 · **Phase 3** · *"admin actions" reworded to "configuration changes" 2026-07-26*

> **Amended 2026-07-26 (owner review).** The grid only ever carried a summary and there was no way to see the record behind it. Every row now has **Details**, opening the full event: a **Summary** tab (outcome, duration, workspace, document, chunk counts, model, store, correlation id), a **Raw record** tab (the actual Serilog JSON — the grid renders a summary *of this*), and a **Related events** tab keyed on the correlation id, which is what turns a single line into a debuggable job. The *Actor* column now reads "you" — there are no other actors.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Filters | `DatePicker` ×2 + `Select` + `Input` + `Button` outline | date range, category, text | — |
| Events | `DataTable` + `Badge` (category) | time, category, actor, event, source | `Pagination` footer; `Empty` |

### Screen: Operator — App settings (`/admin/settings`)

**Mockup:** [docs/mockups/admin-settings.html](./mockups/admin-settings.html) · **Role(s):** the sole user · **BRD:** BRD-75, BRD-89, BRD-90 · **REQ:** REQ-UI-028, REQ-UI-037, REQ-UI-038 · **Phase 3–4** · **Reduced 2026-07-26**

**Layout (one line):** app shell; header + Save; Tabs (Defaults / Branding / Updates) — tabs switch.

> **Amended 2026-07-26.** "Instance settings" → "App settings" (BRD-75 reworded — there is no instance, only this install). The **Widget** and **API Keys** tabs and their cards are **removed** with F-WIDGET and F-API (BRD-70/71, BRD-67); the vector-store `Select` drops its `PgVector` option with the PostgreSQL path.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Defaults | `Card` + `Select` ×3 + `NumericInput` | default LLM/embeddings/vector store (SqliteVec · Qdrant), upload limit | — |
| Branding | `Card` + `FileUpload` (logo) + `Input`s + `ColorPicker` swatches + `RadioGroup` (theme) | white-label fields | license-gated `WHITE_LABEL` `Badge` + upgrade prompt |
| Updates | `Card` + `Switch` ×3 + `Button` ×2 + `Alert` | version, auto-check, background install, pre-release channel, release notes (BRD-131) | **added 2026-07-26** — desktop needs an update surface; the `Alert` states that updates never touch the data directory · **BUILT 2026-07-27 (REQ-FN-038b) at its own route `/settings/updates`**, not as a tab here, because this Phase 3–4 screen does not exist yet — the same precedent REQ-UI-041 set with `/settings/data`. It lifts into this tab unchanged when the screen is built. All specified elements are present; the **background-install `Switch` renders DISABLED with its reason**, because installing an unsigned download without a signature to verify would be an unauthenticated code-execution path — it becomes live with REQ-FN-038c. The auto-check `Switch` defaults **off** per REQ-NFR-008 |
| ~~Widget~~ | — | — | **removed 2026-07-26** (BRD-71) |
| ~~API keys~~ | — | — | **removed 2026-07-26** (BRD-67) |

### Screen: Support (`/support`)

**Mockup:** [docs/mockups/support.html](./mockups/support.html) · **Role(s):** all authenticated · **BRD:** BRD-80, BRD-81, BRD-82 · **REQ:** REQ-UI-032, REQ-UI-033, REQ-FN-027 · **Phase 3**

> **Amended 2026-07-26 (owner review).** Three fixes. (1) The page showed a **New issue** button *and* a permanently-open "New issue (Dialog spec)" card — the card is gone and the button opens a real dialog. (2) **Type** and **Priority** listed one value each; both now carry their full sets (Type: Bug / Feature request / Question / Billing &amp; licensing / Data-ingestion problem / Other · Priority: Low / Medium / High / Critical, each with a plain-language qualifier). (3) There was no way to open an issue — every row now has **View**, opening full issue detail: the comment thread, a composer with *Comment* and *Comment &amp; close*, and a details sidebar (status, type, priority, raised-by, app version, diagnostics).
>
> **Second review (BRD-141 / REQ-UI-047):** comments now take **attachments** — drag, paste a screenshot, or choose from disk (PNG/JPG/PDF/LOG, size-capped), with attachment chips that can be removed before sending. A bug report without a screenshot is materially harder to act on. **Change priority** now opens a real dialog (priority + optional reason recorded on the thread) instead of being a dead button.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| New issue | `Dialog` (mocked inline) + `Input` + `Select` ×2 + `Textarea` + `Button` | title/type/priority/description → `POST /IssueSvc` | created `Toast` w/ ISS number |
| Issues | `DataTable` + `Badge` (priority, status) | number, title, type, priority, status, updated | status filter; `Empty` |
| Issue detail | `Card` + comment thread + `Textarea` + `Button`s | comments (`POST /IssueSvc/{id}/comments`), close | ALREADY_CLOSED handled |

### Screen: Console reskins — Qdrant Admin / LLM Settings / Token Usage (`/qdrant-admin`, `/llm-settings`, `/token-usage`)

**Mockups:** [qdrant-admin.html](./mockups/qdrant-admin.html) · [llm-settings.html](./mockups/llm-settings.html) · [token-usage.html](./mockups/token-usage.html) · **Role(s):** the sole user · **BRD:** BRD-8, BRD-9, BRD-10 (pre-existing features), **BRD-134 (new)** · **REQ:** REQ-UI-003, REQ-UI-004, REQ-UI-005 (`Done (pre-existing)`), **REQ-UI-042 / REQ-FN-040 (new)**

These three screens already exist and are Verified — the mockups are **reskin previews** showing them inside the Phase-1 product shell (dark sidebar, Console nav group, new theme), added so the clickable prototype has no dead nav items. Functional baseline stays the DevGuide screenshots; when the Phase-1 shell lands, these screens adopt it with no behavioral change.

> **Amended 2026-07-26 — Qdrant Admin gains a configurable Docker daemon (BRD-134).** F-QADMIN was explicitly **retained** through the desktop pivot, and extended: the daemon TechieDesk administers is now a **setting**, not an assumption. A new first card on `/qdrant-admin` selects the endpoint (local socket · network host · remote TCP+TLS), tests the connection, shows daemon version/host/last-checked, and manages TLS material. The container lifecycle card below it acts on **whichever daemon is connected**.

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| **Docker daemon endpoint** (new) | `Card` + `Select` + `Input` (monospace) + `Button` outline (Test connection) | endpoint kind + address; `unix:///var/run/docker.sock` · `npipe://./pipe/docker_engine` · `tcp://host:2376` | `Badge` Connected/Disconnected + TLS-verified; failure → `Alert` naming the real error, never a blanket "unavailable" |
| **Daemon facts** (new) | 3-column `Card` grid | daemon version, host OS/arch, last checked | `Skeleton` while probing |
| **TLS & credentials** (new) | `Badge` ×2 + `Button` ghost (Manage…) | CA + client certificate presence | stored in the OS credential store (BRD-132), never a settings file |
| **Security warning** (new) | `Alert` amber | a daemon is root on its host; plain `tcp://` without TLS warns before saving | always shown |
| **New collection** | `Dialog` + `Input` + `Select` ×2 + `NumericInput` ×2 + `Switch` | name, vector size (pre-filled from the configured embedder), distance, shards, replicas, on-disk payload | **added 2026-07-26** — the button previously did nothing. Vector size is pre-filled because a mismatch is the usual cause of a failed first upsert |
| **Delete collection** | `Dialog` + `Alert` red + type-to-confirm `Input` | destructive drop on the remote daemon | type-the-name confirmation |
| **Pull image** | `Dialog` + `Progress` + log `pre` + `Alert` | layer progress; pulling does not restart the container | **added 2026-07-26** |
| Qdrant: container / collections / point browser | `Card` + `Badge` + `DataTable` (overflow wrapper) + dark `pre` payload panel | existing QdrantAdminService data, now against the configured daemon | — |
| LLM Settings: chat provider / embeddings / resilience | `Card` + `Field`/`Select`/`Input` + `Slider` + `Switch` + `Alert` | existing TechieRagConfigService bindings | — |
| **LLM Settings: provider-conditional fields** (new) | conditional `Field` groups keyed on the Source `Select` | only what the chosen provider uses — Azure: endpoint + deployment + API version, no model box; Ollama/LM Studio: base URL, optional key; OpenAI: key + optional org, no base URL; None: an `Alert` explaining Auto-RAG still needs a chat model | required fields marked; **Save refused while incomplete**, error named on the field (BRD-136 / REQ-UI-043) |
| Token Usage: stat tiles / budget / provider + ops tables | `Card` + icon tiles + `Progress` + `Switch` + `DataTable` | existing token tracker data | — |

### ~~Screen: Admin — Users~~ (`/admin/users`) — RETIRED 2026-07-26

**Mockup (archived):** [docs/mockups/admin-users.html](./mockups/admin-users.html) · **BRD:** ~~BRD-72~~ · **REQ:** ~~REQ-UI-025~~

**Retired** by the desktop-only amendment: one install serves one person, so there is no user list and no workspace assignment. The mockup is kept, banner-marked and greyed, as record of the multi-tenant design — it is **not a build target** and is unreachable from the sidebar.

### ~~Screen: Admin — Chat logs~~ (`/admin/chats`) — RETIRED 2026-07-26

**Mockup (archived):** [docs/mockups/admin-chats.html](./mockups/admin-chats.html) · **BRD:** ~~BRD-74~~ · **REQ:** ~~REQ-UI-027~~

**Retired** by the desktop-only amendment: a cross-workspace compliance view over one's own chats has no audience. Per-thread export survives on the thread itself (F-HIST). Kept as record, same treatment as above.

---
Last updated: 2026-07-18 · Amended 2026-07-26 (desktop-only, then owner mockup review) · **20 live screens mocked** (17 product + 3 console reskin previews) + 2 retired kept as record · Native window chrome + live menu bar on every mockup · Theme-following sidebar, operable tabs/dialogs/context menus · Agents and Automations now mocked · Second owner review applied and browser-verified (18/18 interaction checks)
