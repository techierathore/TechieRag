# TechieDesk DevGuide — index

*Generated 2026-07-28 · reflects code as built.*

> ✅ **Runtime-verified 2026-07-29** — re-swept on the live **Mac Catalyst** head over Appium
> (`mac2` driver, session bound by `appPath` to
> `apps/TechieDesk/bin/Release/net10.0-maccatalyst/TechieDesk.app` — the universal bundle, never by
> bundleId alone). **28 of the 30 screens were driven** at **1600×1240** and **1024×720**, plus every
> secondary tab and several dialogs. The two that were not: `/reset-password` (emailed-token only) and
> `/setup` (reachable only by deleting the install's single workspace — destructive, not done). Both
> say so in their area file. Screenshots: `test-results/ui-verify/`.

### Runtime findings worth knowing (2026-07-29)

- 🔴 **One render defect.** `/workspace/{Slug}/documents` → the library table's **`Size` column shows
  `—` on every row**. `SizeFromMetadata` (`DocumentLibrary.razor:735`) probes five metadata keys and no
  ingestion path writes any of them. REQ-UI-021 names *size* as a required column.
- ⚠ **Event-log coverage gap.** `IEventLogRepository` has exactly one producer — the `/admin/settings`
  save. 12 ingested documents and 5 connector runs produced zero events, although the screen promises
  "auth, ingestion and configuration changes".
- ⚠ **Library icon fallback (TR-032 family).** `<LucideIcon Name="alert-circle">` renders a colour
  ⚠️ **emoji** and `Name="shield-alert"` a monochrome ⚠ glyph instead of their SVGs; `info` resolves
  correctly, so it is name-specific. The emoji even leaks into the accessibility tree as a text node
  (seen on `/register`). Cosmetic, library-side. **Zero occurrences of the literal
  `Icon not found: {name}` string** were found across all 28 screens.
- ⚠ **Select/DropdownMenu accessibility is inconsistent.** The Selects on `/admin/settings` and
  `/rag-config` surface as `AXPopUpButton` carrying their value; the `LlmSettings` Source Select and the
  `LanguagePicker` surface as plain `AXStaticText` with the value absent, and the topbar Theme
  dropdown's items surface as `AXStaticText` rather than `AXMenuItem`.
- ✅ **No overlapping, clipped or off-viewport controls anywhere** at either width. The one geometry hit
  is a 3×32 px adjacency between two tab hit boxes on `/workspace/{Slug}/settings` — invisible.
- ✅ **REQ-UI-041's 1024×720 window floor is enforced** — a request for 700×500 clamped to exactly
  1024×720. The native **Go** menu is real (`td.menu.1`) and was used to drive `/`, `/chat` and
  `/ingestion`.

## 1. How to use this guide

Each area file documents the screens of one navigation area: the route, the Razor file, the controls
the page actually mounts (with line numbers), the services it calls, its conditional render guards,
and a screenshot of the screen as it really renders.

Read it to answer "where does this value come from?" or "why is this control blank?" without
reverse-engineering the repo. Every symbol cited was read in the working tree; anything that could
not be confirmed is marked rather than invented.

**What this guide is honest about.** A control listed here is *mounted by the Razor page*. Where the
OBSERVE pass reached the screen, the render verdict is what the running app did. Where it did not,
the screen says `not observed` — an unreached screen is never presented as working.

| Area | Screens | File |
|---|---|---|
| Workspace | 7 | [Workspace](./TechieDesk-DevGuide-Workspace.md) |
| Account | 8 | [Account](./TechieDesk-DevGuide-Account.md) |
| Operator | 5 | [Operator](./TechieDesk-DevGuide-Operator.md) |
| Console | 8 | [Console](./TechieDesk-DevGuide-Console.md) |
| Shell & landing | 1 | [Shell](./TechieDesk-DevGuide-Shell.md) |
| First run | 1 | [FirstRun](./TechieDesk-DevGuide-FirstRun.md) |

## 2. Architecture cheat-sheet

- **Head:** `apps/TechieDesk` — MAUI Blazor Hybrid (`net10.0-maccatalyst`; the Windows TFM is added
  only when building on Windows). `MainPage.xaml` mounts `Components/Routes` into a `BlazorWebView`;
  there is no Kestrel, no SignalR and no HTTP boundary inside the app.
- **Application services:** `apps/TechieDesk.Core` (plain `net10.0`) — extracted so the `net10.0`
  test project can reference them, since a test project cannot reference a platform-targeted one.
- **Data:** Dapper over SQLite, migrated by DbUp at launch (`apps/TechieDeskDb`). EF Core is banned.
- **RAG:** the `TechieRag` library (`src/TechieRag`) + `TechieRag.Embedded` (ONNX BGE-M3).
- **Background:** `apps/TechieDeskScheduler` hosts the *same* `SchedulerService` in a headless
  process so schedules run with the window closed.
- **UI kit:** TrBlazeUI components; icons via `LucideIcon`.

## 3. Roles and the menu map

**There is one role.** REQ-FN-041 **deleted** the role/capability stack — `CapabilityService`,
`AuthGuard`, `ProductRoleMapper` and the user↔workspace assignment set are gone, not disabled. BRD-129
makes the app account-free: it opens straight into a usable workspace and signing in only activates a
licence. The shell shows the local user as `Administrator · This Mac`.

> ⚠ **`docs/TechieDesk-UsageGuide.md` §4 "Test users" is STALE** — it still lists three AppManager
> roles (Superadmin / Manager / User) and a case study "CS-4 Roles & authorization" for a role model
> this build no longer has. That table should be reduced to the single local user plus, separately,
> any AppManager licence account. Flagged here, not edited: this task documents code, it does not
> rewrite other docs.

### Landing truth — read from the routing code, not inferred

- **App launch → `/`** (`Components/Pages/Home.razor:1`). `/` is a **redirect, not a dashboard**.
  `Home.razor:66` calls `Workspaces.ListForCurrentUserAsync()`; if the count is `0` it navigates to
  `/setup` (`Home.razor:69`), otherwise to `/workspace/{slug}` of the first workspace
  (`Home.razor:76`), both with `replace: true`.
- **First-run guard** — `MainLayout.GuardFirstRunAsync()` (`MainLayout.razor:519`) runs for every
  route: `/setup` is exempt (`:522`), any install with `workspaces.Count > 0` returns early (`:533`),
  otherwise an incomplete setup flag redirects to `/setup` (`:539`). Workspaces are checked *before*
  the flag, deliberately — a flag-first check threw established installs into the wizard.
- **Sign-in does NOT force a landing.** `Auth/Login.razor:128` calls `SignIn.SignInAsync(...)` and on
  success navigates to `SafeReturnUrl` (`:141`) — back where the user came from. There is no
  post-login dashboard and no route protection anywhere in the shell.

### Navigation surfaces

The sidebar (`MainLayout.razor`) has four groups — **WORKSPACE · ACCOUNT · OPERATOR · CONSOLE** —
covering 18 destinations. A **native macOS menu bar** (`MainPage.xaml.cs:69-77`) adds a *second,
non-overlapping* surface:

| Native "Go" menu | Route | Also in sidebar? |
|---|---|---|
| Home ⌘1 | `/` | no (it is a redirect) |
| RAG Chat ⌘2 | `/chat` | **no — menu only** |
| Document Ingestion ⌘3 | `/ingestion` | **no — menu only** |
| Token Usage ⌘4 | `/token-usage` | yes |
| Settings ⌘, | `/admin/settings` | yes (sidebar → App Settings) |
| RAG Configuration | `/rag-config` | yes (Console) |
| LLM Settings ⌘L | `/llm-settings` | yes |
| Data & Storage ⌘D | `/settings/data` | yes |

### The settings naming collision — fixed 2026-07-28

Three screens answered to some form of "settings", and the one a user means by the word was not the
one at `/settings`:

| Was | Screen | Now |
|---|---|---|
| `/settings` | "TechieRag Configuration" — embedding, vector store, processing | **`/rag-config`**, `RagConfig.razor`, Console sidebar entry |
| `/admin/settings` | "App settings" — the sidebar's App Settings | unchanged, and **⌘, now opens this** |
| `/settings/data`, `/settings/updates` | Data & Storage, Updates | unchanged — `/settings/*` is now purely a namespace |

`⌘,` is macOS's standard Settings shortcut and previously opened the RAG config screen. It now opens
App Settings; RAG Configuration got its own menu entry and sidebar link, since repointing the
shortcut would otherwise have stranded it.

> 🔴 **Open defect this rename exposed (logged to REQ-NFR-004).** `RagConfig.razor` and
> `AdminSettings.razor` inject the **same `TechieRagConfigService`** and both write
> `config.Embedding` and `config.VectorStore`, each from its own copy loaded on open. Change the
> embedding provider on App Settings, then save RAG Configuration from its stale copy, and the first
> change is **silently reverted**. Each screen also owns fields the other does not
> (`Processing`/`EnableTelemetry` vs `Llm`), so neither is redundant — the fix is to give the
> overlapping fields one owner, not to delete a screen.

### Orphaned routes — found, and now resolved

On 2026-07-28 four routes were found shipping with **zero inbound links** — no nav entry, no in-app
link, absent from the native Go menu — measured across every `href=`, `Href=` and `NavigateTo(` in
`Components/` plus `MainPage.xaml.cs`. All four have since been dealt with, two different ways:

| Route | Razor file | Resolution |
|---|---|---|
| `/tool-demo` | `ToolDemo.razor` (321 lines) | 🗑 **deleted** — execution trace survives in `WorkspaceChat` + `WorkspaceAgents`; REQ-RAG-006 evidence re-pinned there |
| `/settings/appearance` | `AppearanceSettings.razor` (29 lines) | 🗑 **deleted** — superseded duplicate; both panels mount at `AdminSettings.razor:175-176` |
| `/text-ingestion` | `TextIngestion.razor` (325 lines) | 🔗 **re-linked** into **Console** — sole delivery of REQ-RAG-004 (BRD-6, P0) |
| `/llm-playground` | `LlmPlayground.razor` (395 lines) | 🔗 **re-linked** into **Console** — sole delivery of REQ-RAG-005 (BRD-7, P0) |

The split matters: the first two were demo pages whose capability lived on elsewhere, so deleting
them lost nothing. The last two were the **only** place their P0 capability existed — no
`Textarea`/`IngestTextAsync` path on `DocumentLibrary` or `AddFromWeb`, and `LlmSettings` is provider
configuration, not a playground — so deleting them would have removed BRD-6 and BRD-7 outright. They
were re-linked instead, and both are live-verified reachable from the sidebar.

**No route in this app is currently unreachable.**

> ⚠ Two entries were added to the **Console** group that `docs/mockups/workspace-chat.html` does not
> contain. Console rather than Workspace was chosen so the mockup-frozen Workspace group (REQ-UI-048)
> stays intact. It is a deliberate, recorded divergence — worth an owner eye at the next mockup pass.

> ⚠ `TechieDesk.Core/Services/Appearance/AppearanceSettings.cs` is a **different type** that shares a
> name with the deleted page. It was not touched.

## 4. Screens

See the area files in the table above.
