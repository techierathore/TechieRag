---
project: TechieRag
stack: .NET 10 class library (NuGet) + TechieRag.Embedded (ONNX BGE-M3) + Blazor Server sample (TrBlazeUI)
last_updated: 2026-07-02
current_phase: Handoff — all 36 REQs Verified / terminal
last_verified_build: PASS
last_verified_date: 2026-07-02
---

# TechieRag — Status

Configurable .NET 10 RAG library (NuGet), ~96% complete. All v1.1 + v2 features shipped and migrated
into the single checklist as `Done (pre-existing)`. **This file is the dashboard only** — per-REQ
evidence lives in `docs/TechieRag-Checklist.md` (Requirements Status table); library defects in the
per-library feedback files.

## Where I am

**Handoff — all 36 REQs `Verified` / `Done (pre-existing)`; awaiting manual UAT per the Usage Guide.**
The last 3 open rows were closed in the 2026-07-02 `*build-phase` (REQ-UI-011 mobile layout, REQ-RAG-012
Retry-After, REQ-FN-003 CI gates — see the Verification log + `docs/TechieRag-Checklist.md`). Handoff
artifacts are finalized: Usage Guide (test plan + runbook + smoke checklist), DevGuide, BRD §4 rollup
(F-RESIL / F-PKG / F-QDRANT now Done), and both per-library feedback files consolidated.

## Next command

```
Manual UAT per docs/TechieRag-UsageGuide.md smoke checklist.
```

Then hand each per-library feedback file to its team, and after UAT set `current_phase: Released`.

## Open requirements

- **None.** All 36 REQs are `Verified` / `Done (pre-existing)` (terminal). See `docs/TechieRag-Checklist.md#requirements-status`.
- Deferred (not BRD REQs): broader xUnit coverage beyond the resilience/provider suite; OpenTelemetry exporters.

## Known blockers

- None open. RAG retrieval no longer depends on the external `localhost:7997` embedding server — the bundled
  BGE-M3 model in the sample's bin output serves the Embedded source offline.
- ⚠ **Security:** the TrBlazeUI PAT is committed in plaintext in `nuget.config` (now gitignored but still tracked
  and in history) — untrack + revoke recommended.

## Library feedback

- **TrBlazeUI:** 0 major, 2 open minor (TR-003 SidebarInset min-width; TR-004 DataTable scroll wrapper — now
  known to be *inert* because `.overflow-x-auto` is purged from the shipped CSS, fixed app-side with inline
  style), 1 nice-to-have (TR-002 css 404) — `docs/TechieRag-TrBlazeUI-Feedback.md`
- **TechieRag:** 1 major open (TR-RAG-001 streaming RAG sources); TR-RAG-005 + TR-RAG-006 fixed; 3 minor —
  `docs/TechieRag-TechieRag-Feedback.md`

## Standards compliance

- 0 new underscore-field violations (new code is bare camelCase). Pre-existing `_lock`/`_currentInstance` in
  `TechieRagManager` left as-is (out of scope for this fix).

## Verification log

| Date | Phase | Result |
|------|-------|--------|
| 2026-06-25 | Discovery (day-1 brownfield) | Docs generated; roadmap+spec migrated; build PASS |
| 2026-07-01 | Build+Verify (`*build-phase` + follow-ups) | PAT unblocked → sample builds + boots; 6 UI fixes; overlay/SQLite/logging fixes; `*verify ui` → 12/13 UI Verified; REQ-UI-007 open (TR-RAG-005/006 logged) |
| 2026-07-01 | Build (`*build-phase`, TR-RAG-005/006) | LmStudio tool calling + async accessors fixed; core + test + sample build PASS; 3 new xUnit tests pass; **live agent-loop smoke PASS** (real `get_weather` tool call, AgentSteps fired). REQ-UI-007 → Implemented, pending verifier UI render |
| 2026-07-02 | Verify + fix (`*verify REQ-UI-007` → mobile-overflow fix → re-verify) | **Execution Trace VERIFIED LIVE in the UI** (real tool steps, bundled BGE-M3, no download); 390px overflow found on /tool-demo + /ingestion, root-caused (TR-003 SidebarInset min-width + TR-004 DataTable sr-only escape) and FIXED same day; all routes `scrollWidth==390` @390; **REQ-UI-007 + REQ-UI-004 → Verified 100%** — [details](docs/TechieRag-Checklist.md#requirements-status) |
| 2026-07-02 | Full verify (`*verify all`, 36 REQs, live LM Studio + live Qdrant 1.15.5) | Builds 0-err ×3, xUnit 3/3, Playwright 37/37 after gate/selector fixes. LIVE-proven: ingest write cycle, Auto-RAG streaming + sources, token dashboard non-zero, LLM connection test 912ms, Qdrant collection create/delete + 1,043-pt browse/detail. Opened: **REQ-UI-011 ⚠ visual @390** (container row off-canvas), **REQ-RAG-012 PARTIAL** (Retry-After never parsed), **REQ-FN-003 PARTIAL** (NuGet.org job commented out; CI tests non-blocking) — [details](docs/TechieRag-Checklist.md#requirements-status) |
| 2026-07-02 | Build+fix (`*build-phase` FIX, last 3 rows) | Builds 0-err ×3 (+ sample), **xUnit 11/11**. **REQ-RAG-012 → Verified** (Retry-After parsed via new `LlmHttpGuard`+`LlmRateLimitException` in all 6 providers; 4 Retry-After unit tests). **REQ-UI-011 → Verified** (mobile @390 off-canvas fixed — root cause: `.overflow-x-auto` purged/inert + `MapStaticAssets` 0-byte CSS to br clients; real fix = inline style on QdrantAdmin DataTable wrappers; live `scrollWidth` 496→390 with running container). **REQ-FN-003 → Verified (static)** (publish-nuget.yml: test-gate now blocking, NuGet.org job un-commented + secret-gated). **All 36 REQs Verified/terminal → Handoff** — [details](docs/TechieRag-Checklist.md#requirements-status) |
| 2026-07-02 | Handoff (`*handoff-phase`) | **Ship-ready for UAT.** All 36 REQs Verified/terminal; build PASS ×3 + sample; xUnit 11/11. Finalized: Usage Guide (test plan + runbook + smoke), DevGuide, BRD §4 rollup (F-RESIL/F-PKG/F-QDRANT → Done; test-suite → Partial 40%), both feedback files consolidated (open: TR-RAG-001; TR-002/003/004 minor). HTMLs re-rendered. Open for library teams: TrBlazeUI TR-003/TR-004, TechieRag TR-RAG-001 — [details](docs/TechieRag-Checklist.md#requirements-status) |

## Deferred / future

- Formal automated xUnit suite (now partial — LmStudio provider tool tests added this session).
- OpenTelemetry metrics + distributed tracing exporters.
