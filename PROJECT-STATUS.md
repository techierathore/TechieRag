---
project: TechieRag
stack: .NET 10 class library (NuGet) + TechieRag.Embedded (ONNX BGE-M3) + TechieRag.Telemetry (opt-in OTel) + TechieDesk desktop app (MAUI Blazor Hybrid — macOS + Windows, TrBlazeUI, apps/TechieDesk)
last_updated: 2026-08-02
current_phase: Build — 106 open; service-layer English 330 → 189
last_verified_build: PASS
last_verified_date: 2026-08-02
---

# TechieRag — Status

Configurable .NET 10 RAG library (NuGet) + TechieDesk product app. **This file is the dashboard only** —
per-REQ evidence lives in `docs/TechieDesk-Checklist.md` (Requirements Status table); library defects in
the per-library feedback files.

## Where I am

One `*build-phase` fanned **five ways** in parallel, with `*verify all` chained inline. Build **0
errors**, **2,225 tests pass** (was 2,180). Service-layer English **330 → 189**. The persisted-English
policy you were blocked on is **decided and built**. **23/23 screens sweep clean** in Hindi at both
widths and the native menu bar passes in **both** languages.

Two rows reached `Verified` on met acceptance. **Six new rows were raised**, and the most important one
came from looking at a picture, not from a test.

## Next command to run

```
/TechieFlow:agents:flow-master *build-phase TechieDesk   (OpenCode: /flow-master *build-phase TechieDesk)
```

Build leads, and this time **most of it is buildable without you** — the opposite of last pass.
`REQ-FN-053` (a P1 hang), `REQ-UI-058`, `REQ-UI-059`, `REQ-RAG-051`, `REQ-NFR-015` and `REQ-NFR-016`
are all actionable now, and `REQ-UI-059` needs no new decision because it is the policy you already
made, applied to a second table.

## Open requirements

- **TechieDesk: 106 open** of 178 — **72 terminal** (54 `Verified` + 18 `N/A`). **0 `FAIL`.** Open: 44
  `Needs re-verify`, 39 `Implemented`, 10 `Blocked`, 6 `Planned`, 5 `PARTIAL`, 2 `Not Started`.
- **Verified**: `REQ-UI-057` (test-fixture port race — acceptance met literally: 20 consecutive
  full-suite runs, zero occurrences), `REQ-NFR-014` (three harness guards, 12/12 mutations RED).
- **Implemented**: `REQ-UI-056` 90% (persisted run history now code+args), `REQ-UI-055` 85%
  (330 → 189), `REQ-RAG-050` 80% (library emits 20 coded messages).
- **Raised**: `REQ-UI-059`, `REQ-UI-058`, `REQ-FN-053`, `REQ-RAG-051`, `REQ-NFR-015`, `REQ-NFR-016`.

## Known blockers

- 🔴 **`REQ-UI-059` — persisted English in the CHAT TRANSCRIPT, found by the visual gate.** Driving
  `/workspace/{slug}` in Hindi showed two English sentences on an otherwise complete Devanagari screen.
  The Hindi translation **exists and is correct** — the strings were localized at *write* time and
  frozen into `TrMessage` (3 rows of the provider message, 2 of the egress refusal). **Same defect
  class as `REQ-UI-056`, on the primary screen, and here the legacy rows are real chat history, not 22
  disposable fixtures.** No geometry check could have caught this; it came from reading a screenshot.
- 🔴 **The "model-facing" exclusion was wrong at the render seam.** Two clusters independently
  classified `EgressGate` and `AgentToolHandler` text as machine-facing and out of scope. The
  reasoning was right — do not translate what the model reads — but the premise was not: the UI
  renders that tool-result content **verbatim to the user**. Folded into `REQ-UI-059`.
- 🔴 **`REQ-FN-053` P1 — a Tool-node flow run never completes** once the egress prompt is answered,
  either way. Reproduced 4×, two tool names, both answers, no exception logged. Blocks live proof of
  the whole tool-call guardrail path, and therefore `REQ-RAG-050` / `REQ-UI-058`.
- 🔴 **`REQ-RAG-051` is the FOURTH lying-surface defect** (`ConfirmEgress`, Save & apply, thread
  export, now `ForFlow` returning `IsSuccess = true` for a blocked run so the trace renders green).
  Four instances makes it a systemic pattern, not incidental.
- 🔴 **THE ENVIRONMENT IS STILL THE CEILING.** Re-probed this pass, and one claim was understated:
  **Docker is not installed at all** (`command not found`), not merely stopped — so `REQ-RAG-044`'s
  Testcontainers path is impossible here, not just inconvenient. AppManager `192.168.1.14:5101`
  returns **`000`**. Ollama, Qdrant, Postgres all down. **None of the 44 `Needs re-verify` rows was
  promoted**, and none can be without you.
- ⚠ **`REQ-UI-053` now has a measured answer, and it is worse than "open question":** **0 of 23**
  sidebar identifiers reach the macOS AX tree. The sweep runs *entirely* on the resource-key label
  fallback, which makes `REQ-NFR-014`'s no-English-selector guard load-bearing today.
- ⚠ **`REQ-NFR-015`: `/register` and `/setup` have never been swept by anything** — arrival markers
  exist and look deliberate, but the driver loop iterates `SIDEBAR` and neither screen has a row.
- ⚠ **The licence banner has still never been seen on screen**; **read-aloud in Hindi is still
  unverified on a real Mac**; **~2,900 Hindi keys remain agent-produced**.
- 🔴 **`REQ-FN-043`** Apple signing identity; **`REQ-FN-035`** Windows platform sources;
  **`REQ-NFR-001`** PAT still untracked-and-unrotated. All owner-only.

## Library feedback summary

- **TrBlazeUI — 28 entries.** TR-024 closed-with-workaround; TR-039, TR-038 open; TR-008 corrected.
- **TechieRag — 43 entries.** TR-RAG-041 fixed in-library; TR-RAG-042/043 open. New this pass:
  `ConnectorException` is sealed and English-only, so an app-authored refusal cannot carry a code
  (hence `ConnectorSetupException`); `Models.AgentStep`/`ToolResult` have no code slot.

## Standards compliance

- Build **0 errors**; **2,225 tests pass** (1,549 app + 676 lib), 28 skipped.
- Service-layer English ratchet **330 → 189**, and the derivation is recorded in the constant itself:
  **118 real conversions**, plus **23 that were never debt** — the counter matched `logger.Log…` but
  not `logger?.Log…`, so log templates were being counted as prose. The instrument was fixed, not the
  code. Nobody should later read 141 as 141 conversions.
- Harness guards 8/8; markup localization 100%; greps clean (no `a`/`v`/`obj` prefixes, no underscore
  fields, no underscored test names).
- ⚠ Pre-existing: `MauiProgram.cs` `SCREAMING_SNAKE_CASE` env var; ~14 CS1591 in `TechieRagManager.cs`.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-02 | **`*build-phase` (5 parallel clusters) → `*verify all` inline** | ✅ **2,225 tests pass, 0 errors, 23/23 screens clean in Hindi at both widths, menu bar MENU-OK in BOTH languages.** 🔴 **The headline find came from the §4b screenshot gate, not a test**: the primary chat screen renders English amid complete Devanagari because the strings were persisted into `TrMessage` at write time — the same class `REQ-UI-056` had just fixed elsewhere, but on real user history (`REQ-UI-059`). 🔴 **Two clusters' "model-facing, out of scope" judgment was disproven** by the running app rendering that text verbatim. 🔴 **`REQ-UI-057`'s recorded hypothesis was wrong**: not a fixed port and not class concurrency, but `Stop()`-then-`Close()` double prefix-unregistration re-binding the port on Unix — proven by stack trace, a deterministic harness, and errno forensics that also disproved the shared-collection fix this repo had proposed. 🔴 **`REQ-UI-056` unblocked on measured evidence**: the DB held 22 synthetic rows and **zero** `FailureReason` rows, and the proposed bare-key fix would have destroyed the numbers in `"2 ingested of 2 listed"` — so it ships as code+args. 🔴 **`REQ-UI-053` measured at 0/23.** ⚠ **My own errors**: I fanned five clusters at a single-session harness and it cost two clusters their live evidence; my first sweep analysis read non-existent top-level keys and reported a **vacuous** "0 findings" before I checked the schema | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-02 | **`*build-phase` (6 parallel clusters) → `*verify all` inline** | ✅ **2,180 tests pass, 0 errors, 23/23 screens clean in Hindi.** 🔴 Service-layer English 569 → 330 and the ratchet lowered to match. `CronDescriber` solved properly — Hindi reverses three joins, so it composes from whole localizable patterns with deliberately no English default overload. 🔴 **A live pre-existing defect found**: read-aloud spoke with no locale, so Hindi answers were **silently skipped by an English voice**. ⚠ **Three mutations came back GREEN and were reported** — including a `.resx` heredoc that silently never applied | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-02 | **`*build-phase` (2 clusters) → `*verify all` inline** | ✅ **2,073 tests pass.** 🔴 `REQ-UI-054` root cause proven and it was **not** the title collision hypothesised — `UIMenuBuilder` discards an entire menu that re-declares a system-owned key. 🔴 `REQ-UI-051` services now return invariant keys. ⚠ **Three recorded claims were wrong and were corrected** | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-01 | **`*build-phase` (4 clusters) → `*verify all` inline** | ✅ **2,055 tests pass, 23/23 screens clean in English AND Hindi.** 🔴 Markup localization **100%**, counter widened on purpose. 🔴 **`REQ-UI-053` DISPROVEN by probe.** ⚠ `/settings/backup` had shipped for days and had never been graded | [table](docs/TechieDesk-Checklist.md#requirements-status) |

## Deferred / future

- **`REQ-RAG-045/046`** (BRD-deferred by owner decision); **`REQ-RAG-044`**'s `PgVectorStore` test —
  needs Docker *installed*, not merely started.
- The `AgentToolHandler` / `EgressGate` audience split at the render seam (now `REQ-UI-059`).
- `Models.AgentStep` / `ToolResult` code carriers, so an Agent node's duplicate refusal row can
  localize.
- A `drv.py` revive-and-retry wrapper — three clusters independently rediscovered that WDA drops the
  session between nearly every command on this host.
- `run_sweep` sidebar selectors only work at `nav.DESK` (1600×1240); at 1440×980 the rail collapses
  and every selector misses with a confusing "link NOT FOUND".
