---
project: TechieRag
stack: .NET 10 class library (NuGet) + TechieRag.Embedded (ONNX BGE-M3) + TechieRag.Telemetry (opt-in OTel) + TechieDesk desktop app (MAUI Blazor Hybrid — macOS + Windows, TrBlazeUI, apps/TechieDesk)
last_updated: 2026-08-02
current_phase: Build — 100 open; service-layer English 569 → 330
last_verified_build: PASS
last_verified_date: 2026-08-02
---

# TechieRag — Status

Configurable .NET 10 RAG library (NuGet) + TechieDesk product app. **This file is the dashboard only** —
per-REQ evidence lives in `docs/TechieDesk-Checklist.md` (Requirements Status table); library defects in
the per-library feedback files.

## Where I am

One `*build-phase` fanned **six ways** in parallel on `REQ-UI-055`, with `*verify all` chained inline.
Build **0 errors**, **2,180 tests pass**. Service-layer English **569 → 330** — a 239-literal
reduction — and the ratchet is lowered to 330 so it cannot be given back. **23/23 screens sweep clean
in Hindi.** Two new rows raised from what the work uncovered.

## Next command to run

```
/TechieFlow:agents:flow-master *build-phase TechieDesk   (OpenCode: /flow-master *build-phase TechieDesk)
```

Build leads because `REQ-UI-056`, `REQ-UI-057`, `REQ-RAG-045`, `REQ-RAG-046` are unbuilt and `REQ-UI-007`,
`REQ-FN-045`, `REQ-FN-051`, `REQ-RAG-044`, `REQ-UI-040` are `PARTIAL`. ⚠ **But of those nine, only
`REQ-UI-057` (a test-fixture port race) is buildable without you** — `REQ-UI-056` needs your
persisted-English decision, `REQ-RAG-045/046` are BRD-deferred by your decision, and the five `PARTIAL`
rows are environment-gated. **The higher-value moves are yours, not another build pass:** start Ollama or
Docker (unlocks most of the 44 `Needs re-verify` rows), or decide `REQ-UI-056`.

## Open requirements

- **TechieDesk: 100 open** of 170 — **70 terminal** (52 `Verified` + 18 `N/A`). **0 `FAIL`.** Open: 44
  `Needs re-verify`, 37 `Implemented`, 10 `Blocked`, 5 `PARTIAL`, 2 `Planned`, 2 `Not Started`.
- **Delivered**: `REQ-UI-055` → `Implemented` 75%. Six slices converted — connectors −32, scheduling −62,
  licensing −38, backup/Docker −62, leaf services −18, agent skills −3. ~250 keys, parity intact.
- **Raised**: `REQ-UI-056` (65 deferred literals + a persisted-English **policy decision**),
  `REQ-UI-057` (a test flake three clusters hit independently).

## Known blockers

- 🔴 **DECISION NEEDED (`REQ-UI-056`): persisted English in the database.** `ScheduleRunItem.Reason`,
  `ScheduleRun.FailureReason` and `ScheduleRun.Detail` hold English rows already on disk that cannot be
  re-rendered, so any conversion needs a **permanent** key-or-legacy-text fallback. Two clusters found
  this independently and neither would decide it alone. 65 connector literals wait on it.
- 🔴 **THE ENVIRONMENT IS THE CEILING, not the pace.** ~58 of the 100 open rows need something this
  machine does not have: an LLM provider (Ollama), a reachable AppManager, Docker, IMAP, Postgres, an
  Apple signing identity, or Windows platform sources. **Ollama and Docker alone would unlock most of
  the 44 `Needs re-verify` rows.** Only you can provide these.
- ⚠ **The licence banner has never been seen on screen.** `MainLayout.razor:524` was this pass's
  highest-visibility fix, but it renders only when AppManager supplies a licence message — a live probe
  found zero licence text on this install. Proven by test in both cultures; unproven visually.
- ⚠ **Read-aloud in Hindi is unverified on a real Mac.** `MauiReadAloudService` spoke with no
  `SpeechOptions.Locale`, so Hindi answers were being **silently skipped by an English voice** — a
  pre-existing defect now gated on `CanSpeakAsync`. Whether a `hi-IN` voice is installed is a per-machine
  fact only a run can answer.
- ⚠ **~2,900 Hindi keys are agent-produced**, not a native speaker's. Terms wanting a ruling: `सीट` (seat),
  `ग्रेस अवधि` (grace period), `View → दृश्य`, `Provider → प्रोवाइडर`. One known infelicity is shipped and
  recorded in code (`CronDescriber` repeats तारीख़ in a multi-date list).
- ⚠ **`REQ-UI-057`**: a `ConnectorEndToEndTests` port-binding flake makes the suite non-deterministic.
  Three clusters hit it independently — the danger is it teaches people to re-run rather than investigate.
- ⚠ **330 service literals remain**, a substantial share of which is *deliberate* machine-facing text the
  counter cannot distinguish from prose. That is why it is a ratchet, not a zero gate.
- 🔴 **`REQ-FN-043`** Apple signing identity — one owner decision gating four REQs.
- 🔴 **`REQ-FN-035`**: the Windows head has no platform sources.
- ⚠ **OWNER DECISIONS carried**: MCP threat model; a restored `.tdbak` mints a new install identity and
  burns a seat; `REQ-UI-040`'s run-from-chat and the flow-builder vocabulary both want a `*mockups` pass.
- ⚠ **`REQ-NFR-001`** PAT still in `nuget.config` history — owner must untrack and rotate.

## Library feedback summary

- **TrBlazeUI — 28 entries.** **TR-024 closed-with-workaround** (`Select.DisplayTextSelector`, now applied
  to 14 Selects). **TR-039** (`NumberInput` does not exist; the wrong guess renders an invisible control).
  **TR-038** (reference-doc parameter tables materially incomplete). **TR-008 corrected.**
- **TechieRag — 43 entries.** TR-RAG-041 fixed in-library; TR-RAG-042/043 open.

## Standards compliance

- Build **0 errors**; **2,180 tests pass** (1,519 app + 661 lib), 28 skipped.
- Markup localization **100%** under a **deliberately widened** definition; the numerator definition is
  untouched, so 45/117/942/1709/2280 remains one comparable series.
- Greps clean: no `a`/`v`/`obj` field prefixes, no underscore fields, no underscored test names.
- ⚠ Pre-existing: `MauiProgram.cs` `SCREAMING_SNAKE_CASE` env var; ~14 CS1591 in `TechieRagManager.cs`.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-02 | **`*build-phase` (6 parallel clusters) → `*verify all` inline** | ✅ **2,180 tests pass, 0 errors, 23/23 screens clean in Hindi at both widths.** 🔴 **Service-layer English 569 → 330** and the ratchet lowered to match, so the gain is locked. `CronDescriber` was the hard one and was solved properly — Hindi reverses three joins, so it now composes from whole localizable patterns, and there is deliberately **no English default overload**. 🔴 **A live pre-existing defect found**: read-aloud spoke with no locale, so Hindi answers were being **silently skipped by an English voice**. 🔴 Traps avoided by evidence: the audience split was *found* at `AgentLoopRunner.cs:126`, not judged; the Turkish-i trap in promo codes; a bound value that was the English label. ⚠ **Three mutations came back GREEN and were reported** — including a `.resx` heredoc that silently never applied, which would fake a RED-proof invisibly. ⚠ **My own errors**: I told all six clusters the ratchet would go red (it is a ceiling); and a probe mutation of mine poisoned intermediates across three projects and looked like a real compile error | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-02 | **`*build-phase` (2 clusters) → `*verify all` inline** | ✅ **2,073 tests pass, 0 errors. Both defects the previous smoke found are FIXED and verified live.** 🔴 `REQ-UI-054`: root cause proven and it was **not** the title collision I hypothesised — UIKit hands every Catalyst app a stock `Format ▸ Font ▸ Text Size` owning `⌘+`/`⌘−`, and `UIMenuBuilder` **discards the entire menu** that re-declares an owned key, silently. Proved by probe (a `⌘0`-only menu drew, a `⌘+`-only menu did not); in Hindi the identifiers ran `td.menu.0/1/3` with `td.menu.2` missing. Fixed in `AppDelegate`; `दृश्य` now shows `ज़ूम इन`/`ज़ूम आउट`/`वास्तविक आकार`. 🔴 `REQ-UI-051` service layer: services now return **invariant keys** — chosen because the page-level mapping was already leaking. `/settings/data` verified in Devanagari with paths still Latin. 🔴 **569 service-layer literals found** → `REQ-UI-055`, frozen by a ratchet. ⚠ **Three of my own recorded claims were wrong and are corrected**: the impact of `REQ-UI-054` (the shortcuts DID work when the web view had focus), its root cause, and my inference about what a cluster had been doing. ⚠ A mutation exposed that **a missing Hindi key silently resolves to English** | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-01 | **`*build-phase` (4 clusters) → `*verify all` inline** | ✅ **2,055 tests pass, 0 errors, 23/23 screens clean at both widths in English AND Hindi — including the native menu bar in Devanagari.** 🔴 **Markup localization COMPLETE (100%)**, and the counter was widened on purpose so its own number would stop flattering us. 🔴 `REQ-UI-051`: the biggest find was unbriefed — `LlmSettings`' entire provider form lives in a `RenderFragment` inside `@code`, invisible to the counter while the page read zero. 🔴 **Two defects found by looking at the running app**: the `View` menu never reaches the menu bar (`REQ-UI-054`), and service-layer English still renders on `/settings/data`. 🔴 **`REQ-UI-053` DISPROVEN by probe** — a DOM `id` does not cross the BlazorWebView boundary; `nav-` appears 0 times in the AX tree. ⚠ **A harness gap closed for the second pass running**: `/settings/backup` had shipped for days and had never been graded. ⚠ **Third instance of one pattern**: localizing a surface silently invalidated a harness selector (`CHROMELESS` this time) | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-01 | **`*build-phase` (3+1 clusters) → `*verify all` inline** | ✅ 2,019 tests. BRD-123 orchestration framework + BRD-92 flow builder, proven across a kill-and-relaunch. `REQ-UI-050` 48.2% → 80.1% | [table](docs/TechieDesk-Checklist.md#requirements-status) |

## Deferred / future

- **`REQ-UI-056`** — the 65 deferred connector literals, once the persisted-English policy is decided.
- **`REQ-UI-057`** — the connector test-fixture port race.
- A test asserting `run_sweep.SIDEBAR` covers every `SidebarMenuButton`, and one asserting no harness
  selector is an English literal — that pattern has now bitten three times.
- Wiring `menu_check.py` into `verify-phase`; it is the only check that can see the REQ-UI-054 class.
- Library-side English in `src/TechieRag` (`GuardedToolHandler`, `FlowRunner` block messages) — what a
  Hindi user actually sees for a flow tool-call block. No cluster owns it.
- `REQ-UI-040` run-from-chat + a `*mockups` pass; `REQ-RAG-045/046`; `PgVectorStore`'s real-Postgres test.
