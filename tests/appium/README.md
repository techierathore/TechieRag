# Mac Catalyst UI verification harness

**If you are the verifier (`*verify`), the build-phase self-smoke, or the DevGuide OBSERVE pass — USE THIS. Do not hand-roll an Appium sweep.**

This is the `REQ-NFR-011` harness that drives the TechieDesk Mac Catalyst head for
`verify-phase.md` §4a (data-render gate) and §4b (visual-truth gate). It exists because
a naive sweep gets four things wrong, each of which silently produces **false verdicts**.

> A hand-rolled sweep on 2026-07-30 reproduced all four at once and generated **21 false
> `VISUAL-FAIL`s** — every screen "failing" on a screenshot of the agent's own Terminal
> window. The results were discarded and no verdict was written. Read the four traps below
> before you consider writing your own.

## The four traps this harness already handles

| Trap | Why it produces a false verdict | Handled by |
|---|---|---|
| **Screenshots are full-desktop** | The mac2 driver screenshots the *screen*, not the window. Any window that takes focus — a terminal, an editor — occludes the app, and you then grade a picture of that window. | `crop_window()` crops to the window rect via `sips`; `snap()` always crops. |
| **The element tree is system-wide** | `/source` returns the whole accessibility tree. macOS menu-bar items (`About This Mac`, `App Store`) get graded as zero-size *app* controls, failing every screen identically. | `parse()` scopes to `.//XCUIElementTypeWindow`; the menu bar is walked **separately and deliberately** (for `Go`-menu navigation), never mixed into content. |
| **Focus is stolen mid-run** | A pointer click lands in whatever window is frontmost — not necessarily the app. This corrupts the smoke *and* stomps on the owner's desktop. | `activate()` (`macos: activateApp`) before every shot, and `click_rect()` **asserts `frontmost() == "TechieDesk"`** before every click. |
| **Reading the tree before it settles** | A fixed sleep after navigation races the render; you capture the shell only and under-report controls. | `wait_settled()` polls until the content-node count repeats; an unsettled read is flagged `settled: false` + a `warning`, never graded. |

Plus: a **missed click leaves the previous screen up**, and the sweep would file *that*
screen's controls under the new slug. `run_sweep.arrival_ok()` gates every screen on the
app's own breadcrumb and records `MIS-NAVIGATED` instead of grading.

## Usage

```bash
# 0. Prereqs — Appium + WDA must be up (see below)
cd /Users/MyCode/TechieRag

# 1. Start a bound session (pins appium:appPath — 8 bundles share the bundle id)
#    ...and CONSUME IT IN THE SAME COMMAND. This host is a VM and WDA drops sessions
#    between shell invocations; see the 2026-07-31 trap table.
python3 tests/appium/drv.py new >/dev/null && python3 tests/appium/run_sweep.py

# ...or only certain screens
python3 tests/appium/drv.py new >/dev/null && \
  python3 tests/appium/run_sweep.py document-library workspace-agents

# 3. Tear down
python3 tests/appium/drv.py quit
```

`run_sweep.py` **pre-flights before it touches a screen** and refuses with a numbered
list rather than producing 21 identical confusing failures. It checks: Appium ready, WDA
ready, the session actually alive, no stale REQ-FN-051 lock file, exactly one TechieDesk
head running, and that every navigation resource key still exists in `AppStrings.resx`.
Exit `2` = refused by pre-flight (nothing was swept); exit `3` = the session died
mid-sweep and the run **aborted at that screen** instead of burying the cause under 20
copies of the same error; exit `0` = it ran. `TD_SKIP_PREFLIGHT=1` bypasses the checks.

Results land in `test-results/ui-verify/`: one `<slug>-<width>.json` per screen (content
nodes, geometry findings, `settled`, `arrived`) plus cropped screenshots. `sweep-results.json`
is the roll-up, and each entry also records `navKey` / `navLabel` — the resource key the screen
is known by, and the selector that actually navigated it: `id=nav-…` (locale-invariant,
REQ-UI-053) or `label=…`, which also tells you which language the screen was graded in.
`run_sweep.py` is resumable — it merges into the existing results file, so a crash mid-sweep
does not lose completed screens.

The console line reads `ov=<visible>/<total>` overlaps — grade the **visible** number, and
read the fifth trap below before filing either one.

**Read failures, not successes.** Only open screenshots for screens that flagged something.

## Navigation is LANGUAGE-INDEPENDENT — how, and what to do if it breaks

**Do not put an English label in `run_sweep.SIDEBAR`.** The third column is a **resource
key** (`NavQdrantAdmin`), not a caption.

### Two selectors: the link's `identifier` first, its label second (REQ-UI-053)

`nav.sidebar_key(key)` tries **two selectors in order** and reports which one worked:

1. **`identifier`** — `nav.NAV_IDS` maps each resource key to the route-derived `id`
   `MainLayout.razor` puts on that link (`/settings/data` → `nav-settings-data`; the
   per-install workspace slug is dropped, so `/workspace/{slug}/agents` →
   `nav-workspace-agents`). No resource lookup, no `strings.py`, no product resource keys —
   the selector is the same string in every language and survives a key rename.
2. **the displayed label** — the REQ-NFR-011 path below. `detail` in the returned
   `(ok, detail)` — and `navLabel` in `sweep-results.json` — reads `id=nav-…` or `label=…`,
   so every result records which mechanism actually navigated that screen.

**⚠ Which one is live is an OPEN QUESTION — check, do not assume.** REQ-UI-053 added the
ids on 2026-08-01; whether they reach the macOS accessibility tree **has not yet been
measured on a running head**, and the desk evidence points the wrong way:

- The `id` reaches the DOM. Proven by rendering `SidebarMenuButton` through
  `HtmlRenderer` — the unmatched attribute splats onto the anchor:
  `<a href="/qdrant-admin" data-state="closed" id="nav-qdrant-admin" class="…">`.
- Whether the DOM id reaches `identifier` is **WebKit's** decision, not Blazor's, and
  WebKit's iOS-family wrapper (which is what Mac Catalyst uses) implements
  `accessibilityDOMIdentifier`, returning `identifierAttribute()` — **not**
  `accessibilityIdentifier`, which is what `XCUIElement.identifier` reads. `WebDriverAgentMac`
  (`XCUIElement+AMAttributes.m`) can only serve `XCUIElement.identifier`, so if that is the
  real mapping there is no second channel to ask down. The sidebar's measured
  `identifier=""` on 2026-07-31 is consistent with this — but not evidence, because on that
  build no link had an `id` at all.

**So run the probe on a freshly built head and record the answer here:**

```bash
python3 tests/appium/drv.py new >/dev/null && python3 tests/appium/nav.py ids
```

It prints `identifier=… label=…` for every sidebar link, then how many of `NAV_IDS` are on
screen (a signed-in shell renders 22 of the 23 — `nav-login` appears only when signed out).
Exit `1` = **not one** identifier reached the tree.

| What you see | What it means | What to do |
|---|---|---|
| `identifier=<empty>` on every link, on a head built after 2026-08-01 | WebKit does not surface the DOM id as `XCUIElement.identifier` on Mac Catalyst. **This is the outcome the desk evidence predicts.** | Nothing breaks: the sweep is running on the label fallback and still passes 22/22. Record it as a measured finding, keep `NAV_IDS` and the ids in `MainLayout` (they cost nothing and are the standard testability handle), and stop looking for a markup fix — the next thing to try is a native one (a `WKWebView` AX shim, or a `Go`-menu entry per route), not another HTML attribute. **Do not** reach for `aria-label` as a smuggling channel: it overwrites the accessible name and breaks REQ-NFR-005 to serve a test. |
| Ids present, every screen says `id=nav-…` | The mechanism works — the harness no longer depends on the product's resource keys. | Delete nothing: `strings.py` still drives `arrival_ok()` and the chromeless markers. |
| One screen says `label=…` while the others say `id=…` | That route was renamed on one side of the contract. | Update **both** `MainLayout.razor`'s `id` and `nav.NAV_IDS` — two halves of one contract, and nothing else in the harness knows those strings. |

REQ-UI-050 took localization coverage from 6.4% to 46.3% on 2026-07-31. The sidebar is
fully localized, so with `AppearanceLanguage='hi'` it renders `चैट · दस्तावेज़ · कनेक्टर ·
एजेंट · Qdrant एडमिन · LLM सेटिंग्स …` and the old hardcoded-English table failed **every
single screen** with `nav link '<X>' NOT FOUND`. The breadcrumb is localized too, so even
a click that landed would then have failed `arrival_ok()`'s route-segment match.

What `MainLayout.razor` gives us, and what the harness exploits:

- Each sidebar entry renders `<span>@Localizer["NavXxx"]</span>`, and
- `MainLayout.CurrentTrail` renders the **same key** as the breadcrumb's page rung.

So **one resource key per screen drives both the click and the arrival proof**, in any
language. `tests/appium/strings.py` resolves that key against the product's own
`apps/TechieDesk.Core/Resources/AppStrings*.resx`:

1. `strings.app_language()` reads `InstanceSetting.AppearanceLanguage` from
   `~/Library/Application Support/TechieDesk/techiedesk.db` **read-only** — the same row
   `LanguageStore` reads, so the harness follows the app instead of being told.
   (Columns are `SettingKey`/`SettingValue`, not `Key`/`Value`.)
2. `strings.all_candidates(key)` returns that language's value **first**, then neutral
   English, then every other shipped language. Widening past the DB's language is free
   and removes a whole failure class (a Release bundle whose satellite assemblies predate
   a resx edit; a process started before the language row changed). It cannot produce a
   wrong result — the key names the *screen*, not the language, and `arrival_ok()` still
   has to agree before anything is graded.
3. `nav.sidebar_key(key)` clicks the first candidate that is present and returns
   `(ok, detail)`; on a miss `detail` lists what was wanted **and every label the sidebar
   is actually showing**, because "link not found" without the observed labels is an
   unanswerable bug report.

**If navigation breaks, it will be a KEY RENAME, and it fails loudly.** Pre-flight calls
`strings.missing_keys()` and refuses the sweep naming the dead keys. Fix = update the
third column of `SIDEBAR` to the new key. A *re-translation* can never break it.

Chromeless (AuthLayout) screens have no breadcrumb, so `CHROMELESS` holds a marker
string instead; a marker may be a literal or `key:<ResourceKey>`, and a key whose value
carries `{0}` placeholders is matched on all of its literal fragments.

> **~~Localization gaps observed while building this~~ — CLOSED 2026-08-01/02, note kept for
> the lesson.** This used to say the native macOS menu bar and the auth screens were hardcoded
> English, and that this was *convenient* for the harness because `recover_to_app()` drives the
> `Go` menu by its English caption. Both were localized by REQ-UI-052, and that convenience
> turned into a break: `go_menu()` matched nothing in a Hindi app and would have stranded every
> screen after `/login`. It now resolves resource keys, as `sidebar_key()` does.
>
> **The generalisable rule, learned three times over:** *localizing a surface silently
> invalidates any harness that identifies it by English text.* It has now broken `sidebar()`
> (REQ-UI-050), `go_menu()` (REQ-UI-052) and `run_sweep.CHROMELESS` (which identified `/login`
> by a literal REQ-UI-052 had just translated). **Anything in this harness still matching a
> product string literally is a latent break.** A test asserting no selector is an English
> literal is the durable fix and is not yet written.

### Why not the other two designs

- **Route-based navigation** would have been best — routes are locale-invariant and
  `SIDEBAR` already carries one per screen — but it cannot reach 21 screens. The native
  `Go` menu is the only route-driving surface (`MainPage.NavigateAsync` →
  `NavigationManager.NavigateTo`) and it hardcodes **8** routes, none of them the
  `/workspace/{slug}/…` five. The bundle registers **no `CFBundleURLTypes`**, so there is
  no deep-link scheme to `open`, and the mac2 driver cannot execute JS inside a
  `BlazorWebView`. `go_menu()` stays as the *recovery* path off the chromeless screens,
  which is all it can cover.
- **A locale-invariant handle on the links** did not exist when this was written. Measured
  on the live app 2026-07-31: `identifier` was `""` on every sidebar `XCUIElementTypeLink`,
  and the element's `href` is not surfaced anywhere in the AX tree (`label`, `value`,
  `title`, `placeholderValue` all carry the display text and nothing else). Per
  `verify-phase.md` §3b a missing `AutomationId` is **a coding-standard defect, not a
  licence**, so REQ-UI-053 (2026-08-01) put a route-derived `id` on all 23 sidebar entries
  and made it `sidebar_key()`'s first-choice selector — see *Two selectors* above, including
  the open question of whether WebKit lets it through. The label path stays as the fallback
  either way.

## Five more traps, found 2026-07-31 — read these before you debug anything

| Symptom | Cause | What to do |
|---|---|---|
| **Every screen fails with `TypeError("a bytes-like object is required, not 'dict'")`** | The session was DEAD and `drv.screenshot()` base64-decoded the W3C *error object* instead of a screenshot. Three sweeps were lost chasing this before the real cause was found. | **Fixed 2026-07-31.** `drv._unwrap()` now raises `drv.SessionError` naming the driver's own error, and `run_sweep.py` aborts the whole run (exit 3) instead of repeating it 21 times. Note the detection keys off the `error` member, *not* dict-ness — `/timeouts` and `/window/rect` legitimately answer with dicts. |
| **Session dies between two shell commands** | This dev host is a **VM** (`machineName: Ss-Virtual-Machine`) and WebDriverAgentMac is unstable on it: a session created by one `python3 drv.py new` invocation is frequently terminated before the next command runs. Three full sweeps were lost to this. | **Create the session and consume it inside ONE uninterrupted command**: `python3 tests/appium/drv.py new >/dev/null && python3 tests/appium/run_sweep.py <slugs>`. Sweep in chunks; `run_sweep.py` merges results, so chunks compose. Pre-flight now names a dead session up front. |
| **WDA fails `Timed out while enabling automation mode` even though developer mode is enabled** | A stale `testmanagerd`. | `pkill -9 testmanagerd` then restart WDA. ⚠ **This does NOT need `sudo`** — contrary to the recipe below, `testmanagerd` runs as the user (uid 501), so an agent can clear it unaided. It may take one extra WDA restart cycle for the fresh `testmanagerd` to settle. |
| **`nav link '<X>' NOT FOUND` for every screen** | `run_sweep.py`'s `SIDEBAR` table hardcoded **English** labels, so the harness could not navigate a localized app. At 6% localization coverage this never mattered; at 46.3% (REQ-UI-050) it broke the sweep outright. | **Fixed 2026-07-31** — see *Navigation is LANGUAGE-INDEPENDENT* above. Do not "fix" a future recurrence by translating labels by hand; update the resource **key**. |
| **An overlap between two controls that visibly do not touch** | macOS reports elements inside an `overflow`-scrolled container at their **UNCLIPPED LAYOUT** position, not where the pixels are. A list parked at the top reports its scrolled-away rows below the window floor, where they phantom-intersect whatever sits there. Proven twice in `workspace-chat-n1024.json`: the sidebar's own scrollback at `Data & Storage @86,788`…`RAG Configuration @86,1115` and the chat transcript's later turns at 809…1427, all past a window floor of 762. A `980 px²` `Read this answer aloud` × `Send message` overlap was reported as a defect and was pure artifact. | `visual_check()` now tags every overlap `visible` or `clipped` and reports `overlapCountVisible` / `overlapCountClipped`; `run_sweep.py` prints `ov=visible/total`. **Grade `overlapCountVisible`.** Nothing is suppressed — the AX tree carries no scroll offset or clip rect, so "clipped" is an *inference* from the element lying outside the window, and a real defect can hide under a phantom (the same capture also had a composer running 149 px past the window bottom — the actual REQ-UI-044 bug). Read the clipped ones before dismissing them. Conversely, `offWindow` findings on a scrollable container are usually just scroll. |

**One more interaction, from REQ-FN-051:** the single-instance guard means a sweep that launches the app **while a previous instance still holds the data-directory lock** gets the *"TechieDesk is already running"* refusal window instead of the app. Before creating a session, kill any running head and remove `techiedesk.lock` / `techiedesk.instance.json` from the data directory.

## Harness prerequisites (one-time, owner-run)

Appium `:4723` + WebDriverAgentMac `:10100` must be running, and **developer mode must be
enabled**:

```bash
sudo DevToolsSecurity -enable          # needs a real TTY — an agent cannot do this
sudo pkill -9 testmanagerd

appium --port 4723 &
cd ~/.appium/node_modules/appium-mac2-driver/WebDriverAgentMac && \
  xcodebuild test-without-building -project WebDriverAgentMac.xcodeproj \
             -scheme WebDriverAgentRunner -destination 'platform=macOS,arch=arm64' &
```

With `DevToolsSecurity` disabled, `_developer` group membership alone is **not** enough:
macOS falls back to an interactive auth prompt that a headless session cannot answer, and
WDA fails after 60s with `Timed out while enabling automation mode` (exit 65). That symptom
means *developer mode*, not a missing permission.

## Files

- `drv.py` — W3C WebDriver client (stdlib only). Session pinned with `appium:appPath`.
  Note `pointer_click()`: `element/click` **no-ops on WKWebView content**, so clicks go
  through W3C pointer actions at the element's centre. `SessionError` + `_unwrap()` turn a
  dead session into one plain message; `session_alive()` is the cheap pre-flight probe.
- `strings.py` — locale-aware label resolution: `app_language()` (read-only, from the app's
  own settings DB), `table()`, `candidates()`, `all_candidates()`, `missing_keys()`.
  Reads `apps/TechieDesk.Core/Resources/AppStrings*.resx`; writes nothing.
- `sweep.py` — the gates: `parse()`, `content_nodes()`, `visual_check()`, `wait_settled()`,
  `sweep_current()`, `outside_window()`, window crop + resize helpers.
- `nav.py` — `sidebar_key()` (**use this** — identifier first, resource-key label second),
  `NAV_IDS` (resource key → the link's route-derived `id`; the other half lives in
  `MainLayout.razor`), `sidebar_id()` (identifier only, no resource lookup), `id_probe()`
  (`python3 tests/appium/nav.py ids` — what the running head actually exposes),
  `sidebar()` (raw label, ad-hoc use only), `sidebar_labels()` (miss diagnostic),
  `go_menu()` (native `Go` menu, for routes with no sidebar entry), `breadcrumb()`
  (where am I *really*), `desktop()` / `narrow()` widths.
- `run_sweep.py` — `preflight()` plus the driver loop over all sidebar-reachable screens,
  both widths. `SIDEBAR` = `(slug, route, resource-key)`.
- `update_devguides.py` — writes observed render/visual tags back into `docs/devguides/`
  (verify-phase §6b).
- `menu_check.py` — REQ-UI-054. Asserts UIKit really DREW the menu bar `MainPage` declares.
  Reads the declaration out of `MainPage.xaml.cs` (so it cannot drift from the product),
  resolves every caption through `strings.py`, then checks the live menu bar — titles and
  items from the AX tree, key equivalents from `AXMenuItemCmdChar`/`AXMenuItemCmdModifiers`
  via System Events, which WebDriverAgent does not expose at all. Exit `0`/`1`/`2` (pass /
  a named diff / refused).

## The menu bar needs a RUNTIME check — a source scan structurally cannot cover it

Run this in **both** languages whenever anything touches `MainPage.BuildMenuBar`,
`AppDelegate.BuildMenu`, or a `Menu*` accelerator:

```bash
python3 tests/appium/drv.py new >/dev/null && python3 tests/appium/menu_check.py
```

**Why it exists.** `MenuBarLocalizationTests` (REQ-UI-052) reads the same source file and is a
genuine ratchet for *localization* — but its own header says what it cannot do: *"It does NOT
prove UIKit drew them."* On 2026-08-01 that gap cost an entire menu. The app declared four
menus and eighteen items; macOS drew three menus and fifteen. `Zoom In` / `Zoom Out` /
`Actual Size` were absent from the running menu bar in **both** languages, and **every test in
the repo was green**, because nothing readable from disk was wrong.

The cause was `UIMenuBuilder`, and the behaviour is worth knowing before you add any shortcut:

> Every Mac Catalyst app is given a stock `Format ▸ Font ▸ Text Size` group holding *Bigger*
> (⌘+) and *Smaller* (⌘−). If an app menu re-declares a key equivalent UIKit has already
> handed out, **the whole menu containing it is discarded** — not just the clashing item.
> Silently: no exception, no log, nothing in any source file.

Measured on the running head 2026-08-01 with a three-menu probe build: a menu whose only item
took ⌘0 was drawn; a menu whose only item took ⌘+ was not; and a menu holding one ⌘+ item
*alongside four valid ones, including one with no shortcut at all*, was not drawn either. A
later accidental proof: moving App Settings from ⌘, to ⌘; (already owned by
`Edit ▸ Spelling and Grammar ▸ Check Document Now`) made the **entire Go menu** vanish.

So a colliding shortcut does not cost you a shortcut — it costs you a menu. `menu_check.py` is
the only thing in this repository that can see it. `AppDelegate.SupersededStandardMenus` is the
other half: it removes the superseded stock group so the app's own ⌘+/⌘− survive.

**Run it in both languages, not one.** An English menu title that matches a `UIMenuIdentifier`
(`File`, `View`, `Help`) is *merged into* the stock menu of that name; a Hindi one
(`फ़ाइल`, `दृश्य`, `मदद`) becomes its own top-level menu instead. Those are two different code
paths in `AppDelegate.BuildMenu` and only running both exercises both. Set the language in the
app's own settings row **before** launching — UIKit builds the menu bar once, at launch:

```bash
sqlite3 ~/Library/Application\ Support/TechieDesk/techiedesk.db \
  "update InstanceSetting set SettingValue='hi' where SettingKey='AppearanceLanguage';"
```

## Rules that apply here

- **Black-box.** These read the running app; they never modify application source.
- **Never write `Verified` from a sweep alone.** A REQ is `Verified` only via an executed
  `verify-phase` run that also writes the ledger `docs/.last-verify.json`. The PreToolUse
  hook `.tfcore/hooks/guard-verify.sh` enforces it.
- **Never write the ledger unless you actually ran the gates.** That is the audit record.
- **Git is manual** — never run `git`/`gh` from these scripts or while using them.
