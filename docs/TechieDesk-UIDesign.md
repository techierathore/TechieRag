# TechieDesk — UI Design Spec (Mockups)

> **What this is.** The approved visual design for TechieDesk's NEW product screens (Phases 1–4), produced before the build. Each screen has a **rendered mockup** (`docs/mockups/{screen}.html`, styled to look like TrBlazeUI) and a **component map** that ties every region to a real **TrBlazeUI control**, so the build (`/trblazeui`) reproduces it 1:1 and the verifier's visual-truth gate diffs the live screen against it. **19 screens are mocked**: 16 new product screens + 3 console screens (Qdrant Admin, LLM Settings, Token Usage) added as *reskin previews* so the prototype navigation is complete — the console screens' current-behavior baseline remains the DevGuide screenshots (`docs/screenshots/TechieRag/`); their mockups show the Phase-1 product shell they migrate into. The P5 flow builder is deferred (mocked when Phase 5 is scheduled). This is a HUMAN document → rendered to HTML. The owner APPROVES it before build.

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
   - [Pricing](#screen-pricing-pricing)
   - [Billing](#screen-billing-billing)
   - [Admin — Users](#screen-admin-users-admin-users)
   - [Admin — Event log](#screen-admin-event-log-admin-events)
   - [Admin — Chat logs](#screen-admin-chat-logs-admin-chats)
   - [Admin — Instance settings](#screen-admin-instance-settings-admin-settings)
   - [Support](#screen-support-support)

## How to use

- Every screen links to its rendered mockup in `docs/mockups/`. Open the `.html` files in a browser to see the intended layout with realistic placeholder data.
- The **Component map** is the build contract: `region → TrBlazeUI control`. Only controls that exist in the TrBlazeUI catalog (`docs/TrBlazeUI-AI-Reference.md`) are used. Two library gaps were found and logged (see Design system notes): no Stepper/Wizard control, no Chat-thread control — both composed from existing primitives.
- To change a screen after approval: `*mockups TechieDesk --update`.

## Design system (TrBlazeUI)

- **Source:** TrBlazeUI component catalog (`docs/TrBlazeUI-AI-Reference.md`, shadcn-style, OKLCH tokens, Tailwind v4).
- **Layout shell:** `SidebarProvider` + `Sidebar` (workspace switcher + grouped icon nav via `SidebarMenuButton` + `LucideIcon`: Workspace / Account / Admin / Console groups) + `SidebarInset` main column with a `Breadcrumb` topbar (license badge + avatar right). Auth screens (`/login`, `/register`, `/forgot-password`, `/setup`) use a centered-card layout on a soft gradient backdrop, no sidebar.
- **Theme:** **indigo accent** (`--primary ≈ #4F46E5` light / `#818CF8` dark, applied via TrBlazeUI's OKLCH theme variables / tweakcn shadcn theme). **Permanently dark slate sidebar** (deep `#161B2E→#10131F` gradient with accent-tinted active item) against a soft `#F6F7F9` light canvas — white cards + subtle shadows. **Full dark mode** (`html[data-theme="dark"]` in the mockups ⇔ TrBlazeUI's `.dark` in the build): every mockup carries a moon-toggle (topbar on shell screens, floating on auth screens) with the choice persisted; deep `#0D1017` canvas, `#151A26` cards, re-tinted badges/alerts/inputs/tables. User chat bubbles = accent; agent execution trace + code snippets = dark slate mono panels in both themes.
- **Clickable prototype:** the mockups are interlinked — every sidebar item (including the three Console screens), the topbar avatar/license badge, and cross-screen CTAs (e.g. login → chat, pricing → billing) navigate to the sibling mockup file, so the whole app can be walked through in a browser in either theme.
- **Controls inventory used:** `Sidebar`/`SidebarProvider`/`SidebarInset`/`SidebarMenuButton`, `Breadcrumb`, `Card`, `Tabs`, `Button` (default/outline/ghost/destructive/sm), `Input`, `Textarea`, `Label`, `Field`, `Select`, `Combobox`, `MultiSelect`, `RadioGroup`, `Checkbox`, `Switch`, `Slider`, `NumericInput`, `InputGroup`, `FileUpload`, `DataTable`, `Badge`, `Avatar`, `Progress`, `Alert`, `AlertDialog`, `Dialog`, `Sheet`, `Toast`, `Empty`, `Skeleton`, `Spinner`, `DropdownMenu`, `DatePicker`, `Pagination`, `ColorPicker`, `ScrollArea`, `Separator`, `Kbd`, `LucideIcon`.
- **Library gaps (logged to `docs/TechieRag-TrBlazeUI-Feedback.md`):**
  - **TR-005 — no Stepper/Wizard component.** The first-run wizard composes `Progress` + a custom step-label row + `Card`. A dedicated Stepper would be cleaner.
  - **TR-006 — no Chat/message-thread component.** Workspace chat composes `ScrollArea` + styled message blocks + `Badge` citation chips + a custom composer (`Textarea` + `Button` + `Select`). A first-class Chat component would serve every RAG consumer.
- **Standing workaround:** every `DataTable` gets an inline `style="overflow-x:auto"` wrapper (TR-004 — the library's `.overflow-x-auto` class is purged from shipped CSS).

## Screens

### Screen: Login (`/login`)

**Mockup:** [docs/mockups/login.html](./mockups/login.html) · **Role(s):** public · **BRD:** BRD-13, BRD-26 · **REQ:** REQ-UI-007, REQ-UI-013

**Layout (one line):** centered card on muted background; wordmark above; error-state specs shown in a second card.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Wordmark | static brand block | TD mark + name | — |
| Sign-in form | `Card` + `Field`/`Input` ×2 + `Button` | email, password → `POST /AuthSvc/login` | invalid creds `Alert` (danger) |
| Forgot link | link beside password `Label` | → `/forgot-password` | — |
| Error states | `Alert` danger/warning | INVALID_CREDENTIALS, ACCOUNT_LOCKED (423), ACCOUNT_DISABLED (403) | distinct copy per code |

**Notes / interactions:** on success → deep-link return route (REQ-FN-003); password RSA-encrypted client-side before send (REQ-FN-001) — noted in the footer hint. Loading: button shows `Spinner`.

**Empty / loading / error:** never blank — every AppManager error code maps to a friendly `Alert`.

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

**Layout (one line):** centered 560px card; Progress + 5 step labels (TR-005 composition); step body; Back/Continue.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Step indicator | `Progress` + custom label row (TR-005 gap) | Defaults ✓ → AI Provider ● → AppManager → Admin → Workspace | step states done/current/pending |
| Defaults notice | `Alert` success | BGE-M3 + SqliteVec applied, zero services | — |
| Ollama detect | `Alert` success + `Select` | detected endpoint + model list | not-found → info alert + manual URL `Input` |
| Provider choice | `RadioGroup` (bordered rows) | Ollama / LM Studio / OpenAI-compatible / skip | — |
| Nav | `Button` outline + `Button` | back / continue | continue disabled until valid |

**Notes:** step 3 (AppManager) captures base URL + API key/secret or explicit **offline single-user mode**; step 4 bootstraps first Admin; step 5 creates default workspace.

### Screen: Connectors (`/workspace/{slug}/connectors`)

**Mockup:** [docs/mockups/connectors.html](./mockups/connectors.html) · **Role(s):** Manager/Admin · **BRD:** BRD-60…65 · **REQ:** REQ-RAG-016…020, REQ-FN-020 · **Phase 2**

**Layout (one line):** app shell; 3-col source-card grid; config card; jobs table.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Source cards | `Card` ×5 + `Button` outline sm | URL / crawler / YouTube / GitHub-GitLab / Confluence | license-gated `CONNECTORS` → upgrade prompt |
| Config panel | `Card` + `Field`s (`Input`, `NumericInput`) + `Button` | per-source params (root URL, depth, max links, globs) | validation |
| Jobs | `DataTable` + `Progress` + `Badge` | source, target, progress, status, items, started | running/completed/failed(+reason) |

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

### Screen: Admin — Users (`/admin/users`)

**Mockup:** [docs/mockups/admin-users.html](./mockups/admin-users.html) · **Role(s):** Admin · **BRD:** BRD-72, BRD-29 · **REQ:** REQ-UI-025 · **Phase 3**

**Layout (one line):** app shell; search + invite; users DataTable + workspace-assignment Sheet.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Toolbar | `Input` (search) + `Button` | filter; invite (register link) | — |
| Users table | `DataTable` + `Avatar` + `Badge` | name, email, role (read-only from AppManager), workspaces chips, status, last login | disabled users flagged |
| Assignment | `Sheet` + `Checkbox` list + `Button` | per-user workspace membership (App DB) | save `Toast` |

### Screen: Admin — Event log (`/admin/events`)

**Mockup:** [docs/mockups/admin-events.html](./mockups/admin-events.html) · **Role(s):** Admin · **BRD:** BRD-73 · **REQ:** REQ-UI-026 · **Phase 3**

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Filters | `DatePicker` ×2 + `Select` + `Input` + `Button` outline | date range, category, text | — |
| Events | `DataTable` + `Badge` (category) | time, category, actor, event, source | `Pagination` footer; `Empty` |

### Screen: Admin — Chat logs (`/admin/chats`)

**Mockup:** [docs/mockups/admin-chats.html](./mockups/admin-chats.html) · **Role(s):** Admin · **BRD:** BRD-74 · **REQ:** REQ-UI-027 · **Phase 3**

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Filters + export | `Select` ×2 + `Input` + `Button` outline | workspace, user, text; JSON export | export logged to event log |
| Compliance note | `Alert` info | admin-only visibility notice | — |
| Threads table | `DataTable` | workspace, user, thread, msg count, citation count, last activity | row → preview |
| Preview | `Card` + message blocks | 2-bubble excerpt + open-full action | — |

### Screen: Admin — Instance settings (`/admin/settings`)

**Mockup:** [docs/mockups/admin-settings.html](./mockups/admin-settings.html) · **Role(s):** Admin · **BRD:** BRD-75, BRD-89, BRD-90, BRD-71, BRD-67 · **REQ:** REQ-UI-028, REQ-UI-037, REQ-UI-038, REQ-UI-024, REQ-FN-022 · **Phase 3–4**

**Layout (one line):** app shell; header + Save; Tabs (Defaults / Branding / Widget / API Keys) — mockup shows all four stacked with tab badges.

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| Defaults | `Card` + `Select` ×3 + `NumericInput` | default LLM/embeddings/vector store, upload limit | — |
| Branding | `Card` + `FileUpload` (logo) + `Input`s + `ColorPicker` swatches + `RadioGroup` (theme) | white-label fields | license-gated `WHITE_LABEL` `Badge` + upgrade prompt |
| Widget | `Card` + `Select`s + `Input` + code `<pre>` + preview pane | workspace, position, accent, welcome; embed snippet + live-look preview bubble | gated `EMBED_WIDGET` |
| API keys | `Card` + `Button` sm + `Alert` success + `DataTable` + `Button` destructive sm | create (shown once), list hashed-prefix keys, revoke | revoked rows struck |

### Screen: Support (`/support`)

**Mockup:** [docs/mockups/support.html](./mockups/support.html) · **Role(s):** all authenticated · **BRD:** BRD-80, BRD-81, BRD-82 · **REQ:** REQ-UI-032, REQ-UI-033, REQ-FN-027 · **Phase 3**

**Component map:**

| Region | TrBlazeUI control | Shows / binds | States |
|--------|-------------------|---------------|--------|
| New issue | `Dialog` (mocked inline) + `Input` + `Select` ×2 + `Textarea` + `Button` | title/type/priority/description → `POST /IssueSvc` | created `Toast` w/ ISS number |
| Issues | `DataTable` + `Badge` (priority, status) | number, title, type, priority, status, updated | status filter; `Empty` |
| Issue detail | `Card` + comment thread + `Textarea` + `Button`s | comments (`POST /IssueSvc/{id}/comments`), close | ALREADY_CLOSED handled |

### Screen: Console reskins — Qdrant Admin / LLM Settings / Token Usage (`/qdrant-admin`, `/llm-settings`, `/token-usage`)

**Mockups:** [qdrant-admin.html](./mockups/qdrant-admin.html) · [llm-settings.html](./mockups/llm-settings.html) · [token-usage.html](./mockups/token-usage.html) · **Role(s):** Admin · **BRD:** BRD-8, BRD-9, BRD-10 (pre-existing features) · **REQ:** REQ-UI-003, REQ-UI-004, REQ-UI-005 (`Done (pre-existing)`)

These three screens already exist and are Verified — the mockups are **reskin previews** showing them inside the Phase-1 product shell (dark sidebar, Console nav group, new theme), added so the clickable prototype has no dead nav items. Functional baseline stays the DevGuide screenshots; when the Phase-1 shell lands, these screens adopt it with no behavioral change (regression-gated by the existing Playwright specs).

| Region | TrBlazeUI control | Shows / binds |
|--------|-------------------|---------------|
| Qdrant: container / collections / point browser | `Card` + `Badge` + `DataTable` (overflow wrapper) + dark `pre` payload panel | existing QdrantAdminService data |
| LLM Settings: chat provider / embeddings / resilience | `Card` + `Field`/`Select`/`Input` + `Slider` + `Switch` + `Alert` | existing TechieRagConfigService bindings |
| Token Usage: stat tiles / budget / provider + ops tables | `Card` + icon tiles + `Progress` + `Switch` + `DataTable` | existing token tracker data |

---
Last updated: 2026-07-18 · **19 screens mocked** (16 product + 3 console reskin previews) · Dark mode + dark sidebar shipped in all mockups · Flow builder (P5): not yet mocked
