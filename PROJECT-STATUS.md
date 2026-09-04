---
project: TechieRag
stack: .NET 10/8 class libraries (NuGet) — TechieRag + TechieRag.Embedded (ONNX BGE-M3) + TechieRag.Telemetry (opt-in OTel) + TechieRag.Agents (Microsoft Agent Framework, planned) — and TechieDesk desktop app (MAUI Blazor Hybrid, moving to its own repository)
last_updated: 2026-09-03
current_phase: Build — TechieRag 11 open (Agents package + repo separation planned); TechieDesk 101 open
last_verified_build: PASS
last_verified_date: 2026-09-03
---

# TechieRag — Status

Configurable .NET 10 RAG library (NuGet) + TechieDesk product app. **This file is the dashboard only** —
per-REQ evidence lives in `docs/TechieRag-Checklist.md` (library) and `docs/TechieDesk-Checklist.md` (app),
Requirements Status tables; library defects in the per-library feedback files.

## Where I am

**Docs amended 2026-09-03 (`*amend-docs`, both BRDs, owner decision):** three library packages and a separate
product. TechieRag BRD-83…87 add the agentic retrieval contract in core, the **`TechieRag.Agents`** package on
Microsoft Agent Framework 1.20 with public seam adapters, three-package publishing, and **repository separation**
(TechieDesk leaves this repo and consumes the packages from NuGet). TechieDesk BRD-146/147 mirror that: own
repository with pinned packages, and the agent runtime on `TechieRag.Agents`. **Governance reversed:** library
work is ledgered in the TechieRag checklist again — `REQ-RAG-042/044/046/050/051/052` migrated there with their
status; TechieDesk `REQ-RAG-045` (MEAI interop) closed as superseded. Design: `docs/TechieRag.Agents-Proposal.md`.
**Library:** `REQ-FN-003/004` (publishing) stay `Verified`; the hosted nuget.org run still needs the owner's next
release. Library tests 723 pass / 0 fail (unchanged; no code touched today).
**App (`docs/TechieDesk-Checklist.md`):** 101 open of 181 after the migration and the two new rows.

## Next command to run

```
/TechieFlow:agents:flow-master *build-phase TechieRag        (OpenCode: /flow-master *build-phase TechieRag)
```
Targets `REQ-RAG-015` → `REQ-RAG-016` → `REQ-RAG-017` → `REQ-FN-005` (contract, Agents package, adapters, publish); `REQ-FN-006` / TechieDesk `REQ-FN-054` (repository separation) start with the owner-run git half.

## Open requirements

- **TechieRag: 11 open** of 49 — 5 new `Not Started` (`REQ-RAG-015/016/017`, `REQ-FN-005/006`, added
  2026-09-03) + 6 migrated from the app checklist (`REQ-RAG-042` 95%, `044` 85%, `050` 80%, `051` 95%,
  `052` 95% `Implemented`; `046` `Not Started`, deferred). `REQ-FN-003/004` closed 2026-09-03.
- **TechieDesk: 101 open** of 181 — 80 terminal (55 `Verified` + 25 `N/A`), 0 `FAIL`. Open: 39 `Implemented`,
  44 `Needs re-verify`, 9 `Blocked`, 4 `PARTIAL`, 2 `Planned`, 2 `Not Started` (`REQ-FN-054`, `REQ-RAG-053`
  added 2026-09-03), 1 `In Progress`. Per-REQ detail in that checklist's Requirements Status table.

## Known blockers

- 🔶 **`REQ-FN-003` — hosted run unexecuted** (owner): the version logic is proven by replaying the exact
  CI script locally and against live nuget.org; the first real dispatch against `v1.0.7` is the owner's to make.
- 🔶 **Repository separation (`REQ-FN-006` / TechieDesk `REQ-FN-054`)** (owner): creating the TechieDesk
  repository and moving history is git work; the agent half (package references, solution, deletion here) follows.
- 🔶 **TechieDesk `REQ-RAG-052` — RE-INGEST THE WHOLE CORPUS** (owner): vectors embedded before
  2026-08-04 are in a different space; the document library banner names the stale count.
- 🔶 **TechieDesk verification endpoints** (owner): `.tfcore/core-config.yaml → runtimeVerification.services`
  ships every key commented out; filling it unblocks the 44 `Needs re-verify` rows (`docs/VERIFICATION-ENDPOINTS.md`).
- 🔴 **TechieDesk `REQ-FN-053`** — not reproduced through the service layer; needs the running Catalyst head.
- 🔴 Owner-only: **`REQ-FN-043`** Apple signing identity; **`REQ-FN-035`** Windows platform sources;
  **TechieDesk `REQ-NFR-001`** TrBlazeUI PAT still untracked-and-unrotated.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-09-03 | `*amend-docs` TechieRag + TechieDesk (BRD-83…87, BRD-146/147; governance reversal, 6 rows migrated) | 📝 docs only — no build/test run; 4 docs + 2 checklists amended, HTML re-rendered | [lib](docs/TechieRag-Checklist.md#requirements-status) · [app](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-09-03 | `*triage-issues` → `*fix-issues` → verify chained (TechieRag REQ-FN-003, REQ-FN-004) | ✅ 2/2 Verified; 723 lib tests pass; version rules 9/9 (incl. live nuget.org); stranger walk 36 s; 3 misses opened + closed | [table](docs/TechieRag-Checklist.md#requirements-status) |
| 2026-08-05 | `*build-phase` — TechieDesk REQ-RAG-052 banner (owner-scoped), no verify run | ✅ 2,308 pass / 0 fail; banner proven on screen; REQ-FN-053 not started (stated) | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-04 | `*verify` ×6 (second run, consent granted) | ✅ 2,306 pass; 0 promoted, REQ-RAG-052 demoted 95% → `Needs re-verify` 80% | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-04 | `*build-phase` — REQ-UI-058 + REQ-UI-059 | ✅ 2,306 pass (+24); both to `Implemented`, nothing on screen | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-04 | `*verify REQ-RAG-025,044,049,051,052` | ✅ 2,282 pass; 1 promoted (REQ-RAG-025), 4 held, no screen driven | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-04 | `*build-phase` — RAG cluster | ✅ 2,282 pass (+57); BRD-106 reranker live; TR-RAG-044/045 fixed | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-02 | `*build-phase` (5 clusters) → `*verify all` | ✅ 2,225 pass; 23/23 screens clean in Hindi; REQ-UI-059 found by §4b | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-02 | `*build-phase` (6 clusters) → `*verify all` | ✅ 2,180 pass; service-layer English 569 → 330 | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-02 | `*build-phase` (2 clusters) → `*verify all` | ✅ 2,073 pass; REQ-UI-054 root cause proven | [table](docs/TechieDesk-Checklist.md#requirements-status) |
| 2026-08-01 | `*build-phase` (4 clusters) → `*verify all` | ✅ 2,055 pass; markup localization 100% | [table](docs/TechieDesk-Checklist.md#requirements-status) |

## Library feedback summary

- **TrBlazeUI — 28 entries.** TR-024 closed-with-workaround; TR-039, TR-038 open — `docs/TechieRag-TrBlazeUI-Feedback.md`.
- **TechieRag — 45 entries.** TR-RAG-042/043 open; TR-RAG-037/044/045 closed 2026-08-04/05 — `docs/TechieRag-TechieRag-Feedback.md`.

## Standards compliance

- Build 0 errors; library 723 tests pass / 41 skipped at `-p:Version=1.0.7` (2026-09-03); app 2,282 tests pass 2026-08-05, 36 skipped — 5 are gated live-Postgres tests that skip with a reason.
- Harness guards 8/8; markup localization 100%; greps clean (no `a`/`v`/`obj` prefixes, no underscore fields, no underscored test names).
- ⚠ Pre-existing: `MauiProgram.cs` `SCREAMING_SNAKE_CASE` env var; ~14 CS1591 in `TechieRagManager.cs`; NU5129 (`buildTransitive/` .targets path) on pack.

## Deferred / future

- **`REQ-RAG-046`** (deferred, now in the library checklist); **`REQ-RAG-044`**'s `PgVectorStore` test needs Docker installed, not merely started. `REQ-RAG-045` is superseded by `TechieRag.Agents` (`REQ-RAG-016/017`).
- `TechieRag.Agents` phase D items (proposal §7): MAF-native tool approval with resumable sessions, history over `IConversationStore`, MAF agent as a flow node, Azure OpenAI / OllamaSharp conveniences.
- `AgentToolHandler` / `EgressGate` audience split at the render seam (now `REQ-UI-059`); `Models.AgentStep` / `ToolResult` code carriers.
- A `drv.py` revive-and-retry wrapper (WDA drops the session between commands); `run_sweep` sidebar selectors only work at 1600×1240.
- Joint `TechieRag*` + `TrBlazeUI*` nuget.org ID-prefix reservation once both families are live (NUGET-PUBLISHING.md §7).
- The csproj `<Version>` (1.0.0) is a dev-only number now; consider aligning it with the latest tag after each release to keep local packs unambiguous.
